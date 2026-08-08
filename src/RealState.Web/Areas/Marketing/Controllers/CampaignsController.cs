using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Marketing.Models;

namespace RealState.Web.Areas.Marketing.Controllers;

[Area("Marketing")]
[Authorize(Policy = PermissionNames.CampaignsView)]
public class CampaignsController : Controller
{
    private readonly IApplicationDbContext _db;

    public CampaignsController(IApplicationDbContext db) => _db = db;

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // ---------- Dashboard ----------
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var rows = await BuildRowsAsync(ct);
        var vm = new MarketingDashboardVm
        {
            Campaigns = rows,
            TotalLeads = rows.Sum(r => r.Leads),
            TotalReach = rows.Sum(r => r.Reach),
            TotalCost = rows.Sum(r => r.Cost),
            TotalSales = rows.Sum(r => r.Sales),
            TotalReservations = rows.Sum(r => r.Reservations),
            TotalBudget = rows.Sum(r => r.Budget),
            DefaultCampaignId = rows.FirstOrDefault()?.Id
        };

        var totalLeads = vm.TotalLeads;
        vm.Sources = rows.GroupBy(r => r.Platform)
            .Select(g => new { g.Key, Leads = g.Sum(x => x.Leads) })
            .Where(x => x.Leads > 0)
            .OrderByDescending(x => x.Leads)
            .Select(x => new SourceSlice(x.Key.Ar(), x.Leads, totalLeads <= 0 ? 0 : (int)Math.Round(100.0 * x.Leads / totalLeads)))
            .ToList();

        return View(vm);
    }

    // ---------- Add / edit campaign (modal) ----------
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        if (!Can(id is null ? PermissionNames.CampaignsCreate : PermissionNames.CampaignsEdit)) return Forbid();
        if (id is null) return PartialView("_CampaignForm", new CampaignFormModel());

        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        return PartialView("_CampaignForm", new CampaignFormModel
        {
            Id = c.Id,
            Name = c.Name,
            Platform = c.Platform,
            Type = c.Type,
            Objective = c.Objective,
            Status = c.Status,
            StartDate = c.StartDate,
            EndDate = c.EndDate,
            Budget = c.Budget,
            Notes = c.Notes
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(CampaignFormModel model, CancellationToken ct)
    {
        if (!Can(model.Id == Guid.Empty ? PermissionNames.CampaignsCreate : PermissionNames.CampaignsEdit)) return Forbid();
        if (!ModelState.IsValid) return PartialView("_CampaignForm", model);

        if (model.Id == Guid.Empty)
        {
            _db.Campaigns.Add(new Campaign
            {
                Name = model.Name,
                Platform = model.Platform,
                Type = model.Type,
                Objective = model.Objective,
                Status = model.Status,
                StartDate = model.StartDate,
                EndDate = model.EndDate,
                Budget = model.Budget,
                Notes = model.Notes
            });
        }
        else
        {
            var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (c is null) return NotFound();
            c.Name = model.Name;
            c.Platform = model.Platform;
            c.Type = model.Type;
            c.Objective = model.Objective;
            c.Status = model.Status;
            c.StartDate = model.StartDate;
            c.EndDate = model.EndDate;
            c.Budget = model.Budget;
            c.Notes = model.Notes;
        }

        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    // ---------- Insert latest reading -> store delta (modal) ----------
    [HttpGet]
    [Authorize(Policy = PermissionNames.CampaignsEdit)]
    public async Task<IActionResult> Update(Guid campaignId, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        if (c is null) return NotFound();

        var cur = await CurrentTotalsAsync(campaignId, ct);
        var latestDate = await _db.CampaignUpdates
            .Where(u => u.CampaignId == campaignId)
            .OrderByDescending(u => u.Date)
            .Select(u => (DateTime?)u.Date)
            .FirstOrDefaultAsync(ct);
        return PartialView("_CampaignUpdateForm", new CampaignUpdateFormModel
        {
            CampaignId = campaignId,
            CampaignName = c.Name,
            Date = DateTime.Today,
            LatestDate = latestDate,
            CurrentReach = cur.Reach,
            CurrentLeads = cur.Leads,
            CurrentCost = cur.Cost,
            CurrentSales = cur.Sales,
            CurrentReservations = cur.Reservations,
            // Pre-fill inputs with current totals so the user only bumps them up.
            ReachTotal = cur.Reach,
            LeadsTotal = cur.Leads,
            CostTotal = cur.Cost,
            SalesTotal = cur.Sales,
            ReservationsTotal = cur.Reservations
        });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.CampaignsEdit)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(CampaignUpdateFormModel model, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == model.CampaignId, ct);
        if (c is null) return NotFound();

        var cur = await CurrentTotalsAsync(model.CampaignId, ct);
        model.CurrentReach = cur.Reach; model.CurrentLeads = cur.Leads; model.CurrentCost = cur.Cost;
        model.CurrentSales = cur.Sales; model.CurrentReservations = cur.Reservations;

        // The entered values are the new cumulative totals; they cannot be below the current ones.
        if (model.ReachTotal < cur.Reach) ModelState.AddModelError(nameof(model.ReachTotal), "لا يمكن أن يقل عن الإجمالي الحالي.");
        if (model.LeadsTotal < cur.Leads) ModelState.AddModelError(nameof(model.LeadsTotal), "لا يمكن أن يقل عن الإجمالي الحالي.");
        if (model.CostTotal < cur.Cost) ModelState.AddModelError(nameof(model.CostTotal), "لا يمكن أن يقل عن الإجمالي الحالي.");
        if (model.SalesTotal < cur.Sales) ModelState.AddModelError(nameof(model.SalesTotal), "لا يمكن أن يقل عن الإجمالي الحالي.");
        if (model.ReservationsTotal < cur.Reservations) ModelState.AddModelError(nameof(model.ReservationsTotal), "لا يمكن أن يقل عن الإجمالي الحالي.");

        if (!ModelState.IsValid) return PartialView("_CampaignUpdateForm", model);

        // Store the difference as a new record so each campaign keeps a running history.
        _db.CampaignUpdates.Add(new CampaignUpdate
        {
            CampaignId = model.CampaignId,
            Date = model.Date,
            Reach = model.ReachTotal - cur.Reach,
            Leads = model.LeadsTotal - cur.Leads,
            Cost = model.CostTotal - cur.Cost,
            Sales = model.SalesTotal - cur.Sales,
            Reservations = model.ReservationsTotal - cur.Reservations,
            Notes = model.Notes
        });
        await _db.SaveChangesAsync(ct);
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.CampaignsDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();

        _db.Campaigns.Remove(c); // soft-delete via interceptor (its updates history is retained)
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم حذف الحملة «{c.Name}».";
        return RedirectToAction(nameof(Index));
    }

    // ---------- Show-all table (search) for the popup ----------
    [HttpGet]
    public async Task<IActionResult> All(string? q, CancellationToken ct)
    {
        var rows = await BuildRowsAsync(ct);
        if (!string.IsNullOrWhiteSpace(q))
            rows = rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                   || r.Platform.Ar().Contains(q)).ToList();
        ViewData["q"] = q;
        return PartialView("_CampaignsTable", rows);
    }

    // ---------- Chart data (compare campaigns) ----------
    [HttpGet]
    public async Task<IActionResult> ChartData([FromQuery] Guid[] ids, CancellationToken ct)
    {
        ids ??= Array.Empty<Guid>();
        var series = new List<CampaignSeries>();

        // No specific campaigns selected -> overall trend across all campaigns (reach + leads only).
        if (ids.Length == 0)
        {
            series.Add(await BuildAggregateSeriesAsync(ct));
        }
        else
        {
            foreach (var id in ids.Distinct().Take(6))
                series.Add(await BuildSeriesAsync(id, ct));
        }
        return Json(series);
    }

    // ---------- Excel export ----------
    [HttpGet]
    public async Task<IActionResult> ExportExcel(CancellationToken ct)
    {
        var rows = await BuildRowsAsync(ct);
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("الحملات");
        ws.RightToLeft = true;

        string[] headers = { "اسم الحملة", "المنصة", "الحالة", "الوصول", "Leads", "التكلفة", "المبيعات", "الحجوزات", "الميزانية", "آخر تحديث" };
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;

        int r = 2;
        foreach (var row in rows)
        {
            ws.Cell(r, 1).Value = row.Name;
            ws.Cell(r, 2).Value = row.Platform.Ar();
            ws.Cell(r, 3).Value = row.Status.Ar();
            ws.Cell(r, 4).Value = row.Reach;
            ws.Cell(r, 5).Value = row.Leads;
            ws.Cell(r, 6).Value = row.Cost;
            ws.Cell(r, 7).Value = row.Sales;
            ws.Cell(r, 8).Value = row.Reservations;
            ws.Cell(r, 9).Value = row.Budget;
            ws.Cell(r, 10).Value = row.LastUpdate?.ToString("yyyy/MM/dd") ?? "-";
            r++;
        }
        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"campaigns-{DateTime.Now:yyyyMMdd}.xlsx");
    }

    // ---------- Print-friendly view (Save as PDF from the browser) ----------
    [HttpGet]
    public async Task<IActionResult> Print(CancellationToken ct)
        => View("Print", await BuildRowsAsync(ct));

    // ---------- helpers ----------
    private async Task<List<CampaignRow>> BuildRowsAsync(CancellationToken ct)
    {
        var campaigns = await _db.Campaigns.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

        var sums = await _db.CampaignUpdates
            .GroupBy(u => u.CampaignId)
            .Select(g => new
            {
                CampaignId = g.Key,
                Reach = g.Sum(x => x.Reach),
                Leads = g.Sum(x => x.Leads),
                Cost = g.Sum(x => x.Cost),
                Sales = g.Sum(x => x.Sales),
                Res = g.Sum(x => x.Reservations),
                Count = g.Count(),
                Last = (DateTime?)g.Max(x => x.Date)
            })
            .ToListAsync(ct);
        var byId = sums.ToDictionary(s => s.CampaignId);

        return campaigns.Select(c =>
        {
            byId.TryGetValue(c.Id, out var s);
            return new CampaignRow
            {
                Id = c.Id,
                Name = c.Name,
                Platform = c.Platform,
                Status = c.Status,
                Budget = c.Budget,
                Reach = s?.Reach ?? 0,
                Leads = s?.Leads ?? 0,
                Cost = s?.Cost ?? 0,
                Sales = s?.Sales ?? 0,
                Reservations = s?.Res ?? 0,
                UpdatesCount = s?.Count ?? 0,
                LastUpdate = s?.Last
            };
        }).ToList();
    }

    private async Task<(int Reach, int Leads, decimal Cost, decimal Sales, int Reservations)> CurrentTotalsAsync(Guid campaignId, CancellationToken ct)
    {
        var updates = await _db.CampaignUpdates.Where(u => u.CampaignId == campaignId).ToListAsync(ct);
        return (updates.Sum(u => u.Reach), updates.Sum(u => u.Leads), updates.Sum(u => u.Cost),
                updates.Sum(u => u.Sales), updates.Sum(u => u.Reservations));
    }

    /// <summary>Overall reach + leads trend across every campaign (independent of sales), by date.</summary>
    private async Task<CampaignSeries> BuildAggregateSeriesAsync(CancellationToken ct)
    {
        var updates = await _db.CampaignUpdates.OrderBy(u => u.Date).ToListAsync(ct);

        var labels = new List<string>();
        var reach = new List<int>();
        var leads = new List<int>();
        int cr = 0, cl = 0;
        foreach (var g in updates.GroupBy(u => u.Date.Date).OrderBy(g => g.Key))
        {
            cr += g.Sum(u => u.Reach);
            cl += g.Sum(u => u.Leads);
            labels.Add(g.Key.ToString("dd/MM"));
            reach.Add(cr);
            leads.Add(cl);
        }
        return new CampaignSeries(Guid.Empty, "الإجمالي", labels, reach, leads);
    }

    private async Task<CampaignSeries> BuildSeriesAsync(Guid campaignId, CancellationToken ct)
    {
        var c = await _db.Campaigns.FirstOrDefaultAsync(x => x.Id == campaignId, ct);
        var updates = await _db.CampaignUpdates
            .Where(u => u.CampaignId == campaignId)
            .OrderBy(u => u.Date)
            .ToListAsync(ct);

        var labels = new List<string>();
        var reach = new List<int>();
        var leads = new List<int>();
        int cr = 0, cl = 0;
        foreach (var u in updates)
        {
            cr += u.Reach; cl += u.Leads;
            labels.Add(u.Date.ToString("dd/MM"));
            reach.Add(cr);
            leads.Add(cl);
        }
        return new CampaignSeries(campaignId, c?.Name ?? "", labels, reach, leads);
    }
}
