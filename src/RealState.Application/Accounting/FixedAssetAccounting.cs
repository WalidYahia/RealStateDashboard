using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

/// <summary>Straight-line monthly depreciation schedule of an asset (pure arithmetic — no data access).</summary>
public static class FixedAssetSchedule
{
    /// <summary>Total depreciation over the asset's life: cost − salvage − what was already depreciated before it came in.</summary>
    public static decimal Depreciable(FixedAsset a) => Math.Max(0, a.TotalCost - a.SalvageValue - a.OpeningAccumulatedDepreciation);

    public static DateTime MonthOf(DateTime d) => new(d.Year, d.Month, 1);

    /// <summary>Every (month, amount) of the schedule; the last month absorbs the rounding.</summary>
    public static IEnumerable<(DateTime Period, decimal Amount)> Months(FixedAsset a)
    {
        var total = Depreciable(a);
        if (a.UsefulLifeMonths <= 0 || total <= 0 || a.DepreciationStartDate is null) yield break;
        var monthly = Math.Round(total / a.UsefulLifeMonths, 2);
        var start = MonthOf(a.DepreciationStartDate.Value);
        for (var i = 0; i < a.UsefulLifeMonths; i++)
            yield return (start.AddMonths(i), i == a.UsefulLifeMonths - 1 ? total - monthly * (a.UsefulLifeMonths - 1) : monthly);
    }

    public static DateTime EndOfMonth(DateTime period) => MonthOf(period).AddMonths(1).AddDays(-1);
}

/// <summary>
/// Posts the financial events of fixed assets through the accounting engine, using each asset's category posting profile.
/// Custody (العُهد العينية) never posts anything — it only tracks responsibility. None of these call SaveChanges.
/// </summary>
public interface IFixedAssetAccounting
{
    /// <summary>Fills the category's empty accounts: cost under 1500, accumulated depreciation under 1590 (both named after
    /// the category), depreciation expense = 5600.</summary>
    Task EnsureCategoryAccountsAsync(FixedAssetCategory category, CancellationToken ct = default);

    /// <summary>Acquisition: cash → سند صرف + Dr cost / Cr safe; opening balance → Dr cost / Cr مجمع الإهلاك (prior
    /// depreciation) / Cr رصيد افتتاحي. Set <see cref="FixedAsset.AcquisitionMethod"/> (and the safe) before calling.</summary>
    Task PostAcquisitionAsync(FixedAsset asset, CancellationToken ct = default);

    /// <summary>Reverses the acquisition entry (and removes a cash purchase's voucher).</summary>
    Task RemoveAcquisitionAsync(FixedAsset asset, CancellationToken ct = default);

    /// <summary>One month: Dr مصروف الإهلاك / Cr مجمع الإهلاك, dated the month's last day.</summary>
    Task PostDepreciationAsync(FixedAssetDepreciation depreciation, FixedAsset asset, CancellationToken ct = default);

    Task RemoveDepreciationAsync(FixedAssetDepreciation depreciation, CancellationToken ct = default);

    /// <summary>Disposal / write-off: Dr مجمع الإهلاك (to date) + Dr safe (proceeds, with a سند قبض) / Cr cost, and the
    /// difference to أرباح / خسائر استبعاد الأصول. Set the disposal fields on the asset before calling.</summary>
    Task PostDisposalAsync(FixedAsset asset, decimal accumulatedDepreciation, CancellationToken ct = default);

    /// <summary>Reverses the disposal entry and removes the proceeds voucher.</summary>
    Task RemoveDisposalAsync(FixedAsset asset, CancellationToken ct = default);
}

public sealed class FixedAssetAccounting : IFixedAssetAccounting
{
    public const string AcquisitionSource = "FixedAsset";
    public const string DepreciationSource = "FixedAssetDepreciation";
    public const string DisposalSource = "FixedAssetDisposal";

    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    private readonly IAccountingService _accounting;

    public FixedAssetAccounting(IApplicationDbContext db, IAccountingEngine engine, IAccountingService accounting)
    {
        _db = db;
        _engine = engine;
        _accounting = accounting;
    }

    private static string FA(FixedAsset a) => "FA-" + a.Number;

    public async Task EnsureCategoryAccountsAsync(FixedAssetCategory c, CancellationToken ct = default)
    {
        c.AssetAccountId ??= (await _engine.ResolveAccountAsync(LedgerAccounts.FixedAssetsCost, "AssetCategory", c.Id, c.Name, ct)).Id;
        c.AccumulatedDepreciationAccountId ??= (await _engine.ResolveAccountAsync(LedgerAccounts.AccumulatedDepreciation,
            "AssetCategoryDepreciation", c.Id, $"مجمع إهلاك — {c.Name}", ct)).Id;
        c.DepreciationExpenseAccountId ??= (await _engine.ResolveAccountAsync(LedgerAccounts.DepreciationExpense, null, null, null, ct)).Id;
    }

    private async Task<FixedAssetCategory> CategoryAsync(FixedAsset a, CancellationToken ct)
    {
        var c = a.Category
                ?? _db.FixedAssetCategories.Local.FirstOrDefault(x => x.Id == a.CategoryId)
                ?? await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.Id == a.CategoryId, ct)
                ?? throw new InvalidOperationException($"الأصل {FA(a)} بلا فئة — حدِّد فئته أولًا.");
        await EnsureCategoryAccountsAsync(c, ct);
        return c;
    }

    private async Task<Guid> SafeAccountAsync(Guid safeId, CancellationToken ct)
    {
        var name = await _db.Safes.IgnoreQueryFilters().Where(s => s.Id == safeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return (await _engine.ResolveAccountAsync(LedgerAccounts.CashAndBanks, "Safe", safeId, name, ct)).Id;
    }

    public async Task PostAcquisitionAsync(FixedAsset a, CancellationToken ct = default)
    {
        if (a.TotalCost <= 0) return;
        var c = await CategoryAsync(a, ct);
        var date = a.PurchaseDate ?? DateTime.Today;
        var desc = $"اقتناء الأصل الثابت {FA(a)} — {a.Name}";

        if (a.AcquisitionMethod == AssetAcquisitionMethod.Cash)
        {
            var txn = await _accounting.AddVoucherAsync(a.PurchaseSafeId!.Value, TxnType.Expense, TxnSource.FixedAssetPurchase,
                a.TotalCost, date, $"شراء أصل ثابت {FA(a)} — {a.Name}", ct);
            a.PurchaseTransactionId = txn.Id;
            await _engine.PostByIdsAsync(date, desc, "SafeTransaction", new[]
            {
                (c.AssetAccountId!.Value, a.TotalCost, 0m, (string?)desc),
                (await SafeAccountAsync(a.PurchaseSafeId!.Value, ct), 0m, a.TotalCost, (string?)desc),
            }, ct, txn.Id);
            return;
        }

        // Opening balance: an asset the company already owns, possibly part-depreciated.
        var equity = (await _engine.ResolveAccountAsync(LedgerAccounts.OpeningBalanceEquity, null, null, null, ct)).Id;
        var lines = new List<(Guid, decimal, decimal, string?)> { (c.AssetAccountId!.Value, a.TotalCost, 0m, desc) };
        if (a.OpeningAccumulatedDepreciation > 0)
            lines.Add((c.AccumulatedDepreciationAccountId!.Value, 0m, a.OpeningAccumulatedDepreciation, "إهلاك سابق"));
        var net = a.TotalCost - a.OpeningAccumulatedDepreciation;
        if (net > 0) lines.Add((equity, 0m, net, "رصيد افتتاحي"));
        await _engine.PostByIdsAsync(date, desc + " (رصيد افتتاحي)", AcquisitionSource, lines, ct, a.Id);
    }

    public async Task RemoveAcquisitionAsync(FixedAsset a, CancellationToken ct = default)
    {
        if (a.PurchaseTransactionId is Guid txnId && await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == txnId, ct) is { } txn)
            await _accounting.RemoveTransactionAsync(txn, ct);   // the voucher + its entry
        await _engine.RemoveBySourceAsync(AcquisitionSource, a.Id, ct);
        a.PurchaseTransactionId = null;
    }

    public async Task PostDepreciationAsync(FixedAssetDepreciation d, FixedAsset a, CancellationToken ct = default)
    {
        var c = await CategoryAsync(a, ct);
        var desc = $"إهلاك الأصل {FA(a)} — {a.Name} عن {d.Period:yyyy/MM}";
        await _engine.PostByIdsAsync(FixedAssetSchedule.EndOfMonth(d.Period), desc, DepreciationSource, new[]
        {
            (c.DepreciationExpenseAccountId!.Value, d.Amount, 0m, (string?)desc),
            (c.AccumulatedDepreciationAccountId!.Value, 0m, d.Amount, (string?)desc),
        }, ct, d.Id);
    }

    public Task RemoveDepreciationAsync(FixedAssetDepreciation d, CancellationToken ct = default) =>
        _engine.RemoveBySourceAsync(DepreciationSource, d.Id, ct);

    public async Task PostDisposalAsync(FixedAsset a, decimal accumulated, CancellationToken ct = default)
    {
        var c = await CategoryAsync(a, ct);
        var date = a.DisposalDate!.Value;
        var desc = (a.DisposalProceeds > 0 ? "بيع" : "استبعاد") + $" الأصل الثابت {FA(a)} — {a.Name}";
        var lines = new List<(Guid, decimal, decimal, string?)>();
        if (accumulated > 0) lines.Add((c.AccumulatedDepreciationAccountId!.Value, accumulated, 0m, "إقفال مجمع الإهلاك"));
        if (a.DisposalProceeds > 0)
        {
            // The proceeds' voucher carries no entry of its own — its cash line is part of this disposal entry.
            var txn = await _accounting.AddVoucherAsync(a.DisposalSafeId!.Value, TxnType.Income, TxnSource.FixedAssetSale,
                a.DisposalProceeds, date, $"متحصلات بيع أصل ثابت {FA(a)} — {a.Name}", ct);
            a.DisposalTransactionId = txn.Id;
            lines.Add((await SafeAccountAsync(a.DisposalSafeId!.Value, ct), a.DisposalProceeds, 0m, "متحصلات البيع"));
        }
        lines.Add((c.AssetAccountId!.Value, 0m, a.TotalCost, "إقفال تكلفة الأصل"));

        // Gain = proceeds − book value (cost − accumulated depreciation).
        var gain = a.DisposalProceeds - (a.TotalCost - accumulated);
        if (gain > 0)
            lines.Add(((await _engine.ResolveAccountAsync(LedgerAccounts.AssetDisposalGain, null, null, null, ct)).Id, 0m, gain, "أرباح الاستبعاد"));
        else if (gain < 0)
            lines.Add(((await _engine.ResolveAccountAsync(LedgerAccounts.AssetDisposalLoss, null, null, null, ct)).Id, -gain, 0m, "خسائر الاستبعاد"));
        await _engine.PostByIdsAsync(date, desc, DisposalSource, lines, ct, a.Id);
    }

    public async Task RemoveDisposalAsync(FixedAsset a, CancellationToken ct = default)
    {
        await _engine.RemoveBySourceAsync(DisposalSource, a.Id, ct);
        if (a.DisposalTransactionId is Guid txnId && await _db.SafeTransactions.FirstOrDefaultAsync(t => t.Id == txnId, ct) is { } txn)
            _db.SafeTransactions.Remove(txn);   // no entry of its own
        a.DisposalTransactionId = null;
    }
}
