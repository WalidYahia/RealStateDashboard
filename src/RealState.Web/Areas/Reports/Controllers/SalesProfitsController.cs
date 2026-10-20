using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealState.Application.Activity;
using RealState.Application.Common;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Reports.Models;

namespace RealState.Web.Areas.Reports.Controllers;

/// <summary>
/// تقرير أرباح المبيعات — the profit of each product sales invoice in a period, net of its returns:
/// (invoice value − returns) − (cost of the goods issued − cost of the goods returned). Filtered by date (the system's
/// range: الكل / اليوم / آخر أسبوع / آخر شهر), customer and invoice number.
/// </summary>
[Area("Reports")]
[Authorize(Policy = PermissionNames.ReportsSalesProfits)]
public class SalesProfitsController : Controller
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IActivityLogger _activity;

    public SalesProfitsController(IApplicationDbContext db, ICurrentUserService currentUser, IActivityLogger activity)
    {
        _db = db;
        _currentUser = currentUser;
        _activity = activity;
    }

    public async Task<IActionResult> Index(DateTime? from, DateTime? to, Guid? customerId, string? invoiceNo, CancellationToken ct)
    {
        (from, to) = DateFilterDefaults.TodayIfFresh(Request, from, to);
        return View(await BuildAsync(from, to, customerId, invoiceNo, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Print(DateTime? from, DateTime? to, Guid? customerId, string? invoiceNo, CancellationToken ct)
    {
        var vm = await BuildAsync(from, to, customerId, invoiceNo, ct);
        await LogExportAsync("طباعة", vm, ct);
        ViewBag.TenantId = _currentUser.TenantId;
        return View("Print", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Excel(DateTime? from, DateTime? to, Guid? customerId, string? invoiceNo, CancellationToken ct)
    {
        var vm = await BuildAsync(from, to, customerId, invoiceNo, ct);
        await LogExportAsync("تصدير Excel", vm, ct);
        var headers = new[] { "رقم الفاتورة", "التاريخ", "العميل", "قيمة الفاتورة", "المرتجعات", "صافي المبيعات", "التكلفة", "الربح", "هامش الربح %" };
        var rows = vm.Rows.Select(r => (IReadOnlyList<object?>)new object?[]
        {
            $"SI-{r.Number}", RealState.Web.AppCulture.When(r.Date), r.Customer, r.Value, r.Returned, r.Net, r.Cost, r.Profit, r.MarginPct
        });
        var totals = new object?[] { "الإجمالي", null, null, vm.TotValue, vm.TotReturned, vm.TotNet, vm.TotCost, vm.TotProfit, vm.MarginPct };
        return RealState.Web.Common.Xlsx.File($"تقرير أرباح المبيعات {ClientTime.Now:yyyy-MM-dd}.xlsx", "أرباح المبيعات", headers, rows, totals);
    }

    // Reading the report changes nothing (the activity log records changes), but taking its profit figures out of the
    // system — a print or an Excel file — is recorded, with the filters used.
    private async Task LogExportAsync(string what, SalesProfitsReportVm vm, CancellationToken ct)
    {
        var parts = new List<string>();
        if (vm.From.HasValue || vm.To.HasValue)
            parts.Add($"الفترة {(vm.From.HasValue ? RealState.Web.AppCulture.Day(vm.From.Value) : "…")} — {(vm.To.HasValue ? RealState.Web.AppCulture.Day(vm.To.Value) : "…")}");
        else parts.Add("كل الفترات");
        if (vm.CustomerId.HasValue)
            parts.Add("العميل " + (vm.Customers.FirstOrDefault(c => c.Value == vm.CustomerId.Value.ToString())?.Text ?? "—"));
        if (!string.IsNullOrWhiteSpace(vm.InvoiceNo)) parts.Add($"رقم الفاتورة «{vm.InvoiceNo}»");
        try
        {
            await _activity.LogAsync(new ActivityEntry(
                ActionType: ActivityActionType.Other, Controller: "SalesProfits", Action: what == "طباعة" ? "Print" : "Excel",
                Method: Request.Method, Area: "Reports", Path: Request.Path.Value,
                Description: $"{what} تقرير أرباح المبيعات ({string.Join("، ", parts)}) — {vm.Rows.Count} فاتورة، صافي الربح {vm.TotProfit:N2} ج.م",
                IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
        }
        catch
        {
            // Never let audit logging break the export.
        }
    }

    private async Task<SalesProfitsReportVm> BuildAsync(DateTime? from, DateTime? to, Guid? customerId, string? invoiceNo, CancellationToken ct)
    {
        var vm = new SalesProfitsReportVm { From = from, To = to, CustomerId = customerId, InvoiceNo = invoiceNo?.Trim() };

        // Customers that have sales invoices (the customer filter's options).
        var custIds = _db.ProductSalesInvoices.Select(i => i.CustomerId).Distinct();
        var customers = await _db.Customers.Where(c => custIds.Contains(c.Id)).OrderBy(c => c.FullName)
            .Select(c => new { c.Id, c.FullName }).ToListAsync(ct);
        vm.Customers = customers.Select(c => new SelectListItem(c.FullName, c.Id.ToString())).ToList();
        var custNames = customers.ToDictionary(c => c.Id, c => c.FullName);

        var (start, end) = ClientTime.DayRange(from, to);   // [start, end) of the picked days
        var q = _db.ProductSalesInvoices.AsQueryable();
        if (start.HasValue) q = q.Where(i => i.InvoiceDate >= start.Value);
        if (end.HasValue) q = q.Where(i => i.InvoiceDate < end.Value);
        if (customerId.HasValue) q = q.Where(i => i.CustomerId == customerId.Value);
        // Invoice no.: "SI-2026000012", "2026000012" or part of it.
        var digits = new string((vm.InvoiceNo ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length > 0) q = q.Where(i => i.Number.ToString().Contains(digits));
        var invoices = await q.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.Number)
            .Select(i => new { i.Id, i.Number, i.InvoiceDate, i.CustomerId }).ToListAsync(ct);
        var ids = invoices.Select(i => i.Id).ToList();

        // Invoice values.
        var values = await _db.ProductSalesInvoiceItems.Where(x => ids.Contains(x.SalesInvoiceId)).GroupBy(x => x.SalesInvoiceId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        // Cost of sales booked by each invoice's posted goods issue.
        var issued = (await (from l in _db.GoodsIssueLines
                             join g in _db.GoodsIssues on l.GoodsIssueId equals g.Id
                             where g.SalesInvoiceId != null && ids.Contains(g.SalesInvoiceId.Value) && g.Status == InventoryDocStatus.Posted
                             select new { InvoiceId = g.SalesInvoiceId!.Value, l.TotalCost }).ToListAsync(ct))
            .GroupBy(x => x.InvoiceId).ToDictionary(g => g.Key, g => g.Sum(x => x.TotalCost));

        // Returns on those invoices (whatever their date — they belong to the invoice's profit): value back, cost back.
        var returns = await _db.ProductSalesReturns.Where(r => ids.Contains(r.SalesInvoiceId))
            .Select(r => new { r.Id, r.SalesInvoiceId }).ToListAsync(ct);
        var retIds = returns.Select(r => r.Id).ToList();
        var retInvoice = returns.ToDictionary(r => r.Id, r => r.SalesInvoiceId);
        var retValues = (await _db.ProductSalesReturnItems.Where(x => retIds.Contains(x.SalesReturnId))
                .Select(x => new { x.SalesReturnId, x.LineTotal }).ToListAsync(ct))
            .GroupBy(x => retInvoice[x.SalesReturnId]).ToDictionary(g => g.Key, g => g.Sum(x => x.LineTotal));
        var retCosts = (await (from l in _db.GoodsReceiptLines
                               join g in _db.GoodsReceipts on l.GoodsReceiptId equals g.Id
                               where g.SalesReturnId != null && retIds.Contains(g.SalesReturnId.Value) && g.Status == InventoryDocStatus.Posted
                               select new { ReturnId = g.SalesReturnId!.Value, l.TotalCost }).ToListAsync(ct))
            .GroupBy(x => retInvoice[x.ReturnId]).ToDictionary(g => g.Key, g => g.Sum(x => x.TotalCost));

        vm.Rows = invoices.Select(i => new SalesProfitRow
        {
            InvoiceId = i.Id, Number = i.Number, Date = i.InvoiceDate,
            Customer = custNames.GetValueOrDefault(i.CustomerId, "—"),
            Value = values.GetValueOrDefault(i.Id),
            Returned = retValues.GetValueOrDefault(i.Id),
            Cost = issued.GetValueOrDefault(i.Id) - retCosts.GetValueOrDefault(i.Id),
        }).ToList();
        return vm;
    }
}
