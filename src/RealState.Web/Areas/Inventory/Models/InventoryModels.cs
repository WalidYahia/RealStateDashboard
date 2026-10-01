using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Inventory.Models;

/// <summary>Generic tabular payload for the shared PDF/print view (headers + string rows + optional totals).</summary>
public class ListPrintVm
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public IReadOnlyList<string> Headers { get; set; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = Array.Empty<IReadOnlyList<string>>();
    public IReadOnlyList<string>? Totals { get; set; }
}

// ---------------- Master data forms ----------------
public class CategoryFormModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "الاسم مطلوب")][Display(Name = "اسم التصنيف")]
    public string Name { get; set; } = string.Empty;
    [Display(Name = "الترتيب")] public int SortOrder { get; set; }
    [Display(Name = "مفعّل")] public bool IsActive { get; set; } = true;
}

public class UnitFormModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "الكود مطلوب")][Display(Name = "الكود")]
    public string Code { get; set; } = string.Empty;
    [Required(ErrorMessage = "الاسم مطلوب")][Display(Name = "اسم الوحدة")]
    public string Name { get; set; } = string.Empty;
    [Display(Name = "مفعّل")] public bool IsActive { get; set; } = true;
}

public class WarehouseFormModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "الكود مطلوب")][Display(Name = "الكود")]
    public string Code { get; set; } = string.Empty;
    [Required(ErrorMessage = "الاسم مطلوب")][Display(Name = "اسم المخزن")]
    public string Name { get; set; } = string.Empty;
    [Display(Name = "المخزن الافتراضي")] public bool IsDefault { get; set; }
    [Display(Name = "مفعّل")] public bool IsActive { get; set; } = true;
}

public class ProductFormModel
{
    public Guid Id { get; set; }
    [Required(ErrorMessage = "الكود مطلوب")][Display(Name = "كود الصنف (SKU)")]
    public string Sku { get; set; } = string.Empty;
    [Required(ErrorMessage = "الاسم مطلوب")][Display(Name = "اسم الصنف")]
    public string Name { get; set; } = string.Empty;
    [Display(Name = "التصنيف")] public Guid? CategoryId { get; set; }
    // Up to 3 units: the smallest (stock + cost unit), a bigger one = Unit2Factor × smallest, and the biggest =
    // Unit3Factor × the bigger one (e.g. سم ← متر = 100 سم ← كيلو = 1000 متر).
    [Display(Name = "الوحدة الصغرى")] public Guid? UnitOfMeasureId { get; set; }
    [Display(Name = "الوحدة الأكبر")] public Guid? Unit2Id { get; set; }
    [Display(Name = "عدد الوحدات الصغرى فيها")] public decimal? Unit2Factor { get; set; }
    [Display(Name = "الوحدة الأكبر منها")] public Guid? Unit3Id { get; set; }
    [Display(Name = "عدد «الوحدة الأكبر» فيها")] public decimal? Unit3Factor { get; set; }
    /// <summary>Unit (1/2/3) pre-selected on invoices, orders and inventory documents.</summary>
    [Display(Name = "الوحدة الافتراضية")] public byte DefaultUnitLevel { get; set; } = 1;
    // Selling price per unit level (pre-filled on sales invoice lines, editable there). Empty = none.
    [Range(0, 999999999999, ErrorMessage = "سعر غير صالح")][Display(Name = "سعر البيع (الصغرى)")] public decimal? SalePrice { get; set; }
    [Range(0, 999999999999, ErrorMessage = "سعر غير صالح")][Display(Name = "سعر البيع (الأكبر)")] public decimal? SalePrice2 { get; set; }
    [Range(0, 999999999999, ErrorMessage = "سعر غير صالح")][Display(Name = "سعر البيع (الأكبر منها)")] public decimal? SalePrice3 { get; set; }
    [Display(Name = "حد إعادة الطلب (بالوحدة الصغرى)")] public decimal ReorderLevel { get; set; }
    [Display(Name = "تتبّع المخزون")] public bool TrackInventory { get; set; } = true;
    [Display(Name = "تتبّع التكلفة")] public bool TrackCost { get; set; } = true;
    [Display(Name = "مفعّل")] public bool IsActive { get; set; } = true;
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }

    public List<SelectListItem> Categories { get; set; } = new();
    public List<SelectListItem> Units { get; set; } = new();
}

public class ProductRow
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CategoryName { get; set; }
    public string? UnitName { get; set; }
    /// <summary>The product's units, e.g. «سم ← متر (100) ← كيلو (1000)».</summary>
    public string? UnitsChain { get; set; }
    public string? DefaultUnitName { get; set; }
    public bool IsActive { get; set; }
}

// ---------------- Posting profile (settings) ----------------
public class InventorySettingsModel
{
    [Display(Name = "طريقة التكلفة")] public CostingMethod CostingMethod { get; set; } = CostingMethod.WeightedAverage;
    /// <summary>Structural — the control account warehouse sub-accounts hang under. Shown read-only, never taken from the form.</summary>
    [Display(Name = "حساب المخزون")] public string InventoryCode { get; set; } = string.Empty;
    public string InventoryAccountLabel { get; set; } = string.Empty;
    [Display(Name = "حساب تكلفة المبيعات")] public string CogsCode { get; set; } = string.Empty;
    [Display(Name = "حساب المشتريات / بضاعة بالطريق")] public string PurchaseGrniCode { get; set; } = string.Empty;
    [Display(Name = "حساب أرباح التسويات")] public string AdjustmentGainCode { get; set; } = string.Empty;
    [Display(Name = "حساب خسائر التسويات")] public string AdjustmentLossCode { get; set; } = string.Empty;
    [Display(Name = "حساب إيرادات المبيعات")] public string SalesRevenueCode { get; set; } = string.Empty;
    [Display(Name = "حساب مصروف الاستهلاك")] public string ConsumptionExpenseCode { get; set; } = string.Empty;

    public List<SelectListItem> Accounts { get; set; } = new();

    // Tabs on the settings page: "accounts" | "categories" | "units"
    public string ActiveTab { get; set; } = "accounts";
    public List<ProductCategory> Categories { get; set; } = new();
    public List<UnitOfMeasure> Units { get; set; } = new();
}

// Rows posted by the inline editors on the settings tabs (values arrive as strings and are parsed).
public class CategoryRowInput
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? SortOrder { get; set; }
    public bool IsActive { get; set; }
}

public class UnitRowInput
{
    public string? Id { get; set; }
    public string? Code { get; set; }
    public string? Name { get; set; }
    public bool IsActive { get; set; }
}
