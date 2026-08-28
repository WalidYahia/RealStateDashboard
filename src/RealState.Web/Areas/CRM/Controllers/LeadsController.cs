using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;

namespace RealState.Web.Areas.CRM.Controllers;

/// <summary>
/// Leads = customers flagged as leads (potential customers). They are created/edited with the shared
/// customer form (flagged as a lead), managed through the customer profile (communication log, status,
/// conversion), and leave this list once converted to a customer.
/// </summary>
[Area("CRM")]
[Authorize(Policy = PermissionNames.LeadsAccessPolicy)]
public class LeadsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    public LeadsController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IActionResult> Index(Guid? salespersonId, string? source, DateTime? from, DateTime? to,
        string? campaign, string? platform, string? unitType, string? paymentPlan, CancellationToken ct)
    {
        // Default the creation-date range to today on a fresh open (no query string).
        if (Request.Query.Count == 0) { from = DateTime.Today; to = DateTime.Today; }

        var allRows = await AllLeadRowsAsync(ct);
        var sourceLabels = allRows.Select(r => r.SourceLabel).Where(s => s != "—").Distinct().OrderBy(s => s).ToList();

        var salesRoleIds = await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);
        var salespersons = await _db.Employees
            .Where(e => e.IsActive && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value))
            .OrderBy(e => e.FullName).Select(e => new { e.Id, e.FullName }).ToListAsync(ct);

        // Distinct option lists for the campaign-import filters (built from the actual imported data).
        static List<SelectListItem> Options(IEnumerable<string?> values) => values
            .Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()).Distinct().OrderBy(v => v)
            .Select(v => new SelectListItem { Value = v, Text = v }).ToList();

        return View(new LeadListVm
        {
            Rows = ApplyFilter(allRows, salespersonId, source, from, to, campaign, platform, unitType, paymentPlan).ToList(),
            SalespersonId = salespersonId, Source = source, From = from, To = to,
            Campaign = campaign, Platform = platform, UnitType = unitType, PaymentPlan = paymentPlan,
            Salespersons = salespersons.Select(e => new SelectListItem { Value = e.Id.ToString(), Text = e.FullName }).ToList(),
            Sources = sourceLabels.Select(s => new SelectListItem { Value = s, Text = s }).ToList(),
            Campaigns = Options(allRows.Select(r => r.CampaignName)),
            Platforms = Options(allRows.Select(r => r.Platform)),
            UnitTypes = Options(allRows.Select(r => r.UnitType)),
            PaymentPlans = Options(allRows.Select(r => r.PaymentPlan))
        });
    }

    // Assigns one salesperson to several selected leads in a single action.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionNames.LeadsAssign)]
    public async Task<IActionResult> BulkAssign(Guid salespersonId, List<Guid> leadIds, CancellationToken ct)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        IActionResult Fail(string msg)
        {
            if (ajax) return Json(new { ok = false, error = msg });
            TempData["ErrorMessage"] = msg;
            return RedirectToAction(nameof(Index));
        }

        if (leadIds is null || leadIds.Count == 0) return Fail("اختر عميلاً محتملاً واحداً على الأقل.");

        // The target must be an active salesperson employee.
        var salesRoleIds = await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);
        var sp = await _db.Employees.FirstOrDefaultAsync(
            e => e.Id == salespersonId && e.IsActive && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value), ct);
        if (sp is null) return Fail("اختر مندوبًا صالحًا.");

        // Only leads the current user is allowed to see can be reassigned (respects the salesperson scoping).
        var q = _db.Customers.Where(c => c.IsLead && leadIds.Contains(c.Id));
        if (await RestrictedSalespersonIdAsync(ct) is Guid myEmpId) q = q.Where(c => c.SalesPersonId == myEmpId);
        var leads = await q.ToListAsync(ct);
        if (leads.Count == 0) return Fail("لا توجد عملاء محتملون صالحون للإسناد.");

        foreach (var l in leads) l.SalesPersonId = salespersonId;
        await _db.SaveChangesAsync(ct);

        TempData["StatusMessage"] = $"تم إسناد {leads.Count} عميل محتمل إلى «{sp.FullName}».";
        return ajax ? Json(new { ok = true }) : RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Print(Guid? salespersonId, string? source, DateTime? from, DateTime? to,
        string? campaign, string? platform, string? unitType, string? paymentPlan, CancellationToken ct)
    {
        var rows = ApplyFilter(await AllLeadRowsAsync(ct), salespersonId, source, from, to, campaign, platform, unitType, paymentPlan).ToList();
        ViewBag.TenantId = _currentUser.TenantId;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Salesperson = salespersonId.HasValue
            ? await _db.Employees.Where(e => e.Id == salespersonId).Select(e => e.FullName).FirstOrDefaultAsync(ct)
            : null;
        ViewBag.Source = source;
        return View("PrintLeads", rows);
    }

    private async Task<List<LeadRow>> AllLeadRowsAsync(CancellationToken ct)
    {
        var salesNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var campNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);

        // A salesperson user sees only leads assigned to them; managers (Leads.Control) see all.
        var q = _db.Customers.Where(c => c.IsLead);
        if (await RestrictedSalespersonIdAsync(ct) is Guid myEmpId) q = q.Where(c => c.SalesPersonId == myEmpId);
        var leads = await q.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        var leadIds = leads.Select(c => c.Id).ToList();
        // Communication logs per lead → count + most recent entry.
        var logsByLead = (await _db.CustomerLogs.Where(l => leadIds.Contains(l.CustomerId)).ToListAsync(ct))
            .GroupBy(l => l.CustomerId).ToDictionary(g => g.Key, g => g.ToList());

        // Latest campaign-import row per lead (for the campaign/platform/interest columns).
        var campaignByLead = (await _db.CampaignLeads.Where(cl => leadIds.Contains(cl.CustomerId)).ToListAsync(ct))
            .GroupBy(cl => cl.CustomerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.CreatedTime).First());

        return leads.Select(c =>
        {
            campaignByLead.TryGetValue(c.Id, out var cl);
            logsByLead.TryGetValue(c.Id, out var clogs);
            var latest = clogs?.OrderByDescending(l => l.Date).ThenByDescending(l => l.CreatedAt).FirstOrDefault();
            return new LeadRow
            {
                Id = c.Id, Name = c.FullName, Phone = c.Phone, CreatedOn = c.CreatedAt,
                SalespersonId = c.SalesPersonId, SourceLabel = c.SourceLabel(campNames),
                Salesperson = c.SalesPersonId.HasValue ? salesNames.GetValueOrDefault(c.SalesPersonId.Value, "—") : "—",
                Interest = c.Interest, LogCount = clogs?.Count ?? 0,
                CampaignName = cl?.CampaignName,
                Platform = cl?.Platform,
                UnitType = ExtraByKeyword(cl?.ExtraFieldsJson, "نوع"),
                PaymentPlan = ExtraByKeyword(cl?.ExtraFieldsJson, "سداد"),
                LatestLog = latest?.Description,
                LatestLogAt = latest?.Date
            };
        }).ToList();
    }

    // The current user's own salesperson-employee id when they are a salesperson user (linked to a
    // salesperson employee) — their leads view is limited to leads assigned to them. Non-salesperson
    // users (managers/admins/other staff) get null and may see all leads.
    private async Task<Guid?> RestrictedSalespersonIdAsync(CancellationToken ct)
    {
        var uid = _currentUser.UserId;
        if (uid is null) return null;
        var salesRoleIds = await _db.JobRoles.Where(r => r.IsSalesperson).Select(r => r.Id).ToListAsync(ct);
        return await _db.Employees
            .Where(e => e.UserId == uid && e.JobRoleId != null && salesRoleIds.Contains(e.JobRoleId.Value))
            .Select(e => (Guid?)e.Id).FirstOrDefaultAsync(ct);
    }

    // Pulls a form-question answer out of a CampaignLead's extra-fields JSON by a keyword in the column name
    // (e.g. "نوع" → unit-type question, "سداد" → payment-plan question), tolerant of the exact header wording.
    private static string? ExtraByKeyword(string? json, string keyword)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (d is null) return null;
            foreach (var p in d)
                if (p.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Value))
                    return p.Value;
            return null;
        }
        catch { return null; }
    }

    private static IEnumerable<LeadRow> ApplyFilter(IEnumerable<LeadRow> rows, Guid? salespersonId, string? source, DateTime? from, DateTime? to,
        string? campaign = null, string? platform = null, string? unitType = null, string? paymentPlan = null)
    {
        if (from.HasValue) rows = rows.Where(r => r.CreatedOn >= from.Value.Date);
        if (to.HasValue) rows = rows.Where(r => r.CreatedOn < to.Value.Date.AddDays(1));
        if (salespersonId.HasValue) rows = rows.Where(r => r.SalespersonId == salespersonId);
        if (!string.IsNullOrWhiteSpace(source)) rows = rows.Where(r => r.SourceLabel == source);
        if (!string.IsNullOrWhiteSpace(campaign)) rows = rows.Where(r => r.CampaignName == campaign);
        if (!string.IsNullOrWhiteSpace(platform)) rows = rows.Where(r => r.Platform == platform);
        if (!string.IsNullOrWhiteSpace(unitType)) rows = rows.Where(r => r.UnitType == unitType);
        if (!string.IsNullOrWhiteSpace(paymentPlan)) rows = rows.Where(r => r.PaymentPlan == paymentPlan);
        return rows;
    }

    // Analytics landing page for the CRM section.
    public async Task<IActionResult> Summary(CancellationToken ct)
    {
        var campNames = await _db.Campaigns.ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var salesNames = await _db.Employees.ToDictionaryAsync(e => e.Id, e => e.FullName, ct);
        var leadQ = _db.Customers.Where(c => c.IsLead);
        if (await RestrictedSalespersonIdAsync(ct) is Guid myEmpId) leadQ = leadQ.Where(c => c.SalesPersonId == myEmpId);
        var leads = await leadQ.ToListAsync(ct);
        var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        return View(new CrmSummaryVm
        {
            TotalLeads = leads.Count,
            NewLeadsThisMonth = leads.Count(l => l.CreatedAt >= monthStart),
            TotalCustomers = await _db.Customers.CountAsync(c => !c.IsLead, ct),
            NewCustomersThisMonth = await _db.Customers.CountAsync(c => !c.IsLead && c.CreatedAt >= monthStart, ct),
            Salespersons = await _db.Employees.CountAsync(e => e.Type == EmployeeType.Salesperson, ct),
            BySource = leads.GroupBy(l => l.SourceLabel(campNames))
                .Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(r => r.Count).ToList(),
            BySalesperson = leads
                .GroupBy(l => l.SalesPersonId.HasValue ? salesNames.GetValueOrDefault(l.SalesPersonId.Value, "—") : "—")
                .Select(g => new CountRow(g.Key, g.Count())).OrderByDescending(r => r.Count).ToList()
        });
    }

    // ---------- Campaign-leads Excel import ----------

    // Fixed columns handled explicitly; every other column is treated as a form-specific extra field.
    private static readonly HashSet<string> KnownHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "created_time", "ad_id", "ad_name", "adset_id", "adset_name", "campaign_id", "campaign_name",
        "form_id", "form_name", "is_organic", "platform", "full_name", "phone", "job_title", "lead_status"
    };

    // Opens the "رفع من Excel" modal (file picker + live preview table + save).
    [HttpGet]
    [Authorize(Policy = PermissionNames.LeadsImport)]
    public IActionResult ImportForm() => PartialView("_ImportForm");

    // Parses the uploaded file and returns its rows as JSON so the modal can preview them before saving.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionNames.LeadsImport)]
    public IActionResult Preview(IFormFile? file)
    {
        var (ok, error, headers, rows) = ReadUpload(file);
        if (!ok) return Json(new { ok = false, error });
        // Order every row's cells by the header list so the preview columns line up.
        var data = rows.Select(r => headers.Select(h => r.TryGetValue(h, out var v) ? v : "").ToList()).ToList();
        return Json(new { ok = true, headers, rows = data, count = rows.Count });
    }

    /// <summary>
    /// Imports a marketing-platform leads export (e.g. Facebook Lead Ads .xlsx). Each row is matched to an
    /// existing lead/customer by phone (a new lead is created when none matches), then a CampaignLead detail
    /// row is stored. Rows whose «id» was already imported are skipped, so re-uploading the same file is safe.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = PermissionNames.LeadsImport)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken ct)
    {
        var ajax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
        IActionResult Fail(string msg)
        {
            if (ajax) return Json(new { ok = false, error = msg });
            TempData["ErrorMessage"] = msg;
            return RedirectToAction(nameof(Index));
        }

        var (ok, error, headers, rows) = ReadUpload(file);
        if (!ok) return Fail(error!);

        var extraHeaders = headers.Where(h => !KnownHeaders.Contains(h)).ToList();

        // Dedupe keys already in the database (this tenant).
        var seenExternalIds = new HashSet<string>(
            await _db.CampaignLeads.Select(c => c.ExternalId).ToListAsync(ct), StringComparer.OrdinalIgnoreCase);

        // Existing leads/customers keyed by normalized phone (first wins).
        var existingByPhone = new Dictionary<string, Guid>();
        foreach (var c in await _db.Customers.Select(c => new { c.Id, c.Phone }).ToListAsync(ct))
        {
            var key = NormalizePhone(c.Phone);
            if (key.Length > 0) existingByPhone.TryAdd(key, c.Id);
        }
        var newByPhone = new Dictionary<string, Customer>();   // leads created during this import (intra-file reuse)

        int imported = 0, skipped = 0, newLeads = 0, invalid = 0;

        foreach (var row in rows)
        {
            var extId = Get(row, "id");
            if (extId.Length == 0) { invalid++; continue; }
            if (!seenExternalIds.Add(extId)) { skipped++; continue; }   // already imported, or duplicate row in the file

            var phoneKey = NormalizePhone(Get(row, "phone"));
            if (phoneKey.Length == 0) { invalid++; continue; }          // no phone → cannot attach to a lead

            var createdTime = ParseDate(Get(row, "created_time"));

            // Find or create the lead/customer for this phone.
            Guid customerId;
            if (existingByPhone.TryGetValue(phoneKey, out var existingId))
            {
                customerId = existingId;
            }
            else if (newByPhone.TryGetValue(phoneKey, out var pending))
            {
                customerId = pending.Id;
            }
            else
            {
                var name = Get(row, "full_name");
                var lead = new Customer
                {
                    FullName = name.Length > 0 ? name : "—",
                    Phone = Get(row, "phone"),
                    IsLead = true,
                    Source = PlatformToSource(Get(row, "platform")),
                    CreatedAt = createdTime ?? default   // honored by SaveChanges (explicit CreatedAt is preserved)
                };
                _db.Customers.Add(lead);
                newByPhone[phoneKey] = lead;
                customerId = lead.Id;
                newLeads++;
            }

            var extras = new Dictionary<string, string>();
            foreach (var h in extraHeaders)
            {
                var v = Get(row, h);
                if (v.Length > 0) extras[h] = v;
            }

            _db.CampaignLeads.Add(new CampaignLead
            {
                ExternalId = extId,
                CustomerId = customerId,
                CreatedTime = createdTime ?? DateTime.Now,
                AdId = Nz(Get(row, "ad_id")), AdName = Nz(Get(row, "ad_name")),
                AdsetId = Nz(Get(row, "adset_id")), AdsetName = Nz(Get(row, "adset_name")),
                CampaignExternalId = Nz(Get(row, "campaign_id")), CampaignName = Nz(Get(row, "campaign_name")),
                FormId = Nz(Get(row, "form_id")), FormName = Nz(Get(row, "form_name")),
                IsOrganic = ParseBool(Get(row, "is_organic")),
                Platform = Nz(Get(row, "platform")),
                JobTitle = Nz(Get(row, "job_title")), LeadStatus = Nz(Get(row, "lead_status")),
                ExtraFieldsJson = extras.Count > 0 ? JsonSerializer.Serialize(extras) : null
            });
            imported++;
        }

        await _db.SaveChangesAsync(ct);

        var parts = new List<string> { $"تمت إضافة {imported} سجل حملة", $"عملاء محتملون جدد: {newLeads}" };
        if (skipped > 0) parts.Add($"مكرّرة تم تجاهلها: {skipped}");
        if (invalid > 0) parts.Add($"صفوف غير صالحة: {invalid}");
        TempData["StatusMessage"] = string.Join(" — ", parts) + ".";
        return ajax ? Json(new { ok = true }) : RedirectToAction(nameof(Index));
    }

    // Reads + validates an uploaded file (Excel .xlsx or a .csv/.txt export): returns the header list and
    // per-row header→value maps, or an error.
    private (bool ok, string? error, List<string> headers, List<Dictionary<string, string>> rows) ReadUpload(IFormFile? file)
    {
        if (file is null || file.Length == 0) return (false, "اختر ملفًا أولًا.", new(), new());
        var ext = System.IO.Path.GetExtension(file.FileName).ToLowerInvariant();
        try
        {
            List<string> headers;
            List<Dictionary<string, string>> rows;
            if (ext is ".csv" or ".txt" or ".tsv")
            {
                using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                (headers, rows) = ReadDelimited(reader.ReadToEnd(), ext == ".tsv" ? '\t' : ',');
            }
            else
            {
                using var stream = file.OpenReadStream();
                using var wb = new XLWorkbook(stream);
                (headers, rows) = ReadSheet(wb.Worksheets.First());
            }
            if (!headers.Contains("id", StringComparer.OrdinalIgnoreCase) ||
                !headers.Contains("phone", StringComparer.OrdinalIgnoreCase))
                return (false, "الملف لا يحتوي على العمودين المطلوبين: id و phone.", headers, rows);
            return (true, null, headers, rows);
        }
        catch
        {
            return (false, "تعذّر قراءة الملف. تأكد أنه Excel (.xlsx) أو CSV.", new(), new());
        }
    }

    // Parses delimited text (CSV/TSV) — quoted fields, doubled "" quotes, and delimiters/newlines inside quotes.
    private static (List<string> Headers, List<Dictionary<string, string>> Rows) ReadDelimited(string text, char delimiter)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == delimiter) { record.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { /* handled with the following \n */ }
            else if (ch == '\n') { record.Add(field.ToString()); field.Clear(); records.Add(record); record = new(); }
            else field.Append(ch);
        }
        if (field.Length > 0 || record.Count > 0) { record.Add(field.ToString()); records.Add(record); }

        var headers = new List<string>();
        var rows = new List<Dictionary<string, string>>();
        if (records.Count == 0) return (headers, rows);
        headers = records[0].Select(h => h.Trim()).ToList();
        foreach (var rec in records.Skip(1))
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int c = 0; c < headers.Count; c++)
            {
                var h = headers[c];
                if (h.Length == 0 || map.ContainsKey(h)) continue;
                map[h] = c < rec.Count ? rec[c].Trim() : "";
            }
            if (map.Values.Any(v => v.Length > 0)) rows.Add(map);
        }
        return (headers, rows);
    }

    // Reads a worksheet into a header list + a per-row header→value map (dates normalized to a parseable string).
    private static (List<string> Headers, List<Dictionary<string, string>> Rows) ReadSheet(IXLWorksheet ws)
    {
        var range = ws.RangeUsed();
        var headers = new List<string>();
        var rows = new List<Dictionary<string, string>>();
        if (range is null) return (headers, rows);

        var firstRow = range.FirstRow();
        foreach (var cell in firstRow.Cells()) headers.Add(CellStr(cell));

        foreach (var r in range.RowsUsed().Skip(1))
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < headers.Count; i++)
            {
                var h = headers[i];
                if (h.Length == 0 || map.ContainsKey(h)) continue;
                map[h] = CellStr(r.Cell(i + 1));
            }
            if (map.Values.Any(v => v.Length > 0)) rows.Add(map);   // skip fully blank rows
        }
        return (headers, rows);
    }

    private static string CellStr(IXLCell c) =>
        c.DataType == XLDataType.DateTime && c.TryGetValue<DateTime>(out var dt)
            ? dt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
            : c.GetString().Trim();

    private static string Get(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var v) ? v.Trim() : "";

    private static string? Nz(string s) => s.Length == 0 ? null : s;

    // Keep only digits so "+20 106…", "0106…" and "20106…" compare on the same key.
    private static string NormalizePhone(string? phone) =>
        string.IsNullOrEmpty(phone) ? "" : new string(phone.Where(char.IsDigit).ToArray());

    private static bool ParseBool(string s) =>
        s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" ||
        s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("نعم", StringComparison.Ordinal);

    private static DateTime? ParseDate(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string[] formats =
        {
            "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd",
            "M/d/yyyy H:mm:ss", "M/d/yyyy h:mm:ss tt", "M/d/yyyy"
        };
        if (DateTime.TryParseExact(s, formats, inv, System.Globalization.DateTimeStyles.None, out var d)) return d;
        if (DateTime.TryParse(s, inv, System.Globalization.DateTimeStyles.None, out d)) return d;
        return null;
    }

    private static CustomerSource PlatformToSource(string platform) => platform.ToLowerInvariant() switch
    {
        "fb" or "facebook" => CustomerSource.Facebook,
        "ig" or "instagram" => CustomerSource.Instagram,
        "google" or "google_search" => CustomerSource.Google,
        _ => CustomerSource.Facebook
    };
}
