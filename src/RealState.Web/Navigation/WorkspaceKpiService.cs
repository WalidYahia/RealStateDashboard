using Microsoft.EntityFrameworkCore;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Web.Navigation;

/// <summary>A small indicator on a page card. <see cref="State"/>: "" (normal) | "warn" | "danger" | "ok".</summary>
public sealed record NavKpi(string Text, string State = "");

public interface IWorkspaceKpiService
{
    /// <summary>Indicators for the pages of ONE module the user can see — a handful of aggregate counts, only when
    /// that module's workspace is opened (the home workspace computes none).</summary>
    Task<Dictionary<string, NavKpi>> GetAsync(NavModuleVm module, CancellationToken ct = default);
}

/// <summary>
/// Page-card KPIs for a module workspace. Each is one cheap aggregate (COUNT / grouped SUM) and is computed only for
/// pages present in the user's authorized module — the business numbers come from the data, not from the view.
/// </summary>
public sealed class WorkspaceKpiService : IWorkspaceKpiService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    public WorkspaceKpiService(IApplicationDbContext db, ICurrentUserService user) { _db = db; _user = user; }

    public async Task<Dictionary<string, NavKpi>> GetAsync(NavModuleVm module, CancellationToken ct = default)
    {
        var shown = module.Pages.Select(p => p.Key).ToHashSet();
        var k = new Dictionary<string, NavKpi>();
        bool Has(string key) => shown.Contains(key);
        static string N(int n) => n.ToString("N0");
        var today = DateTime.Today;

        if (Has("my-tasks") && _user.UserId is Guid uid)
        {
            var open = await _db.WorkTasks.CountAsync(t => t.Status != WorkTaskStatus.Completed
                && _db.Employees.Any(e => e.Id == t.AssigneeEmployeeId && e.UserId == uid), ct);
            if (open > 0) k["my-tasks"] = new($"{N(open)} مهمة مفتوحة", "warn");
        }
        if (Has("projects")) k["projects"] = new($"{N(await _db.Projects.CountAsync(ct))} مشروع");
        if (Has("contracts")) k["contracts"] = new($"{N(await _db.SaleContracts.CountAsync(ct))} عقد");
        if (Has("collections"))
        {
            var overdue = await _db.Installments.CountAsync(i => i.DueDate < today && i.Amount > 0 && i.PaidAmount < i.Amount, ct);
            k["collections"] = overdue > 0 ? new($"{N(overdue)} قسط متأخر", "danger") : new("لا متأخرات", "ok");
        }
        if (Has("customers")) k["customers"] = new($"{N(await _db.Customers.CountAsync(c => !c.IsLead, ct))} عميل");
        if (Has("leads")) k["leads"] = new($"{N(await _db.Customers.CountAsync(c => c.IsLead, ct))} عميل محتمل");
        if (Has("sales-invoices"))
        {
            var open = await (from i in _db.ProductSalesInvoices
                              let total = _db.ProductSalesInvoiceItems.Where(x => x.SalesInvoiceId == i.Id).Sum(x => (decimal?)x.LineTotal) ?? 0m
                              let paid = _db.SalesInvoiceCollections.Where(x => x.SalesInvoiceId == i.Id).Sum(x => (decimal?)x.Amount) ?? 0m
                              let returned = (from x in _db.ProductSalesReturnItems
                                              join r in _db.ProductSalesReturns on x.SalesReturnId equals r.Id
                                              where r.SalesInvoiceId == i.Id
                                              select (decimal?)x.LineTotal).Sum() ?? 0m
                              let refunded = _db.ProductSalesReturns.Where(r => r.SalesInvoiceId == i.Id).Sum(r => (decimal?)r.RefundAmount) ?? 0m
                              where total - returned > paid - refunded
                              select i.Id).CountAsync(ct);
            if (open > 0) k["sales-invoices"] = new($"{N(open)} فاتورة غير محصّلة", "warn");
        }
        if (Has("suppliers")) k["suppliers"] = new($"{N(await _db.Suppliers.CountAsync(ct))} مورد");
        if (Has("purchase-invoices"))
        {
            var open = await (from i in _db.PurchaseInvoices
                              let total = _db.PurchaseInvoiceItems.Where(x => x.PurchaseInvoiceId == i.Id).Sum(x => (decimal?)x.LineTotal) ?? 0m
                              let paid = _db.SupplierPayments.Where(x => x.PurchaseInvoiceId == i.Id).Sum(x => (decimal?)x.Amount) ?? 0m
                              let returned = (from x in _db.PurchaseReturnItems
                                              join r in _db.PurchaseReturns on x.PurchaseReturnId equals r.Id
                                              where r.PurchaseInvoiceId == i.Id
                                              select (decimal?)x.LineTotal).Sum() ?? 0m
                              let refunded = _db.PurchaseReturns.Where(r => r.PurchaseInvoiceId == i.Id).Sum(r => (decimal?)r.RefundAmount) ?? 0m
                              where total - returned > paid - refunded
                              select i.Id).CountAsync(ct);
            if (open > 0) k["purchase-invoices"] = new($"{N(open)} فاتورة غير مسددة", "warn");
        }
        if (Has("products")) k["products"] = new($"{N(await _db.Products.CountAsync(p => p.IsActive, ct))} صنف");
        if (Has("warehouses")) k["warehouses"] = new($"{N(await _db.Warehouses.CountAsync(w => w.IsActive, ct))} مخزن");
        if (Has("inventory-dashboard"))
        {
            // Out-of-stock / below-reorder tracked products — one grouped query over the movements.
            var qty = await _db.InventoryMovements.GroupBy(m => m.ProductId)
                .Select(g => new { g.Key, Q = g.Sum(m => m.QuantityIn - m.QuantityOut) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
            var products = await _db.Products.Where(p => p.IsActive && p.TrackInventory).Select(p => new { p.Id, p.ReorderLevel }).ToListAsync(ct);
            var alerts = products.Count(p => { var q = qty.GetValueOrDefault(p.Id); return q <= 0 || (p.ReorderLevel > 0 && q <= p.ReorderLevel); });
            k["inventory-dashboard"] = alerts > 0 ? new($"{N(alerts)} تنبيه مخزون", "warn") : new("لا تنبيهات", "ok");
        }
        foreach (var (key, count) in new[]
                 {
                     ("goods-receipts", Has("goods-receipts") ? await _db.GoodsReceipts.CountAsync(d => d.Status == InventoryDocStatus.Draft, ct) : 0),
                     ("goods-issues", Has("goods-issues") ? await _db.GoodsIssues.CountAsync(d => d.Status == InventoryDocStatus.Draft, ct) : 0),
                     ("stock-transfers", Has("stock-transfers") ? await _db.StockTransfers.CountAsync(d => d.Status == InventoryDocStatus.Draft, ct) : 0),
                 })
            if (count > 0) k[key] = new($"{N(count)} مسودة غير مُرحّلة", "warn");
        if (Has("safes")) k["safes"] = new($"{N(await _db.Safes.CountAsync(s => s.IsActive, ct))} خزنة");
        if (Has("campaigns")) k["campaigns"] = new($"{N(await _db.Campaigns.CountAsync(ct))} حملة");
        if (Has("employees")) k["employees"] = new($"{N(await _db.Employees.CountAsync(ct))} موظف");
        return k;
    }
}
