using Microsoft.EntityFrameworkCore;
using RealState.Application.Accounting;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.Suppliers.Models;
using RealState.Web.Models;
using static RealState.Web.Areas.Suppliers.Controllers.PurchasingLookups;

namespace RealState.Web.Areas.Suppliers.Controllers;

/// <summary>An obligation (purchase invoice, or legacy order) with its total and what's been paid on it.</summary>
/// <summary>Paid is net of refunds; Returned is the value of the invoice's purchase returns.</summary>
internal sealed record Obligation(SupplierLedgerKind Kind, Guid Id, string Label, DateTime Date, string Statement, decimal Total, decimal Paid, decimal Returned = 0)
{
    public decimal Remaining => Total - Returned - Paid;
}

/// <summary>A supplier's account movements (unordered, no running balance yet) and its overall totals.</summary>
internal sealed record SupplierLedgerData(List<SupplierLedgerRow> Rows, decimal TotalObligations, decimal TotalPaid,
    int InvoicesCount, int PaymentsCount)
{
    /// <summary>What's owed to the supplier now (− = owed by them).</summary>
    public decimal Balance => TotalObligations - TotalPaid;
}

/// <summary>
/// The supplier account statement's movements — shared by the supplier's own statement, the merged statement of a party
/// that is both a customer and a supplier (عميل ومورد), and the customers / suppliers reports (every supplier's balance).
/// </summary>
internal static class SupplierLedger
{
    /// <summary>The supplier's obligations: every purchase invoice, plus legacy orders that still carry the supplier.</summary>
    public static async Task<List<Obligation>> ObligationsAsync(IApplicationDbContext db, Guid supplierId, CancellationToken ct) =>
        (await ObligationsBySupplierAsync(db, supplierId, ct)).GetValueOrDefault(supplierId) ?? new();

    /// <summary>Every movement on the supplier's account: obligations (+), payments (−), returns (−), refunds (+),
    /// the opening balance and on-account vouchers — with the overall totals.</summary>
    public static async Task<SupplierLedgerData> BuildAsync(IApplicationDbContext db, Guid supplierId, CancellationToken ct) =>
        (await LoadAsync(db, supplierId, ct)).GetValueOrDefault(supplierId) ?? new SupplierLedgerData(new(), 0, 0, 0, 0);

    /// <summary>Every supplier's account, by supplier — loaded in one pass (for the reports).</summary>
    public static Task<Dictionary<Guid, SupplierLedgerData>> AllAsync(IApplicationDbContext db, CancellationToken ct) => LoadAsync(db, null, ct);

    /// <summary>
    /// Fills <see cref="SupplierLedgerRow.Items"/> of the invoice, order and return rows — the lines the detailed statement
    /// («تفصيلي») lists under them. Called with the rows actually shown, so the reports never load lines.
    /// </summary>
    public static async Task AttachItemsAsync(IApplicationDbContext db, IReadOnlyCollection<SupplierLedgerRow> rows, CancellationToken ct)
    {
        List<Guid> Ids(SupplierLedgerKind kind) => rows.Where(r => r.Kind == kind).Select(r => r.Id).Distinct().ToList();
        var invIds = Ids(SupplierLedgerKind.Invoice);
        var ordIds = Ids(SupplierLedgerKind.Order);
        var retIds = Ids(SupplierLedgerKind.Return);
        // By document id (Guids — unique across the three tables).
        var items = new Dictionary<Guid, List<StatementItem>>();
        if (invIds.Count > 0)
            foreach (var g in (await db.PurchaseInvoiceItems.Where(x => invIds.Contains(x.PurchaseInvoiceId)).OrderBy(x => x.CreatedAt).ToListAsync(ct))
                         .GroupBy(x => x.PurchaseInvoiceId))
                items[g.Key] = g.Select(x => new StatementItem(x.Name, x.Unit, x.Quantity, x.Cost, x.LineTotal)).ToList();
        if (ordIds.Count > 0)
            foreach (var g in (await db.SupplierOrderItems.Where(x => ordIds.Contains(x.SupplierOrderId)).OrderBy(x => x.CreatedAt).ToListAsync(ct))
                         .GroupBy(x => x.SupplierOrderId))
                items[g.Key] = g.Select(x => new StatementItem(x.Name, x.Unit, x.Quantity, x.Cost, Math.Round(x.Cost * x.Quantity, 2))).ToList();
        if (retIds.Count > 0)
            foreach (var g in (await db.PurchaseReturnItems.Where(x => retIds.Contains(x.PurchaseReturnId)).OrderBy(x => x.CreatedAt).ToListAsync(ct))
                         .GroupBy(x => x.PurchaseReturnId))
                items[g.Key] = g.Select(x => new StatementItem(x.Name, x.Unit, x.Quantity, x.Cost, x.LineTotal)).ToList();

        foreach (var r in rows)
            if (r.Kind is SupplierLedgerKind.Invoice or SupplierLedgerKind.Order or SupplierLedgerKind.Return && items.TryGetValue(r.Id, out var list))
                r.Items = list;
    }

    /// <summary>The obligations of one supplier (<paramref name="only"/>) or of all of them, grouped by supplier.</summary>
    private static async Task<Dictionary<Guid, List<Obligation>>> ObligationsBySupplierAsync(IApplicationDbContext db, Guid? only, CancellationToken ct)
    {
        // (For all suppliers the id lists aren't sent as filters — they'd only be every row anyway.)
        var invoices = await db.PurchaseInvoices.Where(i => only == null || i.SupplierId == only).ToListAsync(ct);
        var invIds = invoices.Select(i => i.Id).ToList();
        var invItemsQ = db.PurchaseInvoiceItems.AsQueryable();
        if (only != null) invItemsQ = invItemsQ.Where(i => invIds.Contains(i.PurchaseInvoiceId));
        var invItems = (await invItemsQ.ToListAsync(ct)).GroupBy(i => i.PurchaseInvoiceId).ToDictionary(g => g.Key, g => g.ToList());

        var orders = await db.SupplierOrders.Where(o => o.SupplierId != null && (only == null || o.SupplierId == only)).ToListAsync(ct);
        var ordIds = orders.Select(o => o.Id).ToList();
        var ordItemsQ = db.SupplierOrderItems.AsQueryable();
        if (only != null) ordItemsQ = ordItemsQ.Where(i => ordIds.Contains(i.SupplierOrderId));
        var ordItems = (await ordItemsQ.ToListAsync(ct)).GroupBy(i => i.SupplierOrderId).ToDictionary(g => g.Key, g => g.ToList());

        var payments = await db.SupplierPayments.Where(p => only == null || p.SupplierId == only).ToListAsync(ct);
        var paidByInvoice = payments.Where(p => p.PurchaseInvoiceId.HasValue)
            .GroupBy(p => p.PurchaseInvoiceId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));
        // Purchase returns lower an invoice's total; cash the supplier refunded through them is no longer "paid".
        var returns = await InvoiceReturns.PurchaseAsync(db, only == null ? null : invIds, ct);
        var paidByOrder = payments.Where(p => p.SupplierOrderId.HasValue)
            .GroupBy(p => p.SupplierOrderId!.Value).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount));

        string Names(IEnumerable<string> names, string fallback)
        {
            var list = names.ToList();
            return list.Count > 0 ? string.Join("، ", list) : fallback;
        }

        return invoices.Select(i =>
            {
                var items = invItems.GetValueOrDefault(i.Id, new());
                var r = returns.GetValueOrDefault(i.Id);
                return (i.SupplierId, Obligation: new Obligation(SupplierLedgerKind.Invoice, i.Id, $"فاتورة مشتريات رقم {PI(i.Number)}", i.InvoiceDate,
                    Names(items.Select(x => x.Name), "فاتورة مشتريات"), items.Sum(x => x.LineTotal),
                    paidByInvoice.GetValueOrDefault(i.Id, 0) - r.Refunded, r.Returned));
            })
            .Concat(orders.Select(o =>
            {
                var items = ordItems.GetValueOrDefault(o.Id, new());
                return (SupplierId: o.SupplierId!.Value, Obligation: new Obligation(SupplierLedgerKind.Order, o.Id, $"أمر توريد رقم {PO(o.Number)}", o.OrderDate,
                    Names(items.Select(x => x.Name), "أمر توريد"), Math.Round(items.Sum(x => x.Cost * x.Quantity), 2), paidByOrder.GetValueOrDefault(o.Id, 0)));
            }))
            .GroupBy(x => x.SupplierId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Obligation).ToList());
    }

    /// <summary>The account of one supplier (<paramref name="only"/>) or of all of them, by supplier.</summary>
    private static async Task<Dictionary<Guid, SupplierLedgerData>> LoadAsync(IApplicationDbContext db, Guid? only, CancellationToken ct)
    {
        var obligations = await ObligationsBySupplierAsync(db, only, ct);
        var payments = await db.SupplierPayments.Where(p => only == null || p.SupplierId == only).ToListAsync(ct);
        var rets = await db.PurchaseReturns.Where(r => only == null || r.SupplierId == only).ToListAsync(ct);
        var retIds = rets.Select(r => r.Id).ToList();
        var retItemsQ = db.PurchaseReturnItems.AsQueryable();
        if (only != null) retItemsQ = retItemsQ.Where(i => retIds.Contains(i.PurchaseReturnId));
        var retTotals = await retItemsQ.GroupBy(i => i.PurchaseReturnId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var invNos = await db.PurchaseInvoices.Where(i => only == null || i.SupplierId == only).ToDictionaryAsync(i => i.Id, i => i.Number, ct);
        string InvLabel(Guid id) => invNos.TryGetValue(id, out var n) ? PI(n) : "—";
        // The opening balance (الأرصدة الافتتاحية): credit = owed to the supplier (+), debit = owed by them (−).
        var openings = (await db.AccountOpeningBalances.AsNoTracking()
                .Where(b => b.Account!.SubKind == "Supplier" && b.Account.SubRefId != null && (only == null || b.Account.SubRefId == only))
                .Select(b => new { SupplierId = b.Account!.SubRefId!.Value, b.IsDebit, b.Date, b.Amount }).ToListAsync(ct))
            .GroupBy(o => o.SupplierId).ToDictionary(g => g.Key, g => g.First());
        // Vouchers on the supplier's account from المصروفات / الإيرادات (سند صرف −, سند قبض +).
        var accountVouchers = (await db.SafeTransactions
                .Where(t => t.Source == TxnSource.SupplierAccount && t.SupplierId != null && (only == null || t.SupplierId == only)).ToListAsync(ct))
            .GroupBy(t => t.SupplierId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var paymentsBySupplier = payments.GroupBy(p => p.SupplierId).ToDictionary(g => g.Key, g => g.ToList());
        var retsBySupplier = rets.GroupBy(r => r.SupplierId).ToDictionary(g => g.Key, g => g.ToList());

        var supplierIds = obligations.Keys.Concat(paymentsBySupplier.Keys).Concat(retsBySupplier.Keys)
            .Concat(openings.Keys).Concat(accountVouchers.Keys).Distinct();
        var result = new Dictionary<Guid, SupplierLedgerData>();
        foreach (var supplierId in supplierIds)
        {
            var obl = obligations.GetValueOrDefault(supplierId) ?? new();
            var pays = paymentsBySupplier.GetValueOrDefault(supplierId) ?? new();
            var supRets = retsBySupplier.GetValueOrDefault(supplierId) ?? new();
            var opening = openings.GetValueOrDefault(supplierId);
            var vouchers = accountVouchers.GetValueOrDefault(supplierId) ?? new();

            // Build one ledger row per obligation (+) and per payment (settlement, −).
            var rows = obl.Select(o => new SupplierLedgerRow
            {
                Kind = o.Kind, Id = o.Id, Source = o.Label, Date = o.Date, Statement = o.Statement, Amount = o.Total
            }).ToList();
            rows.AddRange(pays.Select(p => new SupplierLedgerRow
            {
                Kind = SupplierLedgerKind.Payment,
                Id = p.Id,
                Source = "إيصال صرف نقدية",
                Date = p.PaidDate,
                Statement = $"إيصال صرف نقدية رقم {p.ReceiptNo:D5}",
                ReceiptNo = p.ReceiptNo,
                Amount = p.Amount
            }));
            // Purchase returns (debit notes, −) and the cash the supplier refunded through them (+).
            rows.AddRange(supRets.Select(r => new SupplierLedgerRow
            {
                Kind = SupplierLedgerKind.Return, Id = r.Id, Source = $"مرتجع مشتريات رقم PR-{r.Number}", Date = r.ReturnDate,
                Statement = $"مرتجع على الفاتورة {InvLabel(r.PurchaseInvoiceId)}" + (string.IsNullOrWhiteSpace(r.Notes) ? "" : $" — {r.Notes}"),
                Amount = retTotals.GetValueOrDefault(r.Id)
            }));
            rows.AddRange(supRets.Where(r => r.RefundAmount > 0).Select(r => new SupplierLedgerRow
            {
                Kind = SupplierLedgerKind.Refund, Id = r.Id, Source = "إيصال استلام نقدية", Date = r.ReturnDate,
                Statement = $"استرداد نقدية عن مرتجع المشتريات PR-{r.Number} (إيصال استلام نقدية رقم {r.RefundVoucherNo})",
                ReceiptNo = r.RefundVoucherNo ?? 0, Amount = r.RefundAmount
            }));
            if (opening is not null)
                rows.Add(new SupplierLedgerRow
                {
                    Kind = opening.IsDebit ? SupplierLedgerKind.OpeningDebit : SupplierLedgerKind.OpeningCredit, Id = supplierId,
                    Source = "رصيد افتتاحي", Date = opening.Date, Amount = opening.Amount,
                    Statement = opening.IsDebit ? "رصيد افتتاحي مدين — مستحق على المورد" : "رصيد افتتاحي دائن — مستحق للمورد"
                });
            rows.AddRange(vouchers.Select(t => new SupplierLedgerRow
            {
                Kind = t.Type == TxnType.Expense ? SupplierLedgerKind.AccountPayment : SupplierLedgerKind.AccountReceipt,
                Id = t.Id, ReceiptNo = t.Serial, Date = t.OccurredAt, Amount = t.Amount,
                Source = t.Type == TxnType.Expense ? "سند صرف" : "سند قبض",
                Statement = (t.Type == TxnType.Expense ? $"سداد على الحساب — سند صرف رقم {t.Serial}" : $"قبض من المورد على الحساب — سند قبض رقم {t.Serial}")
                            + (string.IsNullOrWhiteSpace(t.Description) ? "" : $" — {t.Description}")
            }));

            result[supplierId] = new SupplierLedgerData(rows,
                // Net of purchase returns, and of the cash the supplier refunded through them.
                TotalObligations: obl.Sum(o => o.Total - o.Returned) + (opening is { IsDebit: false } ? opening.Amount : 0m),
                TotalPaid: pays.Sum(p => p.Amount) - supRets.Sum(r => r.RefundAmount) + (opening is { IsDebit: true } ? opening.Amount : 0m)
                           + vouchers.Where(t => t.Type == TxnType.Expense).Sum(t => t.Amount) - vouchers.Where(t => t.Type == TxnType.Income).Sum(t => t.Amount),
                InvoicesCount: obl.Count,
                PaymentsCount: pays.Count + vouchers.Count(t => t.Type == TxnType.Expense));
        }
        return result;
    }
}
