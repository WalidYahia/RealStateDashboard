using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Accounting.Models;

namespace RealState.Web.Areas.Accounting.Controllers;

[Area("Accounting")]
[Authorize(Policy = PermissionNames.AccountsView)]
public class AccountingReportsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    public AccountingReportsController(IApplicationDbContext db, IAccountingEngine engine) { _db = db; _engine = engine; }

    private static string N(decimal v) => v.ToString("N2", RealState.Web.AppCulture.Ar);

    // ======================= ميزان المراجعة (Trial Balance) =======================
    public async Task<IActionResult> TrialBalance(DateTime? from, DateTime? to, CancellationToken ct)
        => View(await BuildTrialBalanceAsync(from, to, ct));

    [HttpGet]
    public async Task<IActionResult> TrialBalancePrint(DateTime? from, DateTime? to, CancellationToken ct)
        => View("TrialBalancePrint", await BuildTrialBalanceAsync(from, to, ct));

    [HttpGet]
    public async Task<IActionResult> TrialBalanceExcel(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildTrialBalanceAsync(from, to, ct);
        var headers = new[] { "رقم الحساب", "اسم الحساب", "رصيد بداية مدين", "رصيد بداية دائن",
                              "حركة مدين", "حركة دائن", "رصيد نهاية مدين", "رصيد نهاية دائن" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Code, new string(' ', r.Depth * 2) + r.Name,
            r.OpeningDebit, r.OpeningCredit, r.PeriodDebit, r.PeriodCredit, r.ClosingDebit, r.ClosingCredit
        });
        var totals = new object?[] { "الإجمالي", null, vm.TotOpeningDebit, vm.TotOpeningCredit,
                                     vm.TotPeriodDebit, vm.TotPeriodCredit, vm.TotClosingDebit, vm.TotClosingCredit };
        return RealState.Web.Common.Xlsx.File(
            $"ميزان المراجعة {(vm.To ?? DateTime.Today):yyyy-MM-dd}.xlsx", "ميزان المراجعة", headers, rows, totals);
    }

    private async Task<TrialBalanceVm> BuildTrialBalanceAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await _engine.EnsureChartAsync(ct);
        var f = from?.Date;                       // null → from the beginning (opening = 0)
        var t = (to ?? DateTime.Today).Date;

        var accounts = await _db.Accounts
            .Select(a => new AccMeta(a.Id, a.Code, a.Name, a.ParentId, a.IsPostable, a.SortOrder)).ToListAsync(ct);

        // Every line up to the end of the period, split into "opening" (before f) and "period" ([f, t]).
        var lineData = await (from l in _db.JournalLines
                              join e in _db.JournalEntries on l.JournalEntryId equals e.Id
                              where e.Date < t.AddDays(1)
                              select new { l.AccountId, l.Debit, l.Credit, e.Date }).ToListAsync(ct);

        var openNet = new Dictionary<Guid, decimal>();
        var perDr = new Dictionary<Guid, decimal>();
        var perCr = new Dictionary<Guid, decimal>();
        foreach (var x in lineData)
        {
            if (f.HasValue && x.Date < f.Value)
                openNet[x.AccountId] = openNet.GetValueOrDefault(x.AccountId) + (x.Debit - x.Credit);
            else
            {
                perDr[x.AccountId] = perDr.GetValueOrDefault(x.AccountId) + x.Debit;
                perCr[x.AccountId] = perCr.GetValueOrDefault(x.AccountId) + x.Credit;
            }
        }

        var childrenOf = accounts.ToLookup(a => a.ParentId);
        IEnumerable<AccMeta> Children(Guid? id) =>
            childrenOf[id].OrderBy(a => a.SortOrder).ThenBy(a => a.Code, StringComparer.Ordinal);

        var vm = new TrialBalanceVm { From = f, To = t };

        // Pre-order DFS: parent row first, then its children (rolled-up sums bubble back up).
        (decimal on, decimal pd, decimal pc) Walk(AccMeta node, int depth)
        {
            var row = new TrialBalanceRow { Code = node.Code, Name = node.Name, Depth = depth };
            vm.Rows.Add(row);   // reference kept — filled after children are aggregated

            decimal on = openNet.GetValueOrDefault(node.Id);
            decimal pd = perDr.GetValueOrDefault(node.Id);
            decimal pc = perCr.GetValueOrDefault(node.Id);
            var kids = Children(node.Id).ToList();
            foreach (var c in kids)
            {
                var (con, cpd, cpc) = Walk(c, depth + 1);
                on += con; pd += cpd; pc += cpc;
            }
            row.IsGroup = kids.Count > 0;
            row.OpeningDebit = on > 0 ? on : 0m; row.OpeningCredit = on < 0 ? -on : 0m;
            row.PeriodDebit = pd; row.PeriodCredit = pc;
            var cn = on + pd - pc;
            row.ClosingDebit = cn > 0 ? cn : 0m; row.ClosingCredit = cn < 0 ? -cn : 0m;

            // Grand totals over leaf accounts only (their sum already equals the roots' sum).
            if (!row.IsGroup)
            {
                vm.TotOpeningDebit += row.OpeningDebit; vm.TotOpeningCredit += row.OpeningCredit;
                vm.TotPeriodDebit += row.PeriodDebit;   vm.TotPeriodCredit += row.PeriodCredit;
                vm.TotClosingDebit += row.ClosingDebit; vm.TotClosingCredit += row.ClosingCredit;
            }
            return (on, pd, pc);
        }

        foreach (var root in Children(null))
            Walk(root, 0);

        // Drop accounts with no balance and no movement in the whole view.
        vm.Rows = vm.Rows.Where(r =>
            r.OpeningDebit != 0 || r.OpeningCredit != 0 || r.PeriodDebit != 0 ||
            r.PeriodCredit != 0 || r.ClosingDebit != 0 || r.ClosingCredit != 0).ToList();
        return vm;
    }

    // ======================= قائمة الدخل (Income Statement) =======================
    public async Task<IActionResult> IncomeStatement(DateTime? from, DateTime? to, CancellationToken ct)
        => View(await BuildIncomeStatementAsync(from, to, ct));

    [HttpGet]
    public async Task<IActionResult> IncomeStatementPrint(DateTime? from, DateTime? to, CancellationToken ct)
        => View("IncomeStatementPrint", await BuildIncomeStatementAsync(from, to, ct));

    [HttpGet]
    public async Task<IActionResult> IncomeStatementExcel(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildIncomeStatementAsync(from, to, ct);
        var headers = new[] { "البند", "الكود", "الحساب", "المبلغ" };
        var rows = new List<IReadOnlyList<object?>>();
        rows.Add(new object?[] { "الإيرادات", null, null, null });
        foreach (var r in vm.Revenues) rows.Add(new object?[] { null, r.Code, r.Name, r.Amount });
        rows.Add(new object?[] { "إجمالي الإيرادات", null, null, vm.TotalRevenue });
        rows.Add(new object?[] { "المصروفات", null, null, null });
        foreach (var e in vm.Expenses) rows.Add(new object?[] { null, e.Code, e.Name, e.Amount });
        rows.Add(new object?[] { "إجمالي المصروفات", null, null, vm.TotalExpense });
        var totals = new object?[] { vm.NetProfit >= 0 ? "صافي الربح" : "صافي الخسارة", null, null, vm.NetProfit };
        return RealState.Web.Common.Xlsx.File(
            $"قائمة الدخل {vm.From:yyyy-MM-dd}_{vm.To:yyyy-MM-dd}.xlsx", "قائمة الدخل", headers, rows, totals);
    }

    private async Task<IncomeStatementVm> BuildIncomeStatementAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await _engine.EnsureChartAsync(ct);
        // Default period: year-to-date (1 Jan of the current year → today).
        var f = (from ?? new DateTime(DateTime.Today.Year, 1, 1)).Date;
        var t = (to ?? DateTime.Today).Date;

        var sums = await (from l in _db.JournalLines
                          join e in _db.JournalEntries on l.JournalEntryId equals e.Id
                          where e.Date >= f && e.Date < t.AddDays(1)
                          group l by l.AccountId into g
                          select new { AccountId = g.Key, Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) })
                         .ToListAsync(ct);

        var accounts = await _db.Accounts
            .Where(a => a.Type == AccountType.Revenue || a.Type == AccountType.Expense)
            .Select(a => new { a.Id, a.Code, a.Name, a.Type }).ToListAsync(ct);
        var byId = accounts.ToDictionary(a => a.Id);

        var vm = new IncomeStatementVm { From = f, To = t };
        foreach (var s in sums)
        {
            if (!byId.TryGetValue(s.AccountId, out var a)) continue;
            if (a.Type == AccountType.Revenue)
            {
                var amt = s.Credit - s.Debit;             // revenue is credit-natured
                if (Math.Round(amt, 2) != 0m) vm.Revenues.Add(new IncomeStatementLine { Code = a.Code, Name = a.Name, Amount = amt });
            }
            else
            {
                var amt = s.Debit - s.Credit;             // expense is debit-natured
                if (Math.Round(amt, 2) != 0m) vm.Expenses.Add(new IncomeStatementLine { Code = a.Code, Name = a.Name, Amount = amt });
            }
        }
        vm.Revenues = vm.Revenues.OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
        vm.Expenses = vm.Expenses.OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
        return vm;
    }

    private sealed record AccMeta(Guid Id, string Code, string Name, Guid? ParentId, bool IsPostable, int SortOrder);

    internal static string TypeAr(AccountType t) => t switch
    {
        AccountType.Asset => "أصول",
        AccountType.Liability => "خصوم",
        AccountType.Equity => "حقوق ملكية",
        AccountType.Revenue => "إيرادات",
        AccountType.Expense => "مصروفات",
        _ => t.ToString()
    };
}
