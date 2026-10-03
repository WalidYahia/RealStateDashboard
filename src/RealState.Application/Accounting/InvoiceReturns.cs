using Microsoft.EntityFrameworkCore;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

/// <summary>What an invoice's returns took off it: the returned value (credit notes) and the cash refunded through them.</summary>
public readonly record struct ReturnTotals(decimal Returned, decimal Refunded)
{
    public static readonly ReturnTotals None = new(0m, 0m);
}

/// <summary>
/// Returns per invoice — the one place invoice balances read them from. With returns, an invoice leaves
/// <c>total − returned − (paid − refunded)</c> owed: a return is a credit note on its invoice, and a cash refund
/// gives back money that had been paid on it.
/// </summary>
public static class InvoiceReturns
{
    /// <summary>Remaining on an invoice: total − returned − (paid − refunded).</summary>
    public static decimal Remaining(decimal total, decimal paid, ReturnTotals r) => total - r.Returned - (paid - r.Refunded);

    /// <summary>Sales returns per sales invoice (all invoices when <paramref name="invoiceIds"/> is null).</summary>
    public static async Task<Dictionary<Guid, ReturnTotals>> SalesAsync(IApplicationDbContext db, IReadOnlyCollection<Guid>? invoiceIds, CancellationToken ct)
    {
        var rets = db.ProductSalesReturns.AsQueryable();
        if (invoiceIds is not null) rets = rets.Where(r => invoiceIds.Contains(r.SalesInvoiceId));
        var refunded = await rets.GroupBy(r => r.SalesInvoiceId).Select(g => new { g.Key, Sum = g.Sum(r => r.RefundAmount) }).ToListAsync(ct);
        var returned = await (from i in db.ProductSalesReturnItems
                              join r in rets on i.SalesReturnId equals r.Id
                              group i by r.SalesInvoiceId into g
                              select new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToListAsync(ct);
        return Merge(returned.Select(x => (x.Key, x.Sum)), refunded.Select(x => (x.Key, x.Sum)));
    }

    /// <summary>Purchase returns per purchase invoice (all invoices when <paramref name="invoiceIds"/> is null).</summary>
    public static async Task<Dictionary<Guid, ReturnTotals>> PurchaseAsync(IApplicationDbContext db, IReadOnlyCollection<Guid>? invoiceIds, CancellationToken ct)
    {
        var rets = db.PurchaseReturns.AsQueryable();
        if (invoiceIds is not null) rets = rets.Where(r => invoiceIds.Contains(r.PurchaseInvoiceId));
        var refunded = await rets.GroupBy(r => r.PurchaseInvoiceId).Select(g => new { g.Key, Sum = g.Sum(r => r.RefundAmount) }).ToListAsync(ct);
        var returned = await (from i in db.PurchaseReturnItems
                              join r in rets on i.PurchaseReturnId equals r.Id
                              group i by r.PurchaseInvoiceId into g
                              select new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToListAsync(ct);
        return Merge(returned.Select(x => (x.Key, x.Sum)), refunded.Select(x => (x.Key, x.Sum)));
    }

    /// <summary>One sales invoice's returns.</summary>
    public static async Task<ReturnTotals> ForSalesInvoiceAsync(IApplicationDbContext db, Guid invoiceId, CancellationToken ct)
        => (await SalesAsync(db, new[] { invoiceId }, ct)).GetValueOrDefault(invoiceId, ReturnTotals.None);

    /// <summary>One purchase invoice's returns.</summary>
    public static async Task<ReturnTotals> ForPurchaseInvoiceAsync(IApplicationDbContext db, Guid invoiceId, CancellationToken ct)
        => (await PurchaseAsync(db, new[] { invoiceId }, ct)).GetValueOrDefault(invoiceId, ReturnTotals.None);

    private static Dictionary<Guid, ReturnTotals> Merge(IEnumerable<(Guid Key, decimal Sum)> returned, IEnumerable<(Guid Key, decimal Sum)> refunded)
    {
        var map = new Dictionary<Guid, ReturnTotals>();
        foreach (var (k, v) in returned) map[k] = map.GetValueOrDefault(k) with { Returned = v };
        foreach (var (k, v) in refunded) map[k] = map.GetValueOrDefault(k) with { Refunded = v };
        return map;
    }
}
