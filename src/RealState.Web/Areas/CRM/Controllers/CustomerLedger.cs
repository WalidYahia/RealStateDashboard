using Microsoft.EntityFrameworkCore;
using RealState.Application.Enums;
using RealState.Application.Interfaces;
using RealState.Web.Areas.CRM.Models;
using RealState.Web.Areas.Suppliers.Controllers;
using RealState.Web.Models;

namespace RealState.Web.Areas.CRM.Controllers;

/// <summary>
/// The customer account statement's movements — shared by the customer's statement (also the merged statement of a
/// «عميل ومورد» party) and the customers / suppliers reports (every customer's current balance).
/// </summary>
internal static class CustomerLedger
{
    /// <summary>
    /// Everything the customer owes (+: contracts, sales invoices, refunds paid back to them) and everything that settles
    /// it (−: installment / invoice collections, sales returns), unordered and without the running balance.
    /// </summary>
    public static async Task<List<CustomerLedgerRow>> RowsAsync(IApplicationDbContext db, Guid customerId, CancellationToken ct) =>
        (await LoadAsync(db, customerId, ct)).GetValueOrDefault(customerId) ?? new();

    /// <summary>The customer's current balance, as at the end of their statement: + = owed by them, − = owed to them.</summary>
    public static async Task<decimal> BalanceAsync(IApplicationDbContext db, Guid customerId, CancellationToken ct) =>
        (await RowsAsync(db, customerId, ct)).Sum(r => r.Decreases ? -r.Amount : r.Amount);

    /// <summary>Every customer's account movements, by customer — loaded in one pass (for the reports).</summary>
    public static Task<Dictionary<Guid, List<CustomerLedgerRow>>> AllRowsAsync(IApplicationDbContext db, CancellationToken ct) =>
        LoadAsync(db, null, ct);

    /// <summary>
    /// Fills <see cref="CustomerLedgerRow.Items"/> of the sales invoice and return rows (and, «عميل ومورد», of the linked
    /// supplier's documents) — the lines the detailed statement («تفصيلي») lists under them. Called with the rows actually
    /// shown, so the reports never load lines.
    /// </summary>
    public static async Task AttachItemsAsync(IApplicationDbContext db, IReadOnlyCollection<CustomerLedgerRow> rows, CancellationToken ct)
    {
        var invIds = rows.Where(r => r.Kind == CustomerLedgerKind.Invoice).Select(r => r.Id).Distinct().ToList();
        var retIds = rows.Where(r => r.Kind == CustomerLedgerKind.Return).Select(r => r.Id).Distinct().ToList();
        // By document id (Guids — unique across both tables).
        var items = new Dictionary<Guid, List<StatementItem>>();
        if (invIds.Count > 0)
            foreach (var g in (await db.ProductSalesInvoiceItems.Where(x => invIds.Contains(x.SalesInvoiceId)).OrderBy(x => x.CreatedAt).ToListAsync(ct))
                         .GroupBy(x => x.SalesInvoiceId))
                items[g.Key] = g.Select(x => new StatementItem(x.Name, x.Unit, x.Quantity, x.Price, x.LineTotal)).ToList();
        if (retIds.Count > 0)
            foreach (var g in (await db.ProductSalesReturnItems.Where(x => retIds.Contains(x.SalesReturnId)).OrderBy(x => x.CreatedAt).ToListAsync(ct))
                         .GroupBy(x => x.SalesReturnId))
                items[g.Key] = g.Select(x => new StatementItem(x.Name, x.Unit, x.Quantity, x.Price, x.LineTotal)).ToList();

        var supplierRows = rows.Where(r => r.SupplierRow is not null).Select(r => r.SupplierRow!).ToList();
        if (supplierRows.Count > 0) await SupplierLedger.AttachItemsAsync(db, supplierRows, ct);

        foreach (var r in rows)
            if (r.SupplierRow is { } s) r.Items = s.Items;
            else if (r.Kind is CustomerLedgerKind.Invoice or CustomerLedgerKind.Return && items.TryGetValue(r.Id, out var list)) r.Items = list;
    }

    /// <summary>The movements of one customer (<paramref name="only"/>) or of all of them, grouped by customer.</summary>
    private static async Task<Dictionary<Guid, List<CustomerLedgerRow>>> LoadAsync(IApplicationDbContext db, Guid? only, CancellationToken ct)
    {
        var byCustomer = new Dictionary<Guid, List<CustomerLedgerRow>>();
        void Add(Guid customerId, CustomerLedgerRow row)
        {
            if (!byCustomer.TryGetValue(customerId, out var list)) byCustomer[customerId] = list = new();
            list.Add(row);
        }
        var projNames = await db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var unitNames = await db.ProjectUnits.ToDictionaryAsync(u => u.Id, u => u.Name + (string.IsNullOrEmpty(u.Number) ? "" : $" ({u.Number})"), ct);

        // Real-estate contracts and their collections.
        var contracts = await db.SaleContracts.Where(c => only == null || c.CustomerId == only).ToListAsync(ct);
        var contractIds = contracts.Select(c => c.Id).ToList();
        var contractById = contracts.ToDictionary(c => c.Id);
        foreach (var c in contracts)
            Add(c.CustomerId, new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.Contract, Id = c.Id, Source = $"عقد بيع {c.Code}", Date = c.ContractDate,
                Statement = $"{projNames.GetValueOrDefault(c.ProjectId, "—")} / {unitNames.GetValueOrDefault(c.UnitId, "—")}", Amount = c.TotalPrice
            });

        var installments = only == null
            ? await db.Installments.ToListAsync(ct)
            : await db.Installments.Where(i => contractIds.Contains(i.SaleContractId)).ToListAsync(ct);
        var instIds = installments.Select(i => i.Id).ToList();
        // (For all customers the id lists aren't sent as filters — they'd only be every row anyway.)
        var collectionsQ = db.SafeTransactions.Where(t => t.Source == TxnSource.Collection && t.InstallmentId != null);
        if (only != null) collectionsQ = collectionsQ.Where(t => instIds.Contains(t.InstallmentId!.Value));
        var collections = await collectionsQ.ToListAsync(ct);
        var collectionsByInst = collections.GroupBy(t => t.InstallmentId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var i in installments)
        {
            if (!contractById.TryGetValue(i.SaleContractId, out var c)) continue;
            var phrase = $"تحصيل {i.InstPhrase()} — عقد {c.Code}";
            var instCollections = collectionsByInst.GetValueOrDefault(i.Id) ?? new();
            foreach (var t in instCollections)
                Add(c.CustomerId, new CustomerLedgerRow
                {
                    Kind = CustomerLedgerKind.InstallmentCollection, Id = i.SaleContractId, VoucherId = t.Id,
                    Source = $"سند قبض رقم {t.Serial}", Date = t.OccurredAt, Statement = phrase, Amount = t.Amount
                });
            // Older collections recorded on the installment without a voucher still count.
            var missing = i.PaidAmount - instCollections.Sum(t => t.Amount);
            if (missing > 0)
                Add(c.CustomerId, new CustomerLedgerRow
                {
                    Kind = CustomerLedgerKind.InstallmentCollection, Id = i.SaleContractId,
                    Source = i.ReceiptNo is int r ? $"إيصال رقم {r}" : "تحصيل", Date = i.PaidDate ?? i.DueDate, Statement = phrase, Amount = missing
                });
        }

        // Cancelled contracts (فسخ التعاقد): the contract is reversed, the fee kept is owed, and what's paid back settles
        // the credit the customer is left with.
        foreach (var c in contracts.Where(c => c.IsCancelled))
        {
            var at = c.CancelledAt ?? c.ContractDate;
            Add(c.CustomerId, new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.ContractCancel, Id = c.Id, Source = $"فسخ عقد بيع {c.Code}", Date = at,
                Statement = $"فسخ التعاقد — {projNames.GetValueOrDefault(c.ProjectId, "—")} / {unitNames.GetValueOrDefault(c.UnitId, "—")}"
                            + (string.IsNullOrWhiteSpace(c.CancelReason) ? "" : $" — {c.CancelReason}"),
                Amount = c.TotalPrice
            });
            Add(c.CustomerId, new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.CancelFee, Id = c.Id, Source = $"رسوم فسخ {c.Code}", Date = at,
                Statement = $"رسوم فسخ التعاقد {c.CancelFeePercent:0.##}% من المدفوع ({c.CancelPaidAmount:N2} ج.م)", Amount = c.CancelFeeAmount
            });
        }
        var cancelRefundsQ = db.SafeTransactions.Where(t => t.Source == TxnSource.ContractCancelRefund && t.SaleContractId != null);
        if (only != null) cancelRefundsQ = cancelRefundsQ.Where(t => contractIds.Contains(t.SaleContractId!.Value));
        var cancelRefunds = await cancelRefundsQ.ToListAsync(ct);
        foreach (var t in cancelRefunds)
            if (contractById.TryGetValue(t.SaleContractId!.Value, out var c))
                Add(c.CustomerId, new CustomerLedgerRow
                {
                    Kind = CustomerLedgerKind.Refund, Id = c.Id, VoucherId = t.Id,
                    Source = $"سند صرف رقم {t.Serial}", Date = t.OccurredAt,
                    Statement = $"رد مستحقات عن فسخ عقد البيع {c.Code}", Amount = t.Amount
                });

        // Product sales invoices, their collections, returns and refunds.
        var invoices = await db.ProductSalesInvoices.Where(i => only == null || i.CustomerId == only).ToListAsync(ct);
        var invIds = invoices.Select(i => i.Id).ToList();
        var invNo = invoices.ToDictionary(i => i.Id, i => i.Number);
        var invItemsQ = db.ProductSalesInvoiceItems.AsQueryable();
        if (only != null) invItemsQ = invItemsQ.Where(x => invIds.Contains(x.SalesInvoiceId));
        var invTotals = await invItemsQ.GroupBy(x => x.SalesInvoiceId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        foreach (var i in invoices)
            Add(i.CustomerId, new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.Invoice, Id = i.Id, Source = $"فاتورة مبيعات SI-{i.Number}", Date = i.InvoiceDate,
                Statement = string.IsNullOrWhiteSpace(i.Notes) ? "فاتورة مبيعات أصناف" : i.Notes!, Amount = invTotals.GetValueOrDefault(i.Id)
            });

        var invCollections = await db.SalesInvoiceCollections.Where(c => only == null || c.CustomerId == only).ToListAsync(ct);
        var voucherSerials = await db.SafeTransactions
            .Where(t => t.Source == TxnSource.SalesInvoiceCollection || t.Source == TxnSource.SalesReturnRefund)
            .Select(t => new { t.Id, t.Serial }).ToDictionaryAsync(t => t.Id, t => t.Serial, ct);
        foreach (var c in invCollections)
            Add(c.CustomerId, new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.InvoiceCollection, Id = c.SalesInvoiceId, VoucherId = c.SafeTransactionId,
                Source = c.SafeTransactionId is Guid v && voucherSerials.TryGetValue(v, out var sn) ? $"سند قبض رقم {sn}" : "تحصيل",
                Date = c.CollectedDate, Statement = $"تحصيل فاتورة مبيعات SI-{invNo.GetValueOrDefault(c.SalesInvoiceId)}", Amount = c.Amount
            });

        var returns = await db.ProductSalesReturns.Where(r => only == null || r.CustomerId == only).ToListAsync(ct);
        var retIds = returns.Select(r => r.Id).ToList();
        var retItemsQ = db.ProductSalesReturnItems.AsQueryable();
        if (only != null) retItemsQ = retItemsQ.Where(x => retIds.Contains(x.SalesReturnId));
        var retTotals = await retItemsQ.GroupBy(x => x.SalesReturnId)
            .Select(g => new { g.Key, Sum = g.Sum(x => x.LineTotal) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        foreach (var r in returns)
        {
            Add(r.CustomerId, new CustomerLedgerRow
            {
                Kind = CustomerLedgerKind.Return, Id = r.Id, Source = $"مرتجع مبيعات SR-{r.Number}", Date = r.ReturnDate,
                Statement = $"مرتجع على الفاتورة SI-{invNo.GetValueOrDefault(r.SalesInvoiceId)}" + (string.IsNullOrWhiteSpace(r.Notes) ? "" : $" — {r.Notes}"),
                Amount = retTotals.GetValueOrDefault(r.Id)
            });
            if (r.RefundAmount > 0)
                Add(r.CustomerId, new CustomerLedgerRow
                {
                    Kind = CustomerLedgerKind.Refund, Id = r.Id, VoucherId = r.RefundTransactionId,
                    Source = r.RefundVoucherNo is long vn ? $"سند صرف رقم {vn}" : "رد نقدية", Date = r.ReturnDate,
                    Statement = $"رد نقدية للعميل عن مرتجع المبيعات SR-{r.Number}", Amount = r.RefundAmount
                });
        }

        // The opening balance (الأرصدة الافتتاحية): debit = owed by the customer (+), credit = owed to them (−).
        var openings = await db.AccountOpeningBalances.AsNoTracking()
            .Where(b => b.Account!.SubKind == "Customer" && b.Account.SubRefId != null && (only == null || b.Account.SubRefId == only))
            .Select(b => new { CustomerId = b.Account!.SubRefId!.Value, b.IsDebit, b.Date, b.Amount }).ToListAsync(ct);
        foreach (var o in openings)
            Add(o.CustomerId, new CustomerLedgerRow
            {
                Kind = o.IsDebit ? CustomerLedgerKind.OpeningDebit : CustomerLedgerKind.OpeningCredit, Id = o.CustomerId,
                Source = "رصيد افتتاحي", Date = o.Date, Amount = o.Amount,
                Statement = o.IsDebit ? "رصيد افتتاحي مدين — مستحق على العميل" : "رصيد افتتاحي دائن — مستحق للعميل"
            });

        // Vouchers on the customer's account from المصروفات / الإيرادات (سند قبض −, سند صرف +).
        var accountVouchers = await db.SafeTransactions
            .Where(t => t.Source == TxnSource.CustomerAccount && t.CustomerId != null && (only == null || t.CustomerId == only)).ToListAsync(ct);
        foreach (var t in accountVouchers)
            Add(t.CustomerId!.Value, new CustomerLedgerRow
            {
                Kind = t.Type == TxnType.Income ? CustomerLedgerKind.AccountReceipt : CustomerLedgerKind.AccountPayment,
                Id = t.Id, VoucherId = t.Id,
                Source = t.Type == TxnType.Income ? $"سند قبض رقم {t.Serial}" : $"سند صرف رقم {t.Serial}",
                Date = t.OccurredAt,
                Statement = (t.Type == TxnType.Income ? "تحصيل على الحساب" : "صرف للعميل على الحساب") + (string.IsNullOrWhiteSpace(t.Description) ? "" : $" — {t.Description}"),
                Amount = t.Amount
            });
        return byCustomer;
    }
}
