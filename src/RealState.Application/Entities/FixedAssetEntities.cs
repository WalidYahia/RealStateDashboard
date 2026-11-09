using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// An asset category (فئة الأصول: أجهزة، سيارات، أثاث…) and its posting profile. In the chart of accounts every asset has
/// its own accounts under its category's: الأصول › الأصول الثابتة (1500) › the category › the asset, mirrored under
/// مجمع إهلاك الأصول الثابتة (1590). The category's cost / accumulated-depreciation accounts are those group accounts
/// (created named after it); the depreciation expense defaults to «مصروف الإهلاك» (5600).
/// </summary>
public class FixedAssetCategory : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Suggested useful life for new assets of this category (0 = not depreciated, e.g. land).</summary>
    public int DefaultUsefulLifeMonths { get; set; }

    public Guid? AssetAccountId { get; set; }                     // الأصول الثابتة › the category (group of its assets' cost accounts)
    public Guid? AccumulatedDepreciationAccountId { get; set; }   // مجمع الإهلاك › the category (group of its assets' accounts)
    public Guid? DepreciationExpenseAccountId { get; set; }       // مصروف الإهلاك
}

/// <summary>
/// A company fixed asset (أصل ثابت) — what the company owns and its accounting value. Physical custodies (العُهد العينية)
/// reference it to track who is responsible for it; they never change its value. Financial events post through the
/// accounting engine using its category's posting profile: acquisition (cash from a safe, or opening balance),
/// depreciation recorded by the user (<see cref="FixedAssetDepreciation"/>), and disposal / write-off. Numbered
/// FA-{year}{4-digit seq}.
/// </summary>
public class FixedAsset : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }

    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public FixedAssetCategory? Category { get; set; }
    public string? SerialNumber { get; set; }
    /// <summary>Identical units recorded as one asset (e.g. 10 office chairs); it's handed over and valued as a whole.</summary>
    public int Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public DateTime? PurchaseDate { get; set; }
    /// <summary>Where it's kept when not in custody (المخزن، المكتب الرئيسي…).</summary>
    public string? Location { get; set; }
    public string? Notes { get; set; }
    /// <summary>In service and available for custody. A disposed asset is always inactive.</summary>
    public bool IsActive { get; set; } = true;

    // ----- Acquisition (null = recorded before the ledger integration; posted later from the asset page) -----
    public AssetAcquisitionMethod? AcquisitionMethod { get; set; }
    public Guid? PurchaseSafeId { get; set; }
    /// <summary>The expense voucher (سند صرف) of a cash purchase.</summary>
    public Guid? PurchaseTransactionId { get; set; }
    /// <summary>Depreciation already accumulated when an owned asset is brought in as an opening balance.</summary>
    public decimal OpeningAccumulatedDepreciation { get; set; }

    // ----- Depreciation guidance (depreciation itself is recorded by the user — nothing runs on a schedule) -----
    /// <summary>Remaining useful life (0 = not depreciated) — suggests the monthly amount on the depreciation form.</summary>
    public int UsefulLifeMonths { get; set; }
    /// <summary>The value the asset is never depreciated below.</summary>
    public decimal SalvageValue { get; set; }

    // ----- Disposal / write-off -----
    public DateTime? DisposalDate { get; set; }
    public decimal DisposalProceeds { get; set; }
    public Guid? DisposalSafeId { get; set; }
    /// <summary>The income voucher (سند قبض) of the sale proceeds, when sold.</summary>
    public Guid? DisposalTransactionId { get; set; }
    public string? DisposalReason { get; set; }

    public decimal TotalCost => UnitCost * Quantity;
    public bool IsPosted => AcquisitionMethod is not null;
    public bool IsDisposed => DisposalDate is not null;
}

/// <summary>
/// A depreciation the user recorded on an asset (Dr مصروف الإهلاك / Cr مجمع الإهلاك), whenever they choose to — there is
/// no automatic schedule. It never takes the asset below its salvage value.
/// </summary>
public class FixedAssetDepreciation : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FixedAssetId { get; set; }
    public FixedAsset? Asset { get; set; }
    /// <summary>Date + time entered by the user; the journal entry takes it.</summary>
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string? Notes { get; set; }
}
