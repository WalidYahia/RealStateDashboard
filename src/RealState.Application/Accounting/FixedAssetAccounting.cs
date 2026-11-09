using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

/// <summary>
/// Depreciation arithmetic of an asset (no data access). Depreciation itself is recorded by the user — these only bound it
/// and suggest an amount.
/// </summary>
public static class FixedAssetSchedule
{
    /// <summary>Total depreciation over the asset's life: cost − salvage − what was already depreciated before it came in.</summary>
    public static decimal Depreciable(FixedAsset a) => Math.Max(0, a.TotalCost - a.SalvageValue - a.OpeningAccumulatedDepreciation);

    /// <summary>What may still be depreciated: book value down to the salvage value.</summary>
    public static decimal Remaining(FixedAsset a, decimal accumulated) => Math.Max(0, a.TotalCost - a.SalvageValue - accumulated);

    /// <summary>A suggested monthly amount — straight line over the useful life (0 without one).</summary>
    public static decimal SuggestedMonthly(FixedAsset a) => a.UsefulLifeMonths > 0 ? Math.Round(Depreciable(a) / a.UsefulLifeMonths, 2) : 0;
}

/// <summary>The accounts an asset posts to: its own cost and accumulated-depreciation accounts, and its category's expense.</summary>
public sealed record FixedAssetAccounts(Guid CostAccountId, Guid AccumulatedDepreciationAccountId, Guid DepreciationExpenseAccountId);

/// <summary>
/// Posts the financial events of fixed assets through the accounting engine. In the chart of accounts every asset has its
/// own accounts under its category's: الأصول › الأصول الثابتة (1500) › the category › «FA-… — the asset», mirrored under
/// مجمع إهلاك الأصول الثابتة (1590); the depreciation expense is the category's (default «مصروف الإهلاك»).
/// Custody (العُهد العينية) never posts anything — it only tracks responsibility. None of these call SaveChanges.
/// </summary>
public interface IFixedAssetAccounting
{
    /// <summary>The category's accounts: its groups under 1500 / 1590 (named after it; they hold its assets' accounts) and
    /// the depreciation expense (5600 unless picked).</summary>
    Task EnsureCategoryAccountsAsync(FixedAssetCategory category, CancellationToken ct = default);

    /// <summary>The asset's own cost / accumulated-depreciation accounts under its category's (created on first use).</summary>
    Task<FixedAssetAccounts> AccountsAsync(FixedAsset asset, CancellationToken ct = default);

    /// <summary>Acquisition: cash → سند صرف + Dr cost / Cr safe; opening balance → Dr cost / Cr مجمع الإهلاك (prior
    /// depreciation) / Cr رصيد افتتاحي. Set <see cref="FixedAsset.AcquisitionMethod"/> (and the safe) before calling.</summary>
    Task PostAcquisitionAsync(FixedAsset asset, CancellationToken ct = default);

    /// <summary>Reverses the acquisition entry (and removes a cash purchase's voucher).</summary>
    Task RemoveAcquisitionAsync(FixedAsset asset, CancellationToken ct = default);

    /// <summary>A recorded depreciation: Dr مصروف الإهلاك / Cr مجمع الإهلاك, dated as entered.</summary>
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

    // Account.SubKind of the category groups and of each asset's own accounts under them.
    public const string CategoryCostKind = "AssetCategory";
    public const string CategoryAccumulatedKind = "AssetCategoryDepreciation";
    public const string AssetCostKind = "FixedAsset";
    public const string AssetAccumulatedKind = "FixedAssetAccumulated";

    public static string CategoryAccumulatedName(string category) => $"مجمع إهلاك — {category}";
    public static string AssetCostName(FixedAsset a) => $"{FA(a)} — {a.Name}";
    public static string AssetAccumulatedName(FixedAsset a) => $"مجمع إهلاك — {FA(a)} — {a.Name}";

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
        // The category's own groups (its assets' accounts hang under them) — not postable themselves.
        var cost = await GroupAsync(LedgerAccounts.FixedAssetsCost, CategoryCostKind, c.Id, c.Name, ct);
        var accumulated = await GroupAsync(LedgerAccounts.AccumulatedDepreciation, CategoryAccumulatedKind, c.Id, CategoryAccumulatedName(c.Name), ct);
        c.AssetAccountId = cost.Id;
        c.AccumulatedDepreciationAccountId = accumulated.Id;
        c.DepreciationExpenseAccountId ??= (await _engine.ResolveAccountAsync(LedgerAccounts.DepreciationExpense, null, null, null, ct)).Id;
    }

    /// <summary>A category's group under 1500 / 1590 — created (not postable) the first time.</summary>
    private async Task<Account> GroupAsync(string control, string kind, Guid categoryId, string name, CancellationToken ct)
    {
        var existed = _db.Accounts.Local.Any(a => a.SubKind == kind && a.SubRefId == categoryId && !a.IsDeleted)
                      || await _db.Accounts.AnyAsync(a => a.SubKind == kind && a.SubRefId == categoryId, ct);
        var group = await _engine.ResolveAccountAsync(control, kind, categoryId, name, ct);
        if (!existed) group.IsPostable = false;
        return group;
    }

    private async Task<Account> AccountAsync(Guid id, CancellationToken ct) =>
        _db.Accounts.Local.FirstOrDefault(a => a.Id == id) ?? await _db.Accounts.FirstAsync(a => a.Id == id, ct);

    public async Task<FixedAssetAccounts> AccountsAsync(FixedAsset a, CancellationToken ct = default)
    {
        var c = (a.CategoryId is Guid id
                    ? a.Category ?? _db.FixedAssetCategories.Local.FirstOrDefault(x => x.Id == id)
                      ?? await _db.FixedAssetCategories.FirstOrDefaultAsync(x => x.Id == id, ct)
                    : null)
                ?? throw new InvalidOperationException($"الأصل {FA(a)} بلا فئة — حدِّد فئته أولًا.");
        await EnsureCategoryAccountsAsync(c, ct);
        var cost = await _engine.ResolveSubAccountAsync(await AccountAsync(c.AssetAccountId!.Value, ct), AssetCostKind, a.Id, AssetCostName(a), ct);
        var accumulated = await _engine.ResolveSubAccountAsync(await AccountAsync(c.AccumulatedDepreciationAccountId!.Value, ct),
            AssetAccumulatedKind, a.Id, AssetAccumulatedName(a), ct);
        return new FixedAssetAccounts(cost.Id, accumulated.Id, c.DepreciationExpenseAccountId!.Value);
    }

    private async Task<Guid> SafeAccountAsync(Guid safeId, CancellationToken ct)
    {
        var name = await _db.Safes.IgnoreQueryFilters().Where(s => s.Id == safeId).Select(s => s.Name).FirstOrDefaultAsync(ct);
        return (await _engine.ResolveAccountAsync(LedgerAccounts.CashAndBanks, "Safe", safeId, name, ct)).Id;
    }

    public async Task PostAcquisitionAsync(FixedAsset a, CancellationToken ct = default)
    {
        if (a.TotalCost <= 0) return;
        var c = await AccountsAsync(a, ct);
        var date = a.PurchaseDate ?? DateTime.Today;
        var desc = $"اقتناء الأصل الثابت {FA(a)} — {a.Name}";

        if (a.AcquisitionMethod == AssetAcquisitionMethod.Cash)
        {
            var txn = await _accounting.AddVoucherAsync(a.PurchaseSafeId!.Value, TxnType.Expense, TxnSource.FixedAssetPurchase,
                a.TotalCost, date, $"شراء أصل ثابت {FA(a)} — {a.Name}", ct);
            a.PurchaseTransactionId = txn.Id;
            await _engine.PostByIdsAsync(date, desc, "SafeTransaction", new[]
            {
                (c.CostAccountId, a.TotalCost, 0m, (string?)desc),
                (await SafeAccountAsync(a.PurchaseSafeId!.Value, ct), 0m, a.TotalCost, (string?)desc),
            }, ct, txn.Id);
            return;
        }

        // Opening balance: an asset the company already owns, possibly part-depreciated.
        var equity = (await _engine.ResolveAccountAsync(LedgerAccounts.OpeningBalanceEquity, null, null, null, ct)).Id;
        var lines = new List<(Guid, decimal, decimal, string?)> { (c.CostAccountId, a.TotalCost, 0m, desc) };
        if (a.OpeningAccumulatedDepreciation > 0)
            lines.Add((c.AccumulatedDepreciationAccountId, 0m, a.OpeningAccumulatedDepreciation, "إهلاك سابق"));
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
        var c = await AccountsAsync(a, ct);
        var desc = $"إهلاك الأصل {FA(a)} — {a.Name}" + (string.IsNullOrWhiteSpace(d.Notes) ? "" : $" — {d.Notes.Trim()}");
        await _engine.PostByIdsAsync(d.Date, desc, DepreciationSource, new[]
        {
            (c.DepreciationExpenseAccountId, d.Amount, 0m, (string?)desc),
            (c.AccumulatedDepreciationAccountId, 0m, d.Amount, (string?)desc),
        }, ct, d.Id);
    }

    public Task RemoveDepreciationAsync(FixedAssetDepreciation d, CancellationToken ct = default) =>
        _engine.RemoveBySourceAsync(DepreciationSource, d.Id, ct);

    public async Task PostDisposalAsync(FixedAsset a, decimal accumulated, CancellationToken ct = default)
    {
        var c = await AccountsAsync(a, ct);
        var date = a.DisposalDate!.Value;
        var desc = (a.DisposalProceeds > 0 ? "بيع" : "استبعاد") + $" الأصل الثابت {FA(a)} — {a.Name}";
        var lines = new List<(Guid, decimal, decimal, string?)>();
        if (accumulated > 0) lines.Add((c.AccumulatedDepreciationAccountId, accumulated, 0m, "إقفال مجمع الإهلاك"));
        if (a.DisposalProceeds > 0)
        {
            // The proceeds' voucher carries no entry of its own — its cash line is part of this disposal entry.
            var txn = await _accounting.AddVoucherAsync(a.DisposalSafeId!.Value, TxnType.Income, TxnSource.FixedAssetSale,
                a.DisposalProceeds, date, $"متحصلات بيع أصل ثابت {FA(a)} — {a.Name}", ct);
            a.DisposalTransactionId = txn.Id;
            lines.Add((await SafeAccountAsync(a.DisposalSafeId!.Value, ct), a.DisposalProceeds, 0m, "متحصلات البيع"));
        }
        lines.Add((c.CostAccountId, 0m, a.TotalCost, "إقفال تكلفة الأصل"));

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
