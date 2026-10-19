using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Sales.Models;

namespace RealState.Web.Areas.Sales.Controllers;

[Area("Sales")]
[Authorize(Policy = PermissionNames.SalesView)]
public class SalesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly RealState.Application.Accounting.IAccountingService _accounting;
    private readonly RealState.Application.Accounting.ISafeBalanceGuard _guard;

    public SalesController(IApplicationDbContext db, ICurrentUserService currentUser,
        RealState.Application.Accounting.IAccountingService accounting, RealState.Application.Accounting.ISafeBalanceGuard guard)
    {
        _db = db;
        _currentUser = currentUser;
        _accounting = accounting;
        _guard = guard;
    }

    private bool Can(string permission) => User.HasClaim("permission", permission);

    // Sales summary landing page (analytics cards + charts for all sales).
    public async Task<IActionResult> Summary(CancellationToken ct)
        => View(await BuildSummaryAsync(ct));

    [HttpGet]
    public async Task<IActionResult> SummaryPrint(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("SummaryPrint", await BuildSummaryAsync(ct));
    }

    private static readonly string[] ArMonths =
        { "", "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };

    private async Task<SalesSummaryVm> BuildSummaryAsync(CancellationToken ct)
    {
        // Cancelled contracts (فسخ) are no longer sales — their value and collections are left out.
        var contracts = await _db.SaleContracts.Where(s => !s.IsCancelled).Select(s => new { s.Id, s.ProjectId, s.TotalPrice, s.ContractDate }).ToListAsync(ct);
        var activeIds = _db.SaleContracts.Where(s => !s.IsCancelled).Select(s => s.Id);
        var paidById = (await _db.Installments.Where(i => activeIds.Contains(i.SaleContractId)).GroupBy(i => i.SaleContractId)
            .Select(g => new { g.Key, Paid = g.Sum(x => x.PaidAmount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Paid);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);

        var totalValue = contracts.Sum(c => c.TotalPrice);
        // Down payment is scheduled as an installment now, so "collected" = paid installments only.
        var collected = paidById.Values.Sum();

        var byProject = contracts.GroupBy(c => c.ProjectId)
            .Select(g => new ProjectSales(projNames.GetValueOrDefault(g.Key, "—"), g.Count(), g.Sum(x => x.TotalPrice)))
            .OrderByDescending(x => x.Value).Take(6).ToList();

        var byMonth = contracts
            .GroupBy(c => new { c.ContractDate.Year, c.ContractDate.Month })
            .OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Month)
            .Select(g => new MonthlySales(ArMonths[g.Key.Month], g.Sum(x => x.TotalPrice)))
            .TakeLast(6).ToList();

        // Month-over-month figures (current vs previous calendar month).
        var now = ClientTime.Today;
        var thisStart = new DateTime(now.Year, now.Month, 1);
        var nextStart = thisStart.AddMonths(1);
        var prevStart = thisStart.AddMonths(-1);
        decimal SalesIn(DateTime a, DateTime b) => contracts.Where(c => c.ContractDate >= a && c.ContractDate < b).Sum(c => c.TotalPrice);
        int CountIn(DateTime a, DateTime b) => contracts.Count(c => c.ContractDate >= a && c.ContractDate < b);

        var collectedThis = await _db.Installments.Where(i => activeIds.Contains(i.SaleContractId) && i.PaidDate >= thisStart && i.PaidDate < nextStart).SumAsync(i => (decimal?)i.PaidAmount, ct) ?? 0;
        var collectedPrev = await _db.Installments.Where(i => activeIds.Contains(i.SaleContractId) && i.PaidDate >= prevStart && i.PaidDate < thisStart).SumAsync(i => (decimal?)i.PaidAmount, ct) ?? 0;

        // Sales pipeline funnel from CRM leads (Lost is excluded from the funnel).
        var leadCounts = (await _db.Leads.GroupBy(l => l.Status).Select(g => new { g.Key, C = g.Count() }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.C);
        var pipeline = new List<PipelineStage>
        {
            new("عملاء محتملين", leadCounts.GetValueOrDefault(LeadStatus.New)),
            new("تم التواصل", leadCounts.GetValueOrDefault(LeadStatus.Contacted)),
            new("مؤهل", leadCounts.GetValueOrDefault(LeadStatus.Qualified)),
            new("عرض / تفاوض", leadCounts.GetValueOrDefault(LeadStatus.Proposal)),
            new("تعاقد", leadCounts.GetValueOrDefault(LeadStatus.Won)),
        };

        // Latest contracts (stands in for the mockup's "آخر الحجوزات").
        var latestRaw = await _db.SaleContracts.Where(s => !s.IsCancelled).OrderByDescending(s => s.ContractDate)
            .Select(s => new { s.Code, s.CustomerId, s.ProjectId, s.TotalPrice, s.ContractDate }).Take(6).ToListAsync(ct);
        var latest = latestRaw
            .Select(s => new RecentContract(s.Code, custNames.GetValueOrDefault(s.CustomerId, "—"), projNames.GetValueOrDefault(s.ProjectId, "—"), s.TotalPrice, s.ContractDate))
            .ToList();

        return new SalesSummaryVm
        {
            ContractsCount = contracts.Count,
            TotalValue = totalValue,
            TotalCollected = collected,
            TotalRemaining = totalValue - collected,
            CustomersCount = custNames.Count,
            SalespersonsCount = await _db.Employees.CountAsync(e => e.Type == EmployeeType.Salesperson, ct),
            UnitsSold = await _db.ProjectUnits.CountAsync(u => u.Status == UnitStatus.Sold, ct),
            SalesThisMonth = SalesIn(thisStart, nextStart),
            SalesPrevMonth = SalesIn(prevStart, thisStart),
            CollectedThisMonth = collectedThis,
            CollectedPrevMonth = collectedPrev,
            ContractsThisMonth = CountIn(thisStart, nextStart),
            ContractsPrevMonth = CountIn(prevStart, thisStart),
            NewCustomersThisMonth = await _db.Customers.CountAsync(c => c.CreatedAt >= thisStart && c.CreatedAt < nextStart, ct),
            NewCustomersPrevMonth = await _db.Customers.CountAsync(c => c.CreatedAt >= prevStart && c.CreatedAt < thisStart, ct),
            ByProject = byProject,
            ByMonth = byMonth,
            Pipeline = pipeline,
            Latest = latest,
        };
    }

    // The contracts list, with a contract-date range filter (defaults to today on a fresh open).
    public async Task<IActionResult> Index(DateTime? from, DateTime? to, List<SaleContractState>? status, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        var vm = new SalesListVm { Rows = await BuildRowsAsync(from, to, status, ct), From = from, To = to, Status = status ?? new() };

        // unit -> price map so the create modal can pre-fill the total from the chosen unit.
        // Materialize then key by ToString() client-side (lowercase) to match the unit option values.
        var priceUnits = await _db.ProjectUnits.Where(u => u.Status != UnitStatus.Sold)
            .Select(u => new { u.Id, u.Price }).ToListAsync(ct);
        ViewBag.UnitPrices = priceUnits.ToDictionary(u => u.Id.ToString(), u => u.Price);

        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> PrintList(DateTime? from, DateTime? to, List<SaleContractState>? status, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("PrintList", new SalesListVm { Rows = await BuildRowsAsync(from, to, status, ct), From = from, To = to, Status = status ?? new() });
    }

    // Create (id null) or edit (id set) — shown in a modal popup.
    [HttpGet]
    public async Task<IActionResult> Form(Guid? id, CancellationToken ct)
    {
        var isEdit = id.HasValue && id.Value != Guid.Empty;
        if (!Can(isEdit ? PermissionNames.SalesEdit : PermissionNames.SalesCreate)) return Forbid();
        if (!isEdit) return PartialView("_SaleForm", await FillAsync(new SaleFormModel(), ct));

        var c = await _db.SaleContracts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();
        if (c.IsCancelled)
            return Content($"<div class=\"status-message\" style=\"color:var(--critical);\">عقد البيع «{c.Code}» مفسوخ — لا يمكن تعديله.</div>", "text/html");
        var insts = await _db.Installments.Where(i => i.SaleContractId == c.Id).ToListAsync(ct);
        var model = new SaleFormModel
        {
            Id = c.Id, CustomerId = c.CustomerId, ProjectId = c.ProjectId, UnitId = c.UnitId,
            ContractDate = c.ContractDate, ReceiveDate = c.ReceiveDate,
            TotalPrice = c.TotalPrice, Step = c.Step, Notes = c.Notes,
            CustomerName = await _db.Customers.Where(x => x.Id == c.CustomerId).Select(x => x.FullName).FirstOrDefaultAsync(ct),
            UnitName = await _db.ProjectUnits.Where(x => x.Id == c.UnitId)
                .Select(x => x.Name + (string.IsNullOrEmpty(x.Number) ? "" : $" ({x.Number})")).FirstOrDefaultAsync(ct),
            LockFinancials = insts.Any(i => i.PaidAmount > 0)
        };
        LoadSchedule(model, c, insts);
        return PartialView("_SaleForm", await FillAsync(model, ct));
    }

    // Splits a saved schedule back into the form: hand-entered rows, plus the auto settings (count / first date)
    // that regenerate the auto rows; the full schedule is also kept for the read-only (locked) view.
    private static void LoadSchedule(SaleFormModel model, SaleContract c, List<Installment> insts)
    {
        var ordered = insts.OrderBy(i => i.DueDate).ThenBy(i => i.Type).ThenBy(i => i.Number).ToList();
        var manual = ordered.Where(i => !i.IsAuto).ToList();
        var auto = ordered.Where(i => i.IsAuto).ToList();
        model.ManualDates = manual.Select(i => (DateTime?)i.DueDate).ToList();
        model.ManualAmounts = manual.Select(i => (decimal?)i.Amount).ToList();
        model.ManualTypes = manual.Select(i => (InstallmentType?)i.Type).ToList();
        model.InstallmentsCount = auto.Count;
        model.FirstInstallmentDate = auto.FirstOrDefault()?.DueDate ?? c.ContractDate;
        model.LockedSchedule = ordered.Select(i => new SaleScheduleRow(i.DueDate, i.Amount, i.Type, i.IsAuto)).ToList();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Form(SaleFormModel model, CancellationToken ct)
    {
        var isEdit = model.Id != Guid.Empty;
        if (!Can(isEdit ? PermissionNames.SalesEdit : PermissionNames.SalesCreate)) return Forbid();

        // Validates the manual rows and that manual + auto rows add up to the total price.
        var schedule = BuildSchedule(model);

        if (isEdit) return await EditContractAsync(model, schedule, ct);

        var unit = model.UnitId is null ? null : await _db.ProjectUnits.FirstOrDefaultAsync(u => u.Id == model.UnitId, ct);
        if (unit is null) ModelState.AddModelError(nameof(model.UnitId), "الوحدة غير موجودة.");
        else if (unit.Status == UnitStatus.Sold) ModelState.AddModelError(nameof(model.UnitId), "الوحدة مباعة بالفعل.");

        if (!ModelState.IsValid) return PartialView("_SaleForm", await FillAsync(model, ct));

        var contract = new SaleContract
        {
            Code = await NextCodeAsync(model.ContractDate.Year, ct),
            CustomerId = model.CustomerId!.Value,
            ProjectId = unit!.ProjectId,   // derived from the chosen unit
            UnitId = model.UnitId!.Value,
            ContractDate = model.ContractDate,
            ReceiveDate = model.ReceiveDate,
            TotalPrice = model.TotalPrice,
            Step = model.Step,
            Notes = model.Notes
        };
        _db.SaleContracts.Add(contract);
        ApplySchedule(contract, schedule);

        unit!.Status = UnitStatus.Sold; // reserve the unit
        await _accounting.PostSaleContractAsync(contract, ct);   // Dr A/R  Cr Sales Revenue
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم إنشاء عقد البيع «{contract.Code}».";
        return Json(new { ok = true, redirect = Url.Action("Details", new { id = contract.Id }) });
    }

    private async Task<IActionResult> EditContractAsync(SaleFormModel model, List<SaleScheduleRow> schedule, CancellationToken ct)
    {
        var c = await _db.SaleContracts.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
        if (c is null) return NotFound();
        if (c.IsCancelled) return Json(new { ok = false, error = $"عقد البيع «{c.Code}» مفسوخ — لا يمكن تعديله." });
        var insts = await _db.Installments.Where(i => i.SaleContractId == c.Id).ToListAsync(ct);
        var locked = insts.Any(i => i.PaidAmount > 0);   // a collection exists → financials are frozen

        if (locked) ModelState.Remove(ManualScheduleKey);   // the schedule isn't touched once collected
        if (!ModelState.IsValid)
        {
            model.LockFinancials = locked;
            if (locked) LoadSchedule(model, c, insts);   // the locked view shows the saved schedule (it isn't posted)
            return PartialView("_SaleForm", await FillAsync(model, ct));
        }

        // Always editable
        c.ContractDate = model.ContractDate;
        c.ReceiveDate = model.ReceiveDate;
        c.Notes = model.Notes;

        // Financials + installment schedule can only change while nothing has been collected.
        if (!locked)
        {
            c.TotalPrice = model.TotalPrice;
            c.Step = model.Step;
            _db.Installments.RemoveRange(insts);
            ApplySchedule(c, schedule);
        }

        // Keep the sale journal entry in sync with the (possibly changed) contract value.
        await _accounting.RemoveObligationAsync("SaleContract", c.Id, ct);
        await _accounting.PostSaleContractAsync(c, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم تحديث عقد البيع «{c.Code}».";
        return Json(new { ok = true, redirect = Url.Action("Details", new { id = c.Id }) });
    }

    private const string ManualScheduleKey = "ManualSchedule";

    /// <summary>
    /// The contract's full payment schedule, sorted by date: the hand-entered rows (date, amount, type) plus
    /// what they leave of the total price (المتبقي) split into <see cref="SaleFormModel.InstallmentsCount"/> auto
    /// «قسط» rows from the first date, one <see cref="SaleFormModel.Step"/> apart (the last takes the rounding).
    /// Fully empty manual rows are ignored; problems are reported on <see cref="ManualScheduleKey"/>.
    /// </summary>
    private List<SaleScheduleRow> BuildSchedule(SaleFormModel model)
    {
        var rows = new List<SaleScheduleRow>();
        var count = Math.Max(model.ManualDates.Count, model.ManualAmounts.Count);
        for (var i = 0; i < count; i++)
        {
            var date = i < model.ManualDates.Count ? model.ManualDates[i] : null;
            var amount = i < model.ManualAmounts.Count ? model.ManualAmounts[i] : null;
            var type = (i < model.ManualTypes.Count ? model.ManualTypes[i] : null) ?? InstallmentType.Installment;
            if (date is null && amount is null) continue;
            if (date is null || amount is null || amount <= 0)
            {
                ModelState.AddModelError(ManualScheduleKey, $"الصف {i + 1}: أدخل التاريخ وقيمة أكبر من صفر.");
                continue;
            }
            rows.Add(new SaleScheduleRow(date.Value.Date, Math.Round(amount.Value, 2), type, false));
        }

        var remaining = model.TotalPrice - rows.Sum(r => r.Amount);
        if (remaining < -0.005m)
            ModelState.AddModelError(ManualScheduleKey,
                $"مجموع الدفعات ({rows.Sum(r => r.Amount):N2}) يتجاوز السعر الإجمالي ({model.TotalPrice:N2} ج.م) بمقدار {-remaining:N2}.");
        else if (remaining > 0.005m && model.InstallmentsCount <= 0)
            ModelState.AddModelError(ManualScheduleKey,
                $"المتبقي ({remaining:N2} ج.م) غير موزّع — أدخل عدد الأقساط التلقائية أو أضف دفعات يدوية.");
        else if (remaining > 0.005m)
        {
            var n = model.InstallmentsCount;
            var per = Math.Round(remaining / n, 2);
            var months = model.Step.Months();
            for (var i = 1; i <= n; i++)
                rows.Add(new SaleScheduleRow(model.FirstInstallmentDate.Date.AddMonths(months * (i - 1)),
                    i == n ? remaining - per * (n - 1) : per, InstallmentType.Installment, true));
        }

        if (rows.Count > 600) ModelState.AddModelError(ManualScheduleKey, "عدد الدفعات بين 0 و600.");
        // Same date: down payment first, then installment, then maintenance; hand-entered before auto.
        return rows.OrderBy(r => r.DueDate).ThenBy(r => r.Type switch
        {
            InstallmentType.DownPayment => 0, InstallmentType.Installment => 1, _ => 2
        }).ThenBy(r => r.IsAuto).ToList();
    }

    // Saves the schedule as the contract's installments (numbered within each type, in date order) and
    // refreshes the contract's summary fields from it.
    private void ApplySchedule(SaleContract contract, List<SaleScheduleRow> schedule)
    {
        var seq = new Dictionary<InstallmentType, int>();
        foreach (var r in schedule)
        {
            seq[r.Type] = seq.GetValueOrDefault(r.Type) + 1;
            _db.Installments.Add(new Installment
            {
                SaleContractId = contract.Id, Number = seq[r.Type], Type = r.Type, IsAuto = r.IsAuto,
                DueDate = r.DueDate, Amount = r.Amount
            });
        }
        contract.DownPayment = schedule.Where(r => r.Type == InstallmentType.DownPayment).Sum(r => r.Amount);
        contract.InstallmentsCount = schedule.Count(r => r.Type == InstallmentType.Installment);
        contract.IsManualSchedule = !schedule.Any(r => r.IsAuto);
    }

    public async Task<IActionResult> Details(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        ViewBag.Installments = await _db.Installments.Where(i => i.SaleContractId == id).OrderBy(i => i.DueDate).ThenBy(i => i.Type).ThenBy(i => i.Number).ToListAsync(ct);
        ViewBag.Refunds = await RefundsAsync(id, ct);
        // Receiving safes for the per-installment «تحصيل» (same flow as the collections page).
        if (!c.IsCancelled && Can(PermissionNames.CollectionsView) && Can(PermissionNames.CollectionsCollect))
            ViewBag.Safes = await _db.Safes.Where(s => s.IsActive)
                .OrderBy(s => s.Name).Select(s => new { id = s.Id, name = s.Name }).ToListAsync(ct);
        // Carry the caller's date filter so the "رجوع" link can return to the same filtered list.
        ViewBag.From = from;
        ViewBag.To = to;
        return View(c);
    }

    private async Task<List<SaleRefundRow>> RefundsAsync(Guid contractId, CancellationToken ct)
    {
        var safeNames = await _db.Safes.ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        return (await _db.SafeTransactions.Where(t => t.Source == TxnSource.ContractCancelRefund && t.SaleContractId == contractId)
                .OrderBy(t => t.OccurredAt).ThenBy(t => t.Serial).ToListAsync(ct))
            .Select(t => new SaleRefundRow(t.Id, t.Serial, t.OccurredAt, safeNames.GetValueOrDefault(t.SafeId, "—"), t.Amount, t.Description))
            .ToList();
    }

    // ---------------- فسخ التعاقد (contract cancellation) ----------------
    // The contract stays on file, labelled «مفسوخ»: its unit goes back to «متاحة», what's left unpaid of its schedule is
    // dropped, the sale is reversed in the ledger, and what the customer had paid — less the cancellation fee (a % of
    // it, may be 0) — is owed back to them (the customer turns دائن), refunded part by part from the contract page.

    [HttpGet]
    [Authorize(Policy = PermissionNames.SalesCancel)]
    public async Task<IActionResult> CancelForm(Guid id, CancellationToken ct)
    {
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        if (c.IsCancelled)
            return Content($"<div class=\"status-message\" style=\"color:var(--critical);\">عقد البيع «{c.Code}» مفسوخ بالفعل.</div>", "text/html");
        return PartialView("_CancelForm", await FillCancelAsync(new SaleCancelFormModel { Id = c.Id }, c, ct));
    }

    private async Task<SaleCancelFormModel> FillCancelAsync(SaleCancelFormModel model, SaleContract c, CancellationToken ct)
    {
        model.Code = c.Code;
        model.CustomerName = c.Customer?.FullName ?? "—";
        model.UnitName = c.Unit is null ? "—" : c.Unit.Name + (string.IsNullOrEmpty(c.Unit.Number) ? "" : $" ({c.Unit.Number})");
        model.TotalPrice = c.TotalPrice;
        model.Paid = await _db.Installments.Where(i => i.SaleContractId == c.Id).SumAsync(i => (decimal?)i.PaidAmount, ct) ?? 0m;
        return model;
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesCancel)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelContract(SaleCancelFormModel model, CancellationToken ct)
    {
        var c = await LoadContractAsync(model.Id, ct);
        if (c is null) return NotFound();
        if (c.IsCancelled) return Json(new { ok = false, error = $"عقد البيع «{c.Code}» مفسوخ بالفعل." });
        if (model.CancelledAt.Date < c.ContractDate.Date)
            ModelState.AddModelError(nameof(model.CancelledAt), "تاريخ الفسخ لا يسبق تاريخ العقد.");
        if (!ModelState.IsValid) return PartialView("_CancelForm", await FillCancelAsync(model, c, ct));

        var insts = await _db.Installments.Where(i => i.SaleContractId == c.Id).ToListAsync(ct);
        var paid = insts.Sum(i => i.PaidAmount);
        var fee = Math.Round(paid * model.FeePercent / 100m, 2);

        // The schedule is kept exactly as entered (so «إلغاء الفسخ» restores it); while the contract is cancelled its unpaid
        // rows are simply ignored — not due, not collectable, not in the customer's balance.
        c.IsCancelled = true;
        c.CancelledAt = model.CancelledAt;
        c.CancelPaidAmount = paid;
        c.CancelFeePercent = model.FeePercent;
        c.CancelFeeAmount = fee;
        c.CancelReason = string.IsNullOrWhiteSpace(model.Reason) ? null : model.Reason.Trim();
        c.CancelledByName = User.FindFirst("full_name")?.Value ?? User.Identity?.Name;

        if (c.Unit is not null) c.Unit.Status = UnitStatus.Available;   // the unit can be sold again
        await _accounting.SyncSaleContractCancellationAsync(c, ct);   // reverse the sale, keep the fee, owe the customer
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم فسخ عقد البيع «{c.Code}» للعميل «{c.Customer?.FullName}» — الوحدة «{c.Unit?.Name}» أصبحت متاحة. "
            + $"المدفوع {paid:N2} ج.م، رسوم الفسخ {fee:N2} ج.م ({model.FeePercent:0.##}%)، المستحق للعميل {paid - fee:N2} ج.م.";
        return Json(new { ok = true, redirect = Url.Action(nameof(Details), new { id = c.Id }) });
    }

    // ---------------- إلغاء الفسخ (undo a cancellation) ----------------
    // Puts the contract back exactly as it was: the unit is sold again, the reversal entry is removed and the schedule
    // (never touched by the cancellation) is due again. Only while nothing has been paid back to the customer and the
    // unit hasn't been sold to someone else.
    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesUncancel)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UncancelContract(Guid id, CancellationToken ct)
    {
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        string? refusal = null;
        if (!c.IsCancelled) refusal = $"عقد البيع «{c.Code}» غير مفسوخ.";
        else if ((await RefundedByContractAsync(ct)).GetValueOrDefault(c.Id) > 0)
            refusal = $"لا يمكن إلغاء فسخ العقد «{c.Code}» لوجود دفعات رد مستحقات للعميل — احذفها أولًا.";
        else if (await _db.SaleContracts.Where(s => s.UnitId == c.UnitId && s.Id != c.Id && !s.IsCancelled)
                     .Select(s => s.Code).FirstOrDefaultAsync(ct) is string other)
            refusal = $"لا يمكن إلغاء فسخ العقد «{c.Code}» لأن الوحدة «{c.Unit?.Name}» بيعت بعقد آخر «{other}».";
        if (refusal is not null)
        {
            TempData["ErrorMessage"] = refusal;
            return RedirectToAction(nameof(Details), new { id });
        }

        var at = c.CancelledAt;
        c.IsCancelled = false;
        c.CancelledAt = null;
        c.CancelPaidAmount = 0;
        c.CancelFeePercent = 0;
        c.CancelFeeAmount = 0;
        c.CancelReason = null;
        c.CancelledByName = null;
        if (c.Unit is not null) c.Unit.Status = UnitStatus.Sold;
        await _accounting.SyncSaleContractCancellationAsync(c, ct);   // not cancelled → just removes the reversal entry
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم إلغاء فسخ عقد البيع «{c.Code}» (المفسوخ في {RealState.Web.AppCulture.When(at)}) وإعادته كما كان — "
            + $"الوحدة «{c.Unit?.Name}» مباعة للعميل «{c.Customer?.FullName}» وجدول الأقساط مستحق من جديد.";
        return RedirectToAction(nameof(Details), new { id });
    }

    // ---------------- Refunds to the customer of a cancelled contract ----------------

    [HttpGet]
    [Authorize(Policy = PermissionNames.SalesRefund)]
    public async Task<IActionResult> RefundForm(Guid id, CancellationToken ct)
    {
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        if (!c.IsCancelled) return NotFound();
        var model = await FillRefundAsync(new SaleRefundFormModel { ContractId = c.Id }, c, ct);
        if (model.Remaining <= 0)
            return Content("<div class=\"status-message\">تم صرف كامل مستحقات العميل عن هذا العقد.</div>", "text/html");
        model.Amount = model.Remaining;
        return PartialView("_RefundForm", model);
    }

    private async Task<SaleRefundFormModel> FillRefundAsync(SaleRefundFormModel model, SaleContract c, CancellationToken ct)
    {
        model.Code = c.Code;
        model.CustomerName = c.Customer?.FullName ?? "—";
        model.Due = c.CancelRefundDue;
        model.Refunded = (await RefundedByContractAsync(ct)).GetValueOrDefault(c.Id);
        model.Safes = await _db.Safes.Where(s => s.IsActive).OrderBy(s => s.Name)
            .Select(s => new SelectListItem { Value = s.Id.ToString(), Text = s.Name }).ToListAsync(ct);
        return model;
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesRefund)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refund(SaleRefundFormModel model, CancellationToken ct)
    {
        var c = await LoadContractAsync(model.ContractId, ct);
        if (c is null || !c.IsCancelled) return NotFound();
        await FillRefundAsync(model, c, ct);
        if (model.Amount > model.Remaining + 0.005m)
            ModelState.AddModelError(nameof(model.Amount), $"المبلغ يتجاوز المتبقي للعميل ({model.Remaining:N2} ج.م).");
        if (model.PaidAt.Date < (c.CancelledAt ?? c.ContractDate).Date)
            ModelState.AddModelError(nameof(model.PaidAt), "تاريخ الصرف لا يسبق تاريخ الفسخ.");
        if (ModelState.IsValid && await _guard.CheckWithdrawalAsync(model.SafeId!.Value, model.Amount, ct) is string overdraw)
            ModelState.AddModelError(nameof(model.Amount), overdraw);   // «سحب على المكشوف»
        if (!ModelState.IsValid) return PartialView("_RefundForm", model);

        var desc = $"رد مستحقات العميل «{model.CustomerName}» عن فسخ عقد البيع {c.Code}"
            + (string.IsNullOrWhiteSpace(model.Notes) ? "" : $" — {model.Notes!.Trim()}");
        var txn = await _accounting.AddTransactionAsync(model.SafeId!.Value, TxnType.Expense, TxnSource.ContractCancelRefund,
            model.Amount, model.PaidAt, desc, projectId: c.ProjectId, customerId: c.CustomerId, unitId: c.UnitId, ct: ct);   // Dr العملاء / Cr الخزنة
        txn.SaleContractId = c.Id;
        await _db.SaveChangesAsync(ct);

        var left = model.Remaining - model.Amount;
        TempData["StatusMessage"] = $"تم صرف {model.Amount:N2} ج.م للعميل «{model.CustomerName}» من مستحقات العقد المفسوخ «{c.Code}» (سند صرف رقم {txn.Serial})"
            + (left > 0 ? $" — المتبقي للعميل {left:N2} ج.م." : " — تم سداد كامل المستحقات.");
        return Json(new { ok = true, openTab = Url.Action(nameof(RefundReceipt), new { id = txn.Id }) });
    }

    // The refund's printable voucher (سند صرف).
    [HttpGet]
    public async Task<IActionResult> RefundReceipt(Guid id, CancellationToken ct)
    {
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == id && t.Source == TxnSource.ContractCancelRefund, ct);
        if (txn is null) return NotFound();
        var code = await _db.SaleContracts.Where(s => s.Id == txn.SaleContractId).Select(s => s.Code).FirstOrDefaultAsync(ct);
        ViewBag.PartyLabel = "العميل";
        ViewBag.Party = await _db.Customers.IgnoreQueryFilters().Where(x => x.Id == txn.CustomerId).Select(x => x.FullName).FirstOrDefaultAsync(ct);
        ViewBag.About = $"رد مستحقات عن فسخ عقد البيع {code}";
        ViewBag.SafeName = await _db.Safes.Where(s => s.Id == txn.SafeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("~/Areas/Accounting/Views/Shared/PrintOne.cshtml", txn);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesDeleteRefund)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRefund(Guid id, CancellationToken ct)
    {
        var txn = await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == id && t.Source == TxnSource.ContractCancelRefund, ct);
        if (txn is null) return NotFound();
        var contractId = txn.SaleContractId;
        var code = await _db.SaleContracts.Where(s => s.Id == contractId).Select(s => s.Code).FirstOrDefaultAsync(ct);
        await _accounting.RemoveTransactionAsync(txn, ct);   // the money is back in the safe and owed to the customer again
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تم حذف دفعة رد المستحقات (سند صرف رقم {txn.Serial}) بقيمة {txn.Amount:N2} ج.م من العقد المفسوخ «{code}».";
        return RedirectToAction(nameof(Details), new { id = contractId });
    }

    [HttpGet]
    public async Task<IActionResult> Contract(Guid id, bool onePage, CancellationToken ct)
    {
        ViewBag.OnePage = onePage;   // «طباعة في صفحة واحدة»
        var c = await LoadContractAsync(id, ct);
        if (c is null) return NotFound();
        ViewBag.Installments = await _db.Installments.Where(i => i.SaleContractId == id).OrderBy(i => i.DueDate).ThenBy(i => i.Type).ThenBy(i => i.Number).ToListAsync(ct);
        // A cancelled contract prints as the normal contract plus its cancellation details.
        if (c.IsCancelled) ViewBag.Refunds = await RefundsAsync(id, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Contract", c);
    }

    [HttpPost]
    [Authorize(Policy = PermissionNames.SalesDelete)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, DateTime? from, DateTime? to, CancellationToken ct)
    {
        // Preserve the caller's date filter so the list stays on the same view after the action.
        var back = new
        {
            from = from?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            to = to?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };

        var c = await _db.SaleContracts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return NotFound();

        // A cancelled contract stays on file (its unit may already be sold again, its refunds hang off it).
        if (c.IsCancelled)
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف عقد البيع «{c.Code}» لأنه مفسوخ — يبقى العقد المفسوخ في السجل.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var insts = await _db.Installments.Where(i => i.SaleContractId == id).ToListAsync(ct);
        // A contract with any collected installment cannot be deleted — cancel the collections first.
        if (insts.Any(i => i.PaidAmount > 0))
        {
            TempData["ErrorMessage"] = $"لا يمكن حذف عقد البيع «{c.Code}» لوجود أقساط محصَّلة. يجب إلغاء التحصيلات المرتبطة أولًا.";
            return RedirectToAction(nameof(Index), back);
        }

        var unit = await _db.ProjectUnits.FirstOrDefaultAsync(u => u.Id == c.UnitId, ct);
        if (unit is not null) unit.Status = UnitStatus.Available; // release the unit
        _db.Installments.RemoveRange(insts);
        await _accounting.RemoveObligationAsync("SaleContract", c.Id, ct);   // reverse the sale journal entry
        _db.SaleContracts.Remove(c);
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم حذف عقد البيع «{c.Code}».";
        return RedirectToAction(nameof(Index), back);
    }

    // ---------- helpers ----------
    private async Task<SaleContract?> LoadContractAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.SaleContracts.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return null;
        c.Customer = await _db.Customers.FirstOrDefaultAsync(x => x.Id == c.CustomerId, ct);
        c.Project = await _db.Projects.FirstOrDefaultAsync(x => x.Id == c.ProjectId, ct);
        c.Unit = await _db.ProjectUnits.FirstOrDefaultAsync(x => x.Id == c.UnitId, ct);
        return c;
    }

    // Year rule: S-{year}{4-digit seq} (S-20260001 … S-20270001), restarting each contract year. Older
    // S-0001-style codes are left as issued (they're printed on signed contracts) and don't affect the sequence.
    private async Task<string> NextCodeAsync(int year, CancellationToken ct)
    {
        long lo = YearSerial.Base(year, 4), hi = YearSerial.End(year, 4);
        var codes = await _db.SaleContracts.Where(s => s.Code.StartsWith("S-" + year)).Select(s => s.Code).ToListAsync(ct);
        long? max = codes.Select(c => long.TryParse(c.Replace("S-", ""), out var n) ? n : 0L)
            .Where(n => n > lo && n < hi).Select(n => (long?)n).DefaultIfEmpty(null).Max();
        return "S-" + YearSerial.Next(max, year, 4);
    }

    private async Task<List<SaleListItem>> BuildRowsAsync(DateTime? from, DateTime? to, List<SaleContractState>? status, CancellationToken ct)
    {
        var q = _db.SaleContracts.AsQueryable();
        if (from.HasValue) q = q.Where(s => s.ContractDate >= from.Value.Date);
        if (to.HasValue) q = q.Where(s => s.ContractDate < to.Value.Date.AddDays(1));
        // الحالة: سارٍ / مفسوخ — one of them ticked filters; none or both = all.
        if (status is { Count: > 0 } && status.Distinct().Count() == 1)
        {
            var cancelled = status[0] == SaleContractState.Cancelled;
            q = q.Where(s => s.IsCancelled == cancelled);
        }
        var contracts = await q.OrderByDescending(s => s.ContractDate).ThenByDescending(s => s.CreatedAt).ToListAsync(ct);
        var refunded = await RefundedByContractAsync(ct);

        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);
        var paidById = (await _db.Installments.GroupBy(i => i.SaleContractId)
            .Select(g => new { g.Key, Paid = g.Sum(x => x.PaidAmount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Paid);

        return contracts.Select(s => new SaleListItem
        {
            Id = s.Id, Code = s.Code, ContractDate = s.ContractDate, ReceiveDate = s.ReceiveDate, TotalPrice = s.TotalPrice,
            Customer = custNames.GetValueOrDefault(s.CustomerId, "—"),
            Project = projNames.GetValueOrDefault(s.ProjectId, "—"),
            Unit = unitNames.GetValueOrDefault(s.UnitId, "—"),
            Paid = paidById.TryGetValue(s.Id, out var p) ? p : 0,   // collected installments only (down payment is scheduled)
            IsCancelled = s.IsCancelled,
            RefundRemaining = s.IsCancelled ? s.CancelRefundDue - refunded.GetValueOrDefault(s.Id) : 0
        }).ToList();
    }

    // Σ refunds paid back per cancelled contract.
    private async Task<Dictionary<Guid, decimal>> RefundedByContractAsync(CancellationToken ct)
        => (await _db.SafeTransactions.Where(t => t.Source == TxnSource.ContractCancelRefund && t.SaleContractId != null)
                .GroupBy(t => t.SaleContractId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);

    private async Task<SaleFormModel> FillAsync(SaleFormModel model, CancellationToken ct)
    {
        // Materialize first, then ToString() client-side so ids are lowercase and match the units'
        // data-project attribute (Guid.ToString() inside an EF query returns UPPERCASE on SQL Server).
        // Only actual customers (leads must be converted first) can be put on a sale contract.
        var customers = await _db.Customers.Where(c => !c.IsLead).OrderBy(c => c.FullName).Select(c => new { c.Id, c.FullName }).ToListAsync(ct);
        model.Customers = customers.Select(c => new SelectListItem { Value = c.Id.ToString(), Text = c.FullName }).ToList();

        var projects = await _db.Projects.OrderBy(p => p.Name).Select(p => new { p.Id, p.Code, p.Name }).ToListAsync(ct);
        model.Projects = projects.Select(p => new SelectListItem { Value = p.Id.ToString(), Text = $"#{p.Code} {p.Name}" }).ToList();

        // Unsold units carrying their project id so the units list filters when a project is chosen.
        var units = await _db.ProjectUnits.Where(u => u.Status != UnitStatus.Sold)
            .Select(u => new { u.Id, u.ProjectId, u.Name, u.Number }).ToListAsync(ct);
        model.UnitOptions = units
            .Select(u => new SaleUnitOption(u.Id, u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), u.ProjectId))
            .ToList();
        return model;
    }
}
