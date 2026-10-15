using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>
/// An asset category (فئة الأصول: أجهزة، سيارات، أثاث…) and its posting profile — which chart-of-accounts accounts its assets
/// post to. Left empty on save, each account is created under its control account (1500 / 1590) named after the category;
/// the depreciation expense defaults to «مصروف الإهلاك» (5600). Assets never carry account ids of their own.
/// </summary>
public class FixedAssetCategory : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Suggested useful life for new assets of this category (0 = not depreciated, e.g. land).</summary>
    public int DefaultUsefulLifeMonths { get; set; }

    public Guid? AssetAccountId { get; set; }                     // الأصول الثابتة — cost
    public Guid? AccumulatedDepreciationAccountId { get; set; }   // مجمع الإهلاك (contra-asset)
    public Guid? DepreciationExpenseAccountId { get; set; }       // مصروف الإهلاك
}

/// <summary>
/// A company fixed asset (أصل ثابت) — what the company owns and its accounting value. Physical custodies (العُهد العينية)
/// reference it to track who is responsible for it; they never change its value. Financial events post through the
/// accounting engine using its category's posting profile: acquisition (cash from a safe, or opening balance), monthly
/// straight-line depreciation, and disposal / write-off. Numbered FA-{year}{4-digit seq}.
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

    // ----- Depreciation (straight-line, monthly) -----
    /// <summary>Remaining useful life from <see cref="DepreciationStartDate"/> (0 = not depreciated).</summary>
    public int UsefulLifeMonths { get; set; }
    public decimal SalvageValue { get; set; }
    public DateTime? DepreciationStartDate { get; set; }

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

/// <summary>One month of straight-line depreciation posted for an asset (Dr مصروف الإهلاك / Cr مجمع الإهلاك).</summary>
public class FixedAssetDepreciation : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid FixedAssetId { get; set; }
    public FixedAsset? Asset { get; set; }
    /// <summary>First day of the depreciated month; the entry is dated its last day.</summary>
    public DateTime Period { get; set; }
    public decimal Amount { get; set; }
}
