using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Inventory.Models;

// Line inputs parsed from the form's JSON payload (robust against indexed model binding).
public class ReceiptLineInput { public Guid ProductId { get; set; } public decimal Quantity { get; set; } public decimal UnitCost { get; set; } }
public class IssueLineInput { public Guid ProductId { get; set; } public decimal Quantity { get; set; } }
public class TransferLineInput { public Guid ProductId { get; set; } public decimal Quantity { get; set; } }
public class AdjustmentLineInput { public Guid ProductId { get; set; } public decimal QuantityDelta { get; set; } public decimal UnitCost { get; set; } }
public class CountLineInput { public Guid ProductId { get; set; } public decimal CountedQty { get; set; } }

/// <summary>A prefilled line for the editor (edit mode) — product label + values.</summary>
public class DocLineVm
{
    public Guid ProductId { get; set; }
    public string ProductLabel { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal QuantityDelta { get; set; }
    public decimal SystemQty { get; set; }
    public decimal CountedQty { get; set; }
}

public abstract class DocFormBase
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public InventoryDocStatus Status { get; set; } = InventoryDocStatus.Draft;
    [Display(Name = "التاريخ")] public DateTime Date { get; set; } = DateTime.Today;
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }
    public List<DocLineVm> ExistingLines { get; set; } = new();
    public List<SelectListItem> Warehouses { get; set; } = new();
    public List<SelectListItem> Products { get; set; } = new();
}

public class ReceiptFormModel : DocFormBase
{
    [Display(Name = "المخزن")] public Guid WarehouseId { get; set; }
    [Display(Name = "المورد (اختياري)")] public Guid? SupplierId { get; set; }
    public List<SelectListItem> Suppliers { get; set; } = new();
}

public class IssueFormModel : DocFormBase
{
    [Display(Name = "المخزن")] public Guid WarehouseId { get; set; }
    [Display(Name = "سبب الصرف")] public IssueReason Reason { get; set; } = IssueReason.Sale;
}

public class TransferFormModel : DocFormBase
{
    [Display(Name = "من مخزن")] public Guid FromWarehouseId { get; set; }
    [Display(Name = "إلى مخزن")] public Guid ToWarehouseId { get; set; }
}

public class AdjustmentFormModel : DocFormBase
{
    [Display(Name = "المخزن")] public Guid WarehouseId { get; set; }
    [Display(Name = "السبب")] public AdjustmentReason Reason { get; set; } = AdjustmentReason.Correction;
}

public class CountFormModel : DocFormBase
{
    [Display(Name = "المخزن")] public Guid WarehouseId { get; set; }
}

/// <summary>A document list page: its rows plus the date range they were filtered by.</summary>
public class DocListVm
{
    public List<DocListRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
}

/// <summary>A document row for the list pages.</summary>
public class DocListRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public InventoryDocStatus Status { get; set; }
    public int LineCount { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Document details view (header + lines + resulting movements).</summary>
public class DocDetailsVm
{
    public string Title { get; set; } = string.Empty;
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string Warehouse { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public List<DocDetailLine> Lines { get; set; } = new();
}

public class DocDetailLine
{
    public string Product { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal TotalCost { get; set; }
}
