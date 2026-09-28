using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Reports.Models;

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
        => View(await BuildDailyAsync(date?.Date ?? DateTime.Today, ct));

    [HttpGet]
    public async Task<IActionResult> DailyPrint(DateTime? date, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("DailyPrint", await BuildDailyAsync(date?.Date ?? DateTime.Today, ct));
    }

    private async Task<DailyReportVm> BuildDailyAsync(DateTime day, CancellationToken ct)
    {
        var next = day.AddDays(1);
        var custNames = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c.FullName, ct);
        var unitNames = await _db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);
        var projNames = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var supNames = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s.Name, ct);

        var vm = new DailyReportVm { Date = day };

        var contracts = await _db.SaleContracts.Where(c => c.ContractDate >= day && c.ContractDate < next).ToListAsync(ct);
        vm.Contracts = contracts.OrderBy(c => c.Code).Select(c => new DailyContractRow(
            c.Code, custNames.GetValueOrDefault(c.CustomerId, "—"), unitNames.GetValueOrDefault(c.UnitId, "—"), c.TotalPrice)).ToList();

        // Purchases of the day: purchase invoices, plus legacy orders that still carry a supplier obligation.
        var invoices = await _db.PurchaseInvoices.Where(i => i.InvoiceDate >= day && i.InvoiceDate < next).ToListAsync(ct);
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

        var dayTxns = await _db.SafeTransactions.Where(t => t.OccurredAt >= day && t.OccurredAt < next).ToListAsync(ct);
        // Transfers between safes move money but are neither income nor expense (safe balances below still include them).
        vm.Incomes = dayTxns.Where(t => t.Type == TxnType.Income && t.Source != TxnSource.SafeTransfer).OrderBy(t => t.Serial)
            .Select(t => new DailyTxnRow(t.Serial, t.Description, t.Amount)).ToList();
        vm.Expenses = dayTxns.Where(t => t.Type == TxnType.Expense && t.Source != TxnSource.SafeTransfer).OrderBy(t => t.Serial)
            .Select(t => new DailyTxnRow(t.Serial, t.Description, t.Amount)).ToList();

        // Safe balances as of the end of the selected day.
        var safes = await _db.Safes.OrderBy(s => s.Name).ToListAsync(ct);
        var upToTxns = await _db.SafeTransactions.Where(t => t.OccurredAt < next)
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
    public async Task<IActionResult> Customers(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        return View(await BuildCustomersAsync(from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> CustomersPrint(DateTime? from, DateTime? to, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("CustomersPrint", await BuildCustomersAsync(from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> CustomersCsv(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildCustomersAsync(from, to, ct);
        var headers = new[] { "العميل", "الهاتف", "عدد العقود", "قيمة العقود", "أقساط متبقية", "المحصّل", "المتبقي" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name, r.Phone, r.Contracts, r.ContractsValue, r.RemainingInstallments, r.Collected, r.Residual
        });
        var totals = new object?[] { "الإجمالي", null, vm.TotContracts, vm.TotValue, vm.TotRemInst, vm.TotCollected, vm.TotResidual };
        return RealState.Web.Common.Xlsx.File($"تقرير العملاء {DateTime.Now:yyyy-MM-dd}.xlsx", "تقرير العملاء", headers, rows, totals);
    }

    private async Task<CustomerReportVm> BuildCustomersAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = new CustomerReportVm { From = from, To = to };
        var contracts = await _db.SaleContracts.ToListAsync(ct);
        if (from.HasValue) contracts = contracts.Where(c => c.ContractDate >= from.Value).ToList();
        if (to.HasValue) contracts = contracts.Where(c => c.ContractDate < to.Value.Date.AddDays(1)).ToList();

        var contractIds = contracts.Select(c => c.Id).ToList();
        var installments = await _db.Installments.Where(i => contractIds.Contains(i.SaleContractId)).ToListAsync(ct);
        var instByContract = installments.GroupBy(i => i.SaleContractId).ToDictionary(g => g.Key, g => g.ToList());
        var customers = await _db.Customers.ToDictionaryAsync(c => c.Id, c => c, ct);

        vm.Rows = contracts.GroupBy(c => c.CustomerId).Select(g =>
        {
            customers.TryGetValue(g.Key, out var cust);
            var value = g.Sum(c => c.TotalPrice);
            var insts = g.SelectMany(c => instByContract.GetValueOrDefault(c.Id, new())).ToList();
            var collected = insts.Sum(i => i.PaidAmount);
            return new CustomerReportRow
            {
                Name = cust?.FullName ?? "—",
                Phone = cust?.Phone,
                Contracts = g.Count(),
                ContractsValue = value,
                RemainingInstallments = insts.Count(i => i.PaidAmount < i.Amount),
                Collected = collected,
                Residual = value - collected
            };
        }).OrderByDescending(r => r.ContractsValue).ToList();

        return vm;
    }

    // ---------------- Supplier report ----------------
    public async Task<IActionResult> Suppliers(DateTime? from, DateTime? to, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        return View(await BuildSuppliersAsync(from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> SuppliersPrint(DateTime? from, DateTime? to, CancellationToken ct)
    {
        ViewBag.TenantId = _currentUser.TenantId;
        return View("SuppliersPrint", await BuildSuppliersAsync(from, to, ct));
    }

    [HttpGet]
    public async Task<IActionResult> SuppliersCsv(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = await BuildSuppliersAsync(from, to, ct);
        var headers = new[] { "المورد", "الهاتف", "عدد الفواتير", "قيمة الفواتير", "المسدَّد", "المتبقي" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            r.Name, r.Phone, r.Orders, r.OrdersValue, r.Paid, r.Residual
        });
        var totals = new object?[] { "الإجمالي", null, vm.TotOrders, vm.TotValue, vm.TotPaid, vm.TotResidual };
        return RealState.Web.Common.Xlsx.File($"تقرير الموردين {DateTime.Now:yyyy-MM-dd}.xlsx", "تقرير الموردين", headers, rows, totals);
    }

    private async Task<SupplierReportVm> BuildSuppliersAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        var vm = new SupplierReportVm { From = from, To = to };

        // A supplier's documents: purchase invoices, plus legacy orders that still carry the supplier.
        var invQ = _db.PurchaseInvoices.AsQueryable();
        var ordQ = _db.SupplierOrders.Where(o => o.SupplierId != null);
        if (from.HasValue) { invQ = invQ.Where(i => i.InvoiceDate >= from.Value); ordQ = ordQ.Where(o => o.OrderDate >= from.Value); }
        if (to.HasValue) { invQ = invQ.Where(i => i.InvoiceDate < to.Value.Date.AddDays(1)); ordQ = ordQ.Where(o => o.OrderDate < to.Value.Date.AddDays(1)); }
        var invoices = await invQ.ToListAsync(ct);
        var orders = await ordQ.ToListAsync(ct);

        var invoiceIds = invoices.Select(i => i.Id).ToList();
        var invoiceSums = (await _db.PurchaseInvoiceItems.Where(i => invoiceIds.Contains(i.PurchaseInvoiceId))
            .GroupBy(i => i.PurchaseInvoiceId).Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var paidByInvoice = (await _db.SupplierPayments.Where(p => p.PurchaseInvoiceId != null && invoiceIds.Contains(p.PurchaseInvoiceId!.Value))
            .GroupBy(p => p.PurchaseInvoiceId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var orderIds = orders.Select(o => o.Id).ToList();
        var itemSums = (await _db.SupplierOrderItems.Where(i => orderIds.Contains(i.SupplierOrderId))
            .GroupBy(i => i.SupplierOrderId).Select(g => new { g.Key, Sum = g.Sum(x => x.Cost * x.Quantity) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var paidByOrder = (await _db.SupplierPayments.Where(p => p.SupplierOrderId != null && orderIds.Contains(p.SupplierOrderId!.Value))
            .GroupBy(p => p.SupplierOrderId!.Value).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var suppliers = await _db.Suppliers.ToDictionaryAsync(s => s.Id, s => s, ct);

        var docs = invoices.Select(i => (SupplierId: i.SupplierId, Value: invoiceSums.GetValueOrDefault(i.Id, 0), Paid: paidByInvoice.GetValueOrDefault(i.Id, 0)))
            .Concat(orders.Select(o => (SupplierId: o.SupplierId!.Value, Value: itemSums.GetValueOrDefault(o.Id, 0), Paid: paidByOrder.GetValueOrDefault(o.Id, 0))));

        vm.Rows = docs.GroupBy(d => d.SupplierId).Select(g =>
        {
            suppliers.TryGetValue(g.Key, out var sup);
            var value = g.Sum(d => d.Value);
            var paid = g.Sum(d => d.Paid);
            return new SupplierReportRow
            {
                Name = sup?.Name ?? "—",
                Phone = sup?.Phone,
                Orders = g.Count(),
                OrdersValue = value,
                Paid = paid,
                Residual = value - paid
            };
        }).OrderByDescending(r => r.OrdersValue).ToList();

        return vm;
    }
}
