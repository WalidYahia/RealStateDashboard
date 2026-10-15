using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;
using RealState.Web.Filters;

namespace RealState.Web.Areas.Accounting.Controllers;

/// <summary>
/// الأرصدة الافتتاحية — the opening balance of every postable account: customers, suppliers, safes and the rest of the chart.
/// Each balance is one journal entry against «رصيد افتتاحي» (3200) dated the opening date (<see cref="IOpeningBalanceService"/>);
/// a safe's is its InitialAmount (balance + overdraft guard). Viewing needs the accounts view permission, editing
/// <see cref="PermissionNames.AccountsOpeningBalances"/>.
/// </summary>
[Area("Accounting")]
[Authorize(Policy = PermissionNames.AccountsView)]
public class OpeningBalancesController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    private readonly IOpeningBalanceService _openings;
    private readonly ISafeBalanceGuard _guard;

    public OpeningBalancesController(IApplicationDbContext db, IAccountingEngine engine, IOpeningBalanceService openings, ISafeBalanceGuard guard)
    {
        _db = db; _engine = engine; _openings = openings; _guard = guard;
    }

    private bool CanEdit => User.HasClaim("permission", PermissionNames.AccountsOpeningBalances);

    public async Task<IActionResult> Index(OpeningKind kind = OpeningKind.All, CancellationToken ct = default)
    {
        if (kind == OpeningKind.Others) kind = OpeningKind.All;   // no «حسابات أخرى» tab — they're listed under «الكل»
        await _engine.EnsureChartAsync(ct);
        var rows = await BuildRowsAsync(ct);
        return View(new OpeningBalancesVm
        {
            Kind = kind,
            Rows = rows.Where(r => kind == OpeningKind.All || r.Kind == kind).ToList(),
            Counts = Enum.GetValues<OpeningKind>().ToDictionary(k => k, k => k == OpeningKind.All ? rows.Count : rows.Count(r => r.Kind == k)),
            OpeningDate = await _openings.OpeningDateAsync(ct),
            CanEdit = CanEdit,
        });
    }

    /// <summary>Saves one account's opening balance (inline, AJAX): amount ≥ 0 and its side; 0 clears it.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(string target, decimal? amount, string? side, CancellationToken ct)
    {
        IActionResult Refuse(string error)
        {
            HttpContext.Items[ActivityLogFilter.SkipItem] = true;
            return Json(new { ok = false, error });
        }
        if (!CanEdit) return Refuse("لا تملك صلاحية تعديل الأرصدة الافتتاحية.");
        // A value that didn't parse must never read as 0 (which would clear the balance).
        if (amount is null || !ModelState.IsValid) return Refuse("أدخل رصيدًا صحيحًا (0 لإلغاء الرصيد).");
        var value = Math.Round(amount.Value, 2);
        if (value < 0) return Refuse("أدخل الرصيد موجبًا واختر «مدين» أو «دائن».");
        var isDebit = side != "C";

        var account = await ResolveTargetAsync(target, ct);
        if (account is null) return Refuse("الحساب غير موجود.");
        if (!account.IsPostable || account.Code == LedgerAccounts.OpeningBalanceEquity) return Refuse("لا يمكن تحديد رصيد افتتاحي لهذا الحساب.");
        if (await ReadOnlySourceAsync(account, ct) is string source) return Refuse($"رصيد هذا الحساب الافتتاحي يُسجَّل من {source}.");

        // Lowering a safe's opening balance takes money out of it — «سحب على المكشوف» applies.
        if (account.SubKind == "Safe" && account.SubRefId is Guid safeId
            && await _db.Safes.Where(s => s.Id == safeId).Select(s => (decimal?)s.InitialAmount).FirstOrDefaultAsync(ct) is decimal before)
        {
            var after = isDebit ? value : -value;
            if (after < before && await _guard.CheckChangesAsync(new[] { (safeId, after - before) }, ct) is string overdraw)
                return Refuse(overdraw);
        }

        try { await _openings.SetAsync(account, value, isDebit, ct); }
        catch (InvalidOperationException ex) { return Refuse(ex.Message); }
        await _db.SaveChangesAsync(ct);

        var row = await _db.AccountOpeningBalances.AsNoTracking().FirstOrDefaultAsync(b => b.AccountId == account.Id, ct);
        HttpContext.Items[ActivityLogFilter.DescriptionItem] = value == 0
            ? $"إلغاء الرصيد الافتتاحي للحساب {account.Code} «{account.Name}»."
            : $"تحديث الرصيد الافتتاحي للحساب {account.Code} «{account.Name}»: {value:N2} ج.م {(isDebit ? "مدين" : "دائن")}.";
        return Json(new { ok = true, code = account.Code, updated = row is null ? "—" : AppCulture.When(row.UpdatedAt ?? row.CreatedAt) });
    }

    /// <summary>Moves every opening balance (and its journal entry) to another opening date.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Redate(DateTime date, OpeningKind kind = OpeningKind.All, CancellationToken ct = default)
    {
        if (!CanEdit) return Forbid();
        if (date.Date > ClientTime.Today)
        {
            HttpContext.Items[ActivityLogFilter.SkipItem] = true;
            TempData["ErrorMessage"] = "تاريخ الأرصدة الافتتاحية لا يكون في المستقبل.";
            return RedirectToAction(nameof(Index), new { kind });
        }
        var n = await _openings.RedateAsync(date.Date, ct);
        await _db.SaveChangesAsync(ct);
        TempData["StatusMessage"] = $"تاريخ الأرصدة الافتتاحية أصبح {AppCulture.Day(date)} — نُقلت قيود {n} حساب إليه.";
        return RedirectToAction(nameof(Index), new { kind });
    }

    /// <summary>
    /// Stock (real-estate units, goods inventory and its warehouses) and fixed-asset (cost / accumulated depreciation and
    /// their category accounts) accounts get their opening value from their own pages — opening stock adjustments, unit
    /// costs, assets recorded as opening balances — so here they're shown, not edited. Returns that page's name, else null.
    /// </summary>
    private async Task<string?> ReadOnlySourceAsync(Account account, CancellationToken ct)
    {
        var codes = await LockedCodesAsync(ct);
        var all = await _db.Accounts.AsNoTracking().Select(a => new { a.Id, a.Code, a.ParentId, a.SubKind }).ToDictionaryAsync(a => a.Id, ct);
        for (var a = all.GetValueOrDefault(account.Id); a is not null; a = a.ParentId is Guid p ? all.GetValueOrDefault(p) : null)
            if (Source(a.Code, a.SubKind, codes) is string s) return s;
        return null;
    }

    private static string? Source(string code, string? subKind, (HashSet<string> Stock, HashSet<string> Assets) codes)
        => codes.Stock.Contains(code) || subKind == RealState.Application.Inventory.InventorySources.WarehouseSubKind ? "الرصيد الافتتاحي للأصناف"
         : codes.Assets.Contains(code) || subKind is "AssetCategory" or "AssetCategoryDepreciation" ? "الأصول الثابتة (أصل برصيد افتتاحي)"
         : null;

    private async Task<(HashSet<string> Stock, HashSet<string> Assets)> LockedCodesAsync(CancellationToken ct)
    {
        var stock = new HashSet<string> { LedgerAccounts.RealEstateInventory, LedgerAccounts.GoodsInventory };
        foreach (var c in await _db.InventoryPostingProfiles.Select(p => p.InventoryCode).ToListAsync(ct))
            if (!string.IsNullOrWhiteSpace(c)) stock.Add(c);
        return (stock, new HashSet<string> { LedgerAccounts.FixedAssetsCost, LedgerAccounts.AccumulatedDepreciation });
    }

    /// <summary>The account a row saves to — a customer / supplier / safe subsidiary is created when missing.</summary>
    private async Task<Account?> ResolveTargetAsync(string? target, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(target) || target.Length < 3 || !Guid.TryParse(target[2..], out var id)) return null;
        switch (target[0])
        {
            case 'A': return await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id, ct);
            case 'C':
                var c = await _db.Customers.Where(x => x.Id == id && !x.IsLead).Select(x => x.FullName).FirstOrDefaultAsync(ct);
                return c is null ? null : await _engine.ResolveAccountAsync(LedgerAccounts.AccountsReceivable, "Customer", id, c, ct);
            case 'S':
                var s = await _db.Suppliers.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct);
                return s is null ? null : await _engine.ResolveAccountAsync(LedgerAccounts.AccountsPayable, "Supplier", id, s, ct);
            case 'F':
                var f = await _db.Safes.Where(x => x.Id == id).Select(x => x.Name).FirstOrDefaultAsync(ct);
                return f is null ? null : await _engine.ResolveAccountAsync(LedgerAccounts.CashAndBanks, "Safe", id, f, ct);
            default: return null;
        }
    }

    /// <summary>
    /// Every account that can carry an opening balance: all customers (not leads), suppliers and safes — with or without
    /// their subsidiary account yet — and the other postable accounts of the chart (not «رصيد افتتاحي» itself).
    /// </summary>
    private async Task<List<OpeningBalanceRow>> BuildRowsAsync(CancellationToken ct)
    {
        var accounts = await _db.Accounts.AsNoTracking().ToListAsync(ct);
        var byId = accounts.ToDictionary(a => a.Id);
        var openings = await _db.AccountOpeningBalances.AsNoTracking().ToDictionaryAsync(b => b.AccountId, ct);
        string? ParentOf(Account a) => a.ParentId is Guid p && byId.TryGetValue(p, out var pa) ? $"{pa.Code} — {pa.Name}" : null;
        string ControlName(string code) => accounts.FirstOrDefault(a => a.Code == code) is { } c ? $"{c.Code} — {c.Name}" : code;

        OpeningBalanceRow Row(OpeningKind kind, string target, Account? acc, string name, string parent, bool naturalDebit)
        {
            var o = acc is null ? null : openings.GetValueOrDefault(acc.Id);
            return new OpeningBalanceRow
            {
                Target = target, Kind = kind, Code = acc?.Code, Name = name, Parent = parent,
                Amount = o?.Amount ?? 0m, IsDebit = o?.IsDebit ?? naturalDebit, UpdatedAt = o is null ? null : o.UpdatedAt ?? o.CreatedAt,
            };
        }
        Account? Sub(string kind, Guid id) => accounts.FirstOrDefault(a => a.SubKind == kind && a.SubRefId == id);

        var rows = new List<OpeningBalanceRow>();
        foreach (var c in await _db.Customers.AsNoTracking().Where(x => !x.IsLead).OrderBy(x => x.FullName).Select(x => new { x.Id, x.FullName }).ToListAsync(ct))
            rows.Add(Row(OpeningKind.Customers, $"C:{c.Id}", Sub("Customer", c.Id), c.FullName, ControlName(LedgerAccounts.AccountsReceivable), true));
        foreach (var s in await _db.Suppliers.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(ct))
            rows.Add(Row(OpeningKind.Suppliers, $"S:{s.Id}", Sub("Supplier", s.Id), s.Name, ControlName(LedgerAccounts.AccountsPayable), false));
        foreach (var s in await _db.Safes.AsNoTracking().OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.InitialAmount }).ToListAsync(ct))
        {
            var r = Row(OpeningKind.Safes, $"F:{s.Id}", Sub("Safe", s.Id), s.Name, ControlName(LedgerAccounts.CashAndBanks), true);
            r.Amount = Math.Abs(s.InitialAmount); r.IsDebit = s.InitialAmount >= 0;   // the safe's own opening balance is the source
            rows.Add(r);
        }
        // Stock / fixed-asset accounts: their opening value is what their opening entries (the ones balanced against
        // «رصيد افتتاحي») put on them — shown read-only, with the time of the latest such entry.
        var codes = await LockedCodesAsync(ct);
        string? LockedSource(Account a)
        {
            for (var x = a; x is not null; x = x.ParentId is Guid p ? byId.GetValueOrDefault(p) : null)
                if (Source(x.Code, x.SubKind, codes) is string s) return s;
            return null;
        }
        var equityId = accounts.FirstOrDefault(a => a.Code == LedgerAccounts.OpeningBalanceEquity)?.Id;
        var lockedIds = accounts.Where(a => a.IsPostable && LockedSource(a) is not null).Select(a => a.Id).ToList();
        var lockedValues = equityId is Guid eq && lockedIds.Count > 0
            ? (await (from l in _db.JournalLines
                      join e in _db.JournalEntries on l.JournalEntryId equals e.Id
                      where lockedIds.Contains(l.AccountId) && _db.JournalLines.Any(x => x.JournalEntryId == e.Id && x.AccountId == eq)
                      select new { l.AccountId, Net = l.Debit - l.Credit, At = e.UpdatedAt ?? e.CreatedAt }).ToListAsync(ct))
              .GroupBy(x => x.AccountId).ToDictionary(g => g.Key, g => (Net: g.Sum(x => x.Net), At: g.Max(x => x.At)))
            : new();

        foreach (var a in accounts.Where(a => a.IsPostable && a.SubKind is not ("Customer" or "Supplier" or "Safe") && a.Code != LedgerAccounts.OpeningBalanceEquity
                                              && (a.IsActive || openings.ContainsKey(a.Id)))
                                  .OrderBy(a => a.Code, StringComparer.Ordinal))
        {
            var r = Row(OpeningKind.Others, $"A:{a.Id}", a, a.Name, ParentOf(a) ?? "—", a.IsDebitNormal);
            if (LockedSource(a) is string source)
            {
                var v = lockedValues.GetValueOrDefault(a.Id);
                r.ReadOnlySource = source;
                r.Amount = Math.Abs(v.Net); r.IsDebit = v.Net >= 0; r.UpdatedAt = v.Net == 0 ? null : v.At;
            }
            rows.Add(r);
        }
        return rows;
    }
}
