using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Controllers;
using RealState.Web.Areas.Reports.Models;
using RealState.Web.Areas.Suppliers.Controllers;

namespace RealState.Web.Areas.Reports.Controllers;

[Area("Reports")]
[Authorize(Policy = PermissionNames.ReportsView)]
public class ReportsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public ReportsController(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    // ---------------- Daily report ----------------
    public async Task<IActionResult> Daily(DateTime? date, CancellationToken ct)
        => View(await BuildDailyAsync(date?.Date ?? ClientTime.Today, ct));

    [HttpGet]
    public async Task<IActionResult> DailyPrint(DateTime? date, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("DailyPrint", await BuildDailyAsync(date?.Date ?? ClientTime.Today, ct));
    }

    private async Task<DailyReportVm> BuildDailyAsync(DateTime day, CancellationToken ct)
    {
        var next = day.AddDays(1);
        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var vm = new DailyReportVm { Date = day };
        var (dayStart, dayEnd) = ClientTime.DayRange(day, day);   // [start, end) of the picked day

        var contracts = await _db.SaleContracts.Where(c => c.ContractDate >= day && c.ContractDate < next).ToListAsync(ct);
        vm.Contracts = contracts.OrderBy(c => c.Code).Select(c => new DailyContractRow(
            c.Code, custNames.GetValueOrDefault(c.CustomerId, "—"), unitNames.GetValueOrDefault(c.UnitId, "—"), c.TotalPrice)).ToList();

        // Purchases of the day: purchase invoices, plus legacy orders that still carry a supplier obligation.
        var invoices = await _db.PurchaseInvoices.Where(i => i.InvoiceDate >= dayStart!.Value && i.InvoiceDate < dayEnd!.Value).ToListAsync(ct);
        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var invoiceSums = (await _db.PurchaseInvoiceItems.Where(i => invoiceIds.Contains(i.PurchaseInvoiceId))
            .GroupBy(i => i.PurchaseInvoiceId).Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var orders = await _db.SupplierOrders.Where(o => o.SupplierId != null && o.OrderDate >= day && o.OrderDate < next).ToListAsync(ct);
        var orderIds = orders.Select(o => o.Id).ToList();
        var itemSums = (await _db.SupplierOrderItems.Where(i => orderIds.Contains(i.SupplierOrderId))
            .GroupBy(i => i.SupplierOrderId).Select(g => new { g.Key, Sum = g.Sum(x => x.Cost * x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        string Proj(Guid? id) => id.HasValue ? projNames.GetValueOrDefault(id.Value, "—") : "—";
        vm.Orders = invoices.OrderBy(i => i.Number).Select(i => new DailyOrderRow(
                $"PI-{i.Number}", supNames.GetValueOrDefault(i.SupplierId, "—"), Proj(i.ProjectId), invoiceSums.GetValueOrDefault(i.Id, 0)))
            .Concat(orders.OrderBy(o => o.Number).Select(o => new DailyOrderRow(
                $"PO-{o.Number:D4}", supNames.GetValueOrDefault(o.SupplierId!.Value, "—"), Proj(o.ProjectId), itemSums.GetValueOrDefault(o.Id, 0))))
            .ToList();

        var dayTxns = await _db.SafeTransactions.Where(t => t.OccurredAt >= dayStart!.Value && t.OccurredAt < dayEnd!.Value).ToListAsync(ct);
        // Transfers between safes move money but are neither income nor expense (safe balances below still include them).
        vm.Incomes = dayTxns.Where(t => t.Type == TxnType.Income && t.Source != TxnSource.SafeTransfer).OrderBy(t => t.Serial)
            .Select(t => new DailyTxnRow(t.Serial, t.Description, t.Amount)).ToList();
        vm.Expenses = dayTxns.Where(t => t.Type == TxnType.Expense && t.Source != TxnSource.SafeTransfer).OrderBy(t => t.Serial)
            .Select(t => new DailyTxnRow(t.Serial, t.Description, t.Amount)).ToList();

        // Safe balances as of the end of the selected day.
        var safes = await _db.Safes.OrderBy(s => s.Name).ToListAsync(ct);
        var upToTxns = await _db.SafeTransactions.Where(t => t.OccurredAt < dayEnd!.Value)
            .Select(t => new { t.SafeId, t.Type, t.Amount }).ToListAsync(ct);
        vm.Safes = safes.Select(s =>
        {
            var inc = upToTxns.Where(t => t.SafeId == s.Id && t.Type == TxnType.Income).Sum(t => t.Amount);
            var exp = upToTxns.Where(t => t.SafeId == s.Id && t.Type == TxnType.Expense).Sum(t => t.Amount);
            return new SafeBalanceRow(s.Name, s.InitialAmount + inc - exp);
        }).ToList();

        return vm;
    }

    // ---------------- Customer report ----------------
    // Every customer's current account — the same figures as its statement (all time, no date range).
    public async Task<IActionResult> Customers(CancellationToken ct) => View(await BuildCustomersAsync(ct));

    [HttpGet]
    public async Task<IActionResult> CustomersPrint(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("CustomersPrint", await BuildCustomersAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> CustomersCsv(CancellationToken ct)
    {
        var vm = await BuildCustomersAsync(ct);
        var headers = new[] { "العميل", "الهاتف", "مدين", "دائن", "الرصيد", "" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name + (r.IsLinked ? " (عميل ومورد)" : ""), r.Phone, r.Debit, r.Credit, Math.Abs(r.Balance), Side(r.Balance, "مدين", "دائن")
        });
        var totals = new object?[] { "الإجمالي", null, vm.TotDebit, vm.TotCredit, Math.Abs(vm.TotBalance), Side(vm.TotBalance, "مدين", "دائن") };
        return RealState.Web.Common.Xlsx.File($"تقرير العملاء {ClientTime.Now:yyyy-MM-dd}.xlsx", "تقرير العملاء", headers, rows, totals);
    }

    private async Task<CustomerReportVm> BuildCustomersAsync(CancellationToken ct)
    {
        var customers = await _db.Customers.Where(c => !c.IsLead).OrderBy(c => c.FullName).ToListAsync(ct);
        var accounts = await CustomerLedger.AllRowsAsync(_db, ct);
        // «عميل ومورد»: the merged statement — the linked supplier account's movements count too, from the party's side
        // (what lowers the amount owed to the supplier raises its debit here, and the other way round).
        var supplierAccounts = customers.Any(c => c.SupplierId.HasValue) ? await SupplierLedger.AllAsync(_db, ct) : new();

        return new CustomerReportVm
        {
            Rows = customers.Select(c =>
            {
                var rows = accounts.GetValueOrDefault(c.Id) ?? new();
                var supplierRows = c.SupplierId is Guid sid ? supplierAccounts.GetValueOrDefault(sid)?.Rows ?? new() : new();
                return new CustomerReportRow
                {
                    Id = c.Id, Name = c.FullName, Phone = c.Phone, IsLinked = c.SupplierId.HasValue,
                    Debit = rows.Where(r => !r.Decreases).Sum(r => r.Amount) + supplierRows.Where(r => r.Decreases).Sum(r => r.Amount),
                    Credit = rows.Where(r => r.Decreases).Sum(r => r.Amount) + supplierRows.Where(r => !r.Decreases).Sum(r => r.Amount)
                };
            }).ToList()
        };
    }

    // ---------------- Supplier report ----------------
    // Every supplier's current account — the same figures as its statement (all time, no date range).
    public async Task<IActionResult> Suppliers(CancellationToken ct) => View(await BuildSuppliersAsync(ct));

    [HttpGet]
    public async Task<IActionResult> SuppliersPrint(CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("SuppliersPrint", await BuildSuppliersAsync(ct));
    }

    [HttpGet]
    public async Task<IActionResult> SuppliersCsv(CancellationToken ct)
    {
        var vm = await BuildSuppliersAsync(ct);
        var headers = new[] { "المورد", "الهاتف", "دائن", "مدين", "الرصيد", "" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name + (r.IsLinked ? " (عميل ومورد)" : ""), r.Phone, r.Credit, r.Debit, Math.Abs(r.Balance), Side(r.Balance, "دائن", "مدين")
        });
        var totals = new object?[] { "الإجمالي", null, vm.TotCredit, vm.TotDebit, Math.Abs(vm.TotBalance), Side(vm.TotBalance, "دائن", "مدين") };
        return RealState.Web.Common.Xlsx.File($"تقرير الموردين {ClientTime.Now:yyyy-MM-dd}.xlsx", "تقرير الموردين", headers, rows, totals);
    }

    private async Task<SupplierReportVm> BuildSuppliersAsync(CancellationToken ct)
    {
        var suppliers = await _db.Suppliers.OrderBy(s => s.Name).ToListAsync(ct);
        var accounts = await SupplierLedger.AllAsync(_db, ct);
        // «عميل ومورد»: the merged statement — the linked customer account's movements count too, from the party's side
        // as a supplier (a collection from them raises what's owed to them here, a sale lowers it).
        var linkedCustomers = await _db.Customers.Where(c => c.SupplierId != null && !c.IsLead)
            .Select(c => new { c.Id, SupplierId = c.SupplierId!.Value }).ToListAsync(ct);
        var customerAccounts = linkedCustomers.Count > 0 ? await CustomerLedger.AllRowsAsync(_db, ct) : new();
        var customerOf = linkedCustomers.GroupBy(c => c.SupplierId).ToDictionary(g => g.Key, g => g.First().Id);

        return new SupplierReportVm
        {
            Rows = suppliers.Select(s =>
            {
                var rows = accounts.GetValueOrDefault(s.Id)?.Rows ?? new();
                var linked = customerOf.TryGetValue(s.Id, out var customerId);
                var customerRows = linked ? customerAccounts.GetValueOrDefault(customerId) ?? new() : new();
                return new SupplierReportRow
                {
                    Id = s.Id, Name = s.Name, Phone = s.Phone, IsLinked = linked,
                    Credit = rows.Where(r => !r.Decreases).Sum(r => r.Amount) + customerRows.Where(r => r.Decreases).Sum(r => r.Amount),
                    Debit = rows.Where(r => r.Decreases).Sum(r => r.Amount) + customerRows.Where(r => !r.Decreases).Sum(r => r.Amount)
                };
            }).ToList()
        };
    }

    /// <summary>The side of a balance: <paramref name="positive"/> above zero, <paramref name="negative"/> below, empty at zero.</summary>
    private static string Side(decimal v, string positive, string negative) => v > 0 ? positive : v < 0 ? negative : "";
}
