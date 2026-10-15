using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Accounting.Models;

// ======================================================================================
//  فئات الأصول الثابتة — categories and their posting profile (chart-of-accounts accounts)
// ======================================================================================

public class FixedAssetCategoryFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم الفئة مطلوب")][Display(Name = "اسم الفئة")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 1200, ErrorMessage = "من 0 إلى 1200 شهر")][Display(Name = "العمر الإنتاجي الافتراضي (بالأشهر)")]
    public int DefaultUsefulLifeMonths { get; set; } = 60;

    [Display(Name = "حساب تكلفة الأصول")] public Guid? AssetAccountId { get; set; }
    [Display(Name = "حساب مجمع الإهلاك")] public Guid? AccumulatedDepreciationAccountId { get; set; }
    [Display(Name = "حساب مصروف الإهلاك")] public Guid? DepreciationExpenseAccountId { get; set; }

    /// <summary>Postable asset accounts (cost and accumulated depreciation pickers).</summary>
    public List<SelectListItem> AssetAccounts { get; set; } = new();
    /// <summary>Postable expense accounts (depreciation expense picker).</summary>
    public List<SelectListItem> ExpenseAccounts { get; set; } = new();
    public bool HasPostings { get; set; }
}

public class FixedAssetCategoryRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int DefaultUsefulLifeMonths { get; set; }
    public string? AssetAccount { get; set; }
    public string? AccumulatedAccount { get; set; }
    public string? ExpenseAccount { get; set; }
    public int AssetsCount { get; set; }
    public decimal Cost { get; set; }
    public decimal BookValue { get; set; }
}

// ======================================================================================
//  الأصول الثابتة — the register: what the company owns and its accounting value
// ======================================================================================

public class FixedAssetFormModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }

    [Required(ErrorMessage = "اسم الأصل مطلوب")][Display(Name = "اسم الأصل")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "اختر الفئة")][Display(Name = "الفئة")]
    public Guid? CategoryId { get; set; }

    [Display(Name = "الرقم التسلسلي / الكود")] public string? SerialNumber { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "الكمية يجب أن تكون 1 أو أكثر")][Display(Name = "الكمية")]
    public int Quantity { get; set; } = 1;

    [Range(0.01, 999_999_999_999, ErrorMessage = "أدخل تكلفة أكبر من صفر")][Display(Name = "تكلفة الوحدة (ج.م)")]
    public decimal UnitCost { get; set; }

    [DataType(DataType.Date)][Display(Name = "تاريخ الشراء / الاقتناء")] public DateTime PurchaseDate { get; set; } = ClientTime.Today;
    [Display(Name = "مكان الحفظ")] public string? Location { get; set; }
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }
    [Display(Name = "في الخدمة (متاح للتسليم كعهدة)")] public bool IsActive { get; set; } = true;

    // Acquisition (new assets only — fixed once posted).
    [Display(Name = "طريقة الاقتناء")] public AssetAcquisitionMethod AcquisitionMethod { get; set; } = AssetAcquisitionMethod.Cash;
    [Display(Name = "تُدفع من خزنة")] public Guid? SafeId { get; set; }
    [Range(0, 999_999_999_999, ErrorMessage = "قيمة غير صالحة")][Display(Name = "إهلاك سابق متراكم (ج.م)")]
    public decimal OpeningAccumulatedDepreciation { get; set; }

    // Depreciation (straight-line, monthly).
    [Range(0, 1200, ErrorMessage = "من 0 إلى 1200 شهر")][Display(Name = "العمر الإنتاجي المتبقي (بالأشهر)")]
    public int UsefulLifeMonths { get; set; }
    [Range(0, 999_999_999_999, ErrorMessage = "قيمة غير صالحة")][Display(Name = "القيمة التخريدية (ج.م)")]
    public decimal SalvageValue { get; set; }
    [DataType(DataType.Date)][Display(Name = "بداية الإهلاك")] public DateTime? DepreciationStartDate { get; set; }

    /// <summary>Editing a posted asset: cost / quantity / category / acquisition are fixed (they're in the ledger).</summary>
    public bool FinancialsLocked { get; set; }
    /// <summary>Depreciation already posted: the schedule fields are fixed too (undo the runs first).</summary>
    public bool ScheduleLocked { get; set; }

    public List<SelectListItem> Categories { get; set; } = new();
    /// <summary>Category id → its default useful life (prefills the field).</summary>
    public Dictionary<Guid, int> CategoryLives { get; set; } = new();
    public List<SelectListItem> Safes { get; set; } = new();
}

public class FixedAssetRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CategoryId { get; set; }
    public string? Category { get; set; }
    public string? SerialNumber { get; set; }
    public int Quantity { get; set; }
    public decimal Cost { get; set; }
    public decimal Accumulated { get; set; }
    public decimal BookValue => Cost - Accumulated;
    public DateTime? PurchaseDate { get; set; }
    public bool IsActive { get; set; }
    public bool IsPosted { get; set; }
    public bool IsDisposed { get; set; }
    /// <summary>Current custodian (active custody), if any.</summary>
    public string? Custodian { get; set; }
    public Guid? CustodyId { get; set; }
    public int? CustodyNumber { get; set; }

    public string StateText => IsDisposed ? "مُستبعد" : !IsActive ? "خارج الخدمة" : Custodian is not null ? "في عهدة" : "متاح";
    public string StateClass => IsDisposed ? "state-off" : !IsActive ? "badge-role" : Custodian is not null ? "state-warn" : "state-on";
}

public class FixedAssetListVm
{
    public List<FixedAssetRow> Rows { get; set; } = new();
    public Guid? CategoryId { get; set; }
    /// <summary>available · custody · inactive · disposed · unposted</summary>
    public string? State { get; set; }
    public string? Q { get; set; }
    public List<SelectListItem> Categories { get; set; } = new();

    // Register-wide counters (not filtered, disposed excluded).
    public decimal TotalCost { get; set; }
    public decimal TotalAccumulated { get; set; }
    public decimal TotalBookValue => TotalCost - TotalAccumulated;
    public int InCustodyCount { get; set; }
    public int AvailableCount { get; set; }
    public int UnpostedCount { get; set; }
}

public class FixedAssetDetailsVm
{
    public FixedAsset Asset { get; set; } = default!;
    public FixedAssetCategory? Category { get; set; }
    public string? PurchaseSafe { get; set; }
    public long? PurchaseVoucherSerial { get; set; }
    public long? DisposalVoucherSerial { get; set; }
    public decimal Accumulated { get; set; }
    public decimal BookValue => Asset.TotalCost - Accumulated;
    public decimal MonthlyDepreciation { get; set; }
    public int MonthsPosted { get; set; }
    public List<FixedAssetDepreciation> Depreciations { get; set; } = new();
    /// <summary>Every custody of this asset, newest first (history is never deleted).</summary>
    public List<MaterialCustodyRow> Custodies { get; set; } = new();
    public MaterialCustodyRow? ActiveCustody => Custodies.FirstOrDefault(c => c.Status == MaterialCustodyStatus.HandedOver);
}

/// <summary>Post the acquisition of an asset recorded before the ledger integration.</summary>
public class FixedAssetPostModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    [Display(Name = "طريقة الاقتناء")] public AssetAcquisitionMethod AcquisitionMethod { get; set; } = AssetAcquisitionMethod.OpeningBalance;
    [Display(Name = "تُدفع من خزنة")] public Guid? SafeId { get; set; }
    [Range(0, 999_999_999_999, ErrorMessage = "قيمة غير صالحة")][Display(Name = "إهلاك سابق متراكم (ج.م)")]
    public decimal OpeningAccumulatedDepreciation { get; set; }
    public List<SelectListItem> Safes { get; set; } = new();
}

public class FixedAssetDisposeModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Cost { get; set; }
    public decimal Accumulated { get; set; }
    public decimal BookValue => Cost - Accumulated;

    [DataType(DataType.Date)][Display(Name = "تاريخ الاستبعاد")] public DateTime Date { get; set; } = ClientTime.Today;
    [Range(0, 999_999_999_999, ErrorMessage = "قيمة غير صالحة")][Display(Name = "متحصلات البيع (ج.م) — 0 للاستبعاد / الشطب")]
    public decimal Proceeds { get; set; }
    [Display(Name = "تُورَّد المتحصلات إلى خزنة")] public Guid? SafeId { get; set; }
    [Required(ErrorMessage = "سبب الاستبعاد مطلوب")][Display(Name = "السبب")] public string Reason { get; set; } = string.Empty;
    public List<SelectListItem> Safes { get; set; } = new();
}

// ======================================================================================
//  إهلاك الأصول الثابتة — monthly runs
// ======================================================================================

public class DepreciationPreviewRow
{
    public Guid AssetId { get; set; }
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Category { get; set; }
    /// <summary>Months not yet posted up to the selected month, and their total.</summary>
    public int Months { get; set; }
    public decimal Amount { get; set; }
    public decimal BookValueAfter { get; set; }
}

public class DepreciationRunRow
{
    public DateTime Period { get; set; }
    public int Assets { get; set; }
    public decimal Amount { get; set; }
}

public class DepreciationVm
{
    /// <summary>Selected month (first day).</summary>
    public DateTime Period { get; set; }
    public List<DepreciationPreviewRow> Due { get; set; } = new();
    public decimal DueTotal => Due.Sum(d => d.Amount);
    public List<DepreciationRunRow> History { get; set; } = new();
    public DateTime? LastPostedPeriod { get; set; }
    /// <summary>Assets that can't be depreciated yet: not posted, or missing a useful life / start date.</summary>
    public int SkippedUnposted { get; set; }
}
