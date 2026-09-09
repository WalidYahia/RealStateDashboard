using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

[Area("Accounting")]
[Authorize(Policy = PermissionNames.AccountsView)]
public class GeneralLedgerController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    public GeneralLedgerController(IApplicationDbContext db, IAccountingEngine engine) { _db = db; _engine = engine; }

    // Adding/editing/deleting a manual journal entry (قيد يدوي) needs its own permission —
    // separate from managing the chart of accounts.
    private bool CanPost() => User.HasClaim("permission", PermissionNames.AccountsPostJournal);

    public async Task<IActionResult> Index(Guid? accountId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        await _engine.EnsureChartAsync(ct);
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);

        var vm = new LedgerVm
        {
            AccountId = accountId, From = from, To = to, CanManage = CanPost(),
            Accounts = await _db.Accounts.OrderBy(a => a.Code)
                .Select(a => new SelectListItem { Value = a.Id.ToString(), Text = a.Code + " — " + a.Name, Selected = a.Id == accountId }).ToListAsync(ct)
        };
        var all = await _db.Accounts.Select(a => new { a.Id, a.ParentId, a.Name }).ToListAsync(ct);
        var acctName = all.ToDictionary(a => a.Id, a => a.Name);

        // "الكل" (no account chosen) → every account's lines as a flat general journal.
        Account? acc = null;
        HashSet<Guid>? targetIds = null;   // null = no account filter (all accounts)
        if (accountId is null)
        {
            vm.IsAll = true;
            vm.IsGroup = true;             // show the account column
        }
        else
        {
            acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
            if (acc is null) return View(vm);
            vm.AccountCode = acc.Code; vm.AccountName = acc.Name; vm.IsDebitNormal = acc.IsDebitNormal;

            // A parent account rolls up all its descendants' lines.
            var byParent = all.ToLookup(a => a.ParentId);
            targetIds = new HashSet<Guid>();
            void Collect(Guid id) { if (targetIds.Add(id)) foreach (var c in byParent[id]) Collect(c.Id); }
            Collect(accountId.Value);
            vm.IsGroup = targetIds.Count > 1;
        }

        var lines = from l in _db.JournalLines
                    join e in _db.JournalEntries on l.JournalEntryId equals e.Id
                    select new { l.AccountId, l.Debit, l.Credit, l.Memo, e.Id, e.Date, e.Number, e.Description, e.SourceType };
        if (targetIds != null) lines = lines.Where(x => targetIds.Contains(x.AccountId));

        // A running balance only makes sense for a single account/subtree, not for "الكل".
        if (!vm.IsAll)
        {
            var openingRaw = from.HasValue
                ? await lines.Where(x => x.Date < from.Value.Date).SumAsync(x => (decimal?)(x.Debit - x.Credit), ct) ?? 0m
                : 0m;
            vm.Opening = acc!.IsDebitNormal ? openingRaw : -openingRaw;
        }

        var q = lines;
        if (from.HasValue) q = q.Where(x => x.Date >= from.Value.Date);
        if (to.HasValue) q = q.Where(x => x.Date < to.Value.Date.AddDays(1));
        var rows = await q.OrderBy(x => x.Date).ThenBy(x => x.Number).ToListAsync(ct);

        if (vm.IsAll)
        {
            // "الكل" → one row per journal entry (قيد): date, number, description, and its value (Σ debits).
            foreach (var g in rows.GroupBy(x => x.Id).OrderBy(g => g.First().Date).ThenBy(g => g.First().Number))
            {
                var first = g.First();
                vm.Rows.Add(new LedgerRow
                {
                    EntryId = first.Id, Date = first.Date, Number = first.Number, Description = first.Description,
                    IsManual = first.SourceType == "ManualJournal", Value = g.Sum(x => x.Debit)
                });
            }
        }
        else
        {
            var running = vm.Opening;
            foreach (var x in rows)
            {
                running += acc!.IsDebitNormal ? (x.Debit - x.Credit) : (x.Credit - x.Debit);
                vm.Rows.Add(new LedgerRow
                {
                    EntryId = x.Id, Date = x.Date, Number = x.Number, Description = x.Description, Memo = x.Memo,
                    AccountName = acctName.GetValueOrDefault(x.AccountId, ""), IsManual = x.SourceType == "ManualJournal",
                    Debit = x.Debit, Credit = x.Credit, Balance = running
                });
            }
        }
        return View(vm);
    }

    // ---------- View a journal entry's full details ----------
    [HttpGet]
    public async Task<IActionResult> Entry(Guid id, CancellationToken ct)
    {
        var e = await _db.JournalEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return NotFound();
        var lines = await (from l in _db.JournalLines
                           join a in _db.Accounts on l.AccountId equals a.Id
                           where l.JournalEntryId == id
                           select new EntryLineVm { AccountCode = a.Code, AccountName = a.Name, Debit = l.Debit, Credit = l.Credit, Memo = l.Memo })
                          .ToListAsync(ct);
        return PartialView("_EntryDetails", new EntryVm
        {
            Id = e.Id, Number = e.Number, Date = e.Date, Description = e.Description,
            SourceType = e.SourceType, IsManual = e.SourceType == "ManualJournal", Lines = lines
        });
    }

    // ---------- Manual journal entry (قيد يدوي) ----------
    [HttpGet]
    public async Task<IActionResult> EntryForm(Guid? id, CancellationToken ct)
    {
        if (!CanPost()) return Forbid();
        var model = new ManualEntryModel { Accounts = await PostableAccountsAsync(ct) };
        if (id is null)
        {
            model.Lines.Add(new ManualLineModel());
            model.Lines.Add(new ManualLineModel());
        }
        else
        {
            var e = await _db.JournalEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
            if (e is null) return NotFound();
            if (e.SourceType != "ManualJournal")
                return Content("<div style=\"padding:18px;color:var(--warning);text-align:center;\">هذا القيد مُولَّد آليًا ولا يمكن تعديله.</div>", "text/html");
            model.Id = e.Id; model.Date = e.Date; model.Description = e.Description;
            model.Lines = await _db.JournalLines.Where(l => l.JournalEntryId == id)
                .Select(l => new ManualLineModel { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, Memo = l.Memo }).ToListAsync(ct);
        }
        return PartialView("_ManualEntryForm", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EntryForm(ManualEntryModel model, string? payload, CancellationToken ct)
    {
        if (!CanPost()) return Forbid();
        // The lines are posted as a JSON payload (robust against indexed <select> binding).
        if (!string.IsNullOrWhiteSpace(payload))
        {
            try { model.Lines = System.Text.Json.JsonSerializer.Deserialize<List<ManualLineModel>>(payload, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new(); }
            catch { }
        }
        // The indexed Lines[i].* inputs also post (their empty debit/credit sides fail to bind to
        // the non-nullable decimals and add invisible ModelState errors) — but payload is the source
        // of truth, so drop that binding noise while keeping the البيان (Description) validation.
        foreach (var k in ModelState.Keys.Where(k => k.StartsWith("Lines", StringComparison.Ordinal) || k == "payload").ToList())
            ModelState.Remove(k);

        var lines = (model.Lines ?? new()).Where(l => l.AccountId is not null && (l.Debit != 0 || l.Credit != 0)).ToList();

        if (lines.Count < 2) ModelState.AddModelError(string.Empty, "أدخل سطرين على الأقل.");
        if (lines.Any(l => l.Debit != 0 && l.Credit != 0)) ModelState.AddModelError(string.Empty, "كل سطر يحمل مدينًا أو دائنًا وليس كليهما.");
        if (lines.Any(l => l.Debit < 0 || l.Credit < 0)) ModelState.AddModelError(string.Empty, "لا يُسمح بقيم سالبة.");
        if (Math.Round(lines.Sum(l => l.Debit) - lines.Sum(l => l.Credit), 2) != 0m)
            ModelState.AddModelError(string.Empty, $"القيد غير متوازن: مدين {lines.Sum(l => l.Debit):N2} ≠ دائن {lines.Sum(l => l.Credit):N2}.");

        if (!ModelState.IsValid) { model.Accounts = await PostableAccountsAsync(ct); return PartialView("_ManualEntryForm", model); }

        var tuples = lines.Select(l => (l.AccountId!.Value, l.Debit, l.Credit, l.Memo)).ToList();

        // Edit replaces the old manual entry; create posts a new one.
        if (model.Id != Guid.Empty)
        {
            var old = await _db.JournalEntries.FirstOrDefaultAsync(x => x.Id == model.Id, ct);
            if (old is null) return NotFound();
            if (old.SourceType != "ManualJournal") return Json(new { ok = false, error = "لا يمكن تعديل قيد مُولَّد آليًا." });
            _db.JournalLines.RemoveRange(await _db.JournalLines.Where(l => l.JournalEntryId == model.Id).ToListAsync(ct));
            _db.JournalEntries.Remove(old);
        }
        try { await _engine.PostByIdsAsync(model.Date, model.Description, "ManualJournal", tuples, ct); }
        catch (InvalidOperationException ex) { model.Accounts = await PostableAccountsAsync(ct); ModelState.AddModelError(string.Empty, ex.Message); return PartialView("_ManualEntryForm", model); }

        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = model.Id == Guid.Empty ? "تم تسجيل القيد اليدوي." : "تم تحديث القيد اليدوي.";
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EntryDelete(Guid id, Guid? accountId, DateTime? from, DateTime? to, CancellationToken ct)
    {
        if (!CanPost()) return Forbid();
        var e = await _db.JournalEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is not null && e.SourceType == "ManualJournal")
        {
            _db.JournalLines.RemoveRange(await _db.JournalLines.Where(l => l.JournalEntryId == id).ToListAsync(ct));
            _db.JournalEntries.Remove(e);
            await _db.SaveChangesAsync(ct);
            TempData["StatusMessage"] = "تم حذف القيد اليدوي.";
        }
        else TempData["ErrorMessage"] = "لا يمكن حذف قيد مُولَّد آليًا.";
        return RedirectToAction(nameof(Index), new { accountId, from = from?.ToString("yyyy-MM-dd"), to = to?.ToString("yyyy-MM-dd") });
    }

    private async Task<List<SelectListItem>> PostableAccountsAsync(CancellationToken ct) =>
        await _db.Accounts.Where(a => a.IsPostable && a.IsActive).OrderBy(a => a.Code)
            .Select(a => new SelectListItem { Value = a.Id.ToString(), Text = a.Code + " — " + a.Name }).ToListAsync(ct);
}
