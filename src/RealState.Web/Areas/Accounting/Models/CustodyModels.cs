using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.Accounting.Models;

// ======================================================================================
//  العُهد العينية — physical company items held by employees (register only, no ledger)
// ======================================================================================

public class MaterialCustodyFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اختر الأصل الثابت")][Display(Name = "الأصل الثابت")]
    public Guid? FixedAssetId { get; set; }

    [Required(ErrorMessage = "الموظف المستلم مطلوب")][Display(Name = "الموظف المستلم")]
    public Guid? EmployeeId { get; set; }

    [DataType(DataType.Date)][Display(Name = "تاريخ التسليم")] public DateTime HandoverDate { get; set; } = ClientTime.Today;
    [DataType(DataType.Date)][Display(Name = "تاريخ الإرجاع المتوقع")] public DateTime? ExpectedReturnDate { get; set; }
    [Display(Name = "الحالة عند التسليم")] public string? HandoverCondition { get; set; }
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }

    public List<SelectListItem> Employees { get; set; } = new();
    /// <summary>In-service assets with no active custodian (plus this custody's own asset when editing).</summary>
    public List<FixedAssetOption> Assets { get; set; } = new();
    /// <summary>Editing an existing custody: only the dates / conditions / notes may change — the asset and the employee
    /// are fixed (cancel and re-issue, or transfer, instead).</summary>
    public bool IsLocked { get; set; }
    public string? AssetLabel { get; set; }
    public string? EmployeeName { get; set; }
}

public record FixedAssetOption(Guid Id, string Label, string? Category, string? SerialNumber, int Quantity, decimal UnitCost);

/// <summary>Hand the asset of an active custody to another employee: closes this custody (حُوِّلت) and opens a new one.</summary>
public class MaterialCustodyTransferModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? FromEmployee { get; set; }
    [Required(ErrorMessage = "اختر الموظف المستلم")][Display(Name = "الموظف المستلم الجديد")] public Guid? ToEmployeeId { get; set; }
    [DataType(DataType.Date)][Display(Name = "تاريخ التحويل")] public DateTime Date { get; set; } = ClientTime.Today;
    [DataType(DataType.Date)][Display(Name = "تاريخ الإرجاع المتوقع")] public DateTime? ExpectedReturnDate { get; set; }
    [Display(Name = "حالة الأصل عند التحويل")] public string? Condition { get; set; }
    public List<SelectListItem> Employees { get; set; } = new();
}

/// <summary>Cancel a custody recorded by mistake — it stays in the history with the reason.</summary>
public class MaterialCustodyCancelModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string ItemName { get; set; } = string.Empty;
    [Required(ErrorMessage = "سبب الإلغاء مطلوب")][Display(Name = "سبب الإلغاء")] public string Reason { get; set; } = string.Empty;
}

public class MaterialCustodyReturnModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public string ItemName { get; set; } = string.Empty;
    [DataType(DataType.Date)][Display(Name = "تاريخ الاستلام")] public DateTime ReturnDate { get; set; } = ClientTime.Today;
    [Display(Name = "حالة العهدة عند الاستلام")] public MaterialCustodyStatus Status { get; set; } = MaterialCustodyStatus.Returned;
    [Display(Name = "ملاحظات الاستلام (الحالة الفعلية)")] public string? ReturnCondition { get; set; }
}

public class MaterialCustodyRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public Guid? FixedAssetId { get; set; }
    public int? AssetNumber { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? ItemType { get; set; }
    public string? SerialNumber { get; set; }
    public int Quantity { get; set; }
    public decimal? Value { get; set; }
    public Guid EmployeeId { get; set; }
    public string Employee { get; set; } = "—";
    public DateTime HandoverDate { get; set; }
    public DateTime? ExpectedReturnDate { get; set; }
    public DateTime? ReturnDate { get; set; }
    public MaterialCustodyStatus Status { get; set; }
    public int AttachmentsCount { get; set; }
    /// <summary>Still with the employee after its expected return date.</summary>
    public bool IsOverdue => Status == MaterialCustodyStatus.HandedOver && ExpectedReturnDate is DateTime d && d.Date < ClientTime.Today;
}

public class MaterialCustodyListVm
{
    public List<MaterialCustodyRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? EmployeeId { get; set; }
    public MaterialCustodyStatus? Status { get; set; }
    public string? ItemType { get; set; }
    public string? Q { get; set; }
    public List<SelectListItem> Employees { get; set; } = new();
    public List<SelectListItem> ItemTypes { get; set; } = new();

    // Register-wide counters (not filtered).
    public int HeldCount { get; set; }
    public decimal HeldValue { get; set; }
    public int OverdueCount { get; set; }
    public int ReturnedCount { get; set; }
    public int IssueCount { get; set; }
}

public record CustodyLink(Guid Id, int Number, string? Employee);

public class MaterialCustodyDetailsVm
{
    public MaterialCustody Custody { get; set; } = default!;
    public FixedAsset? Asset { get; set; }
    /// <summary>The custody this one continues (asset transferred from another employee).</summary>
    public CustodyLink? TransferredFrom { get; set; }
    /// <summary>The custody that continued this one (when it was transferred).</summary>
    public CustodyLink? TransferredTo { get; set; }
    public Employee? Employee { get; set; }
    public string? Department { get; set; }
    /// <summary>Attachments without their bytes.</summary>
    public List<MaterialCustodyAttachment> Attachments { get; set; } = new();
    public bool IsOverdue => Custody.Status == MaterialCustodyStatus.HandedOver
                             && Custody.ExpectedReturnDate is DateTime d && d.Date < ClientTime.Today;
}

// ======================================================================================
//  العُهد المالية — cash custodies / imprest funds, settled with the accountant
// ======================================================================================

public class FinancialCustodyCreateModel
{
    [DataType(DataType.Date)][Display(Name = "التاريخ")] public DateTime Date { get; set; } = ClientTime.Today;

    [Required(ErrorMessage = "الموظف (صاحب العهدة) مطلوب")][Display(Name = "صاحب العهدة")]
    public Guid? EmployeeId { get; set; }

    [Required(ErrorMessage = "اسم العهدة مطلوب")][Display(Name = "اسم العهدة")]
    public string Purpose { get; set; } = string.Empty;

    [Range(0.01, 999_999_999_999, ErrorMessage = "أدخل مبلغًا أكبر من صفر")][Display(Name = "المبلغ المصروف (ج.م)")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "اختر الخزنة")][Display(Name = "تُصرف من خزنة")]
    public Guid? SafeId { get; set; }

    [Display(Name = "ملاحظات")] public string? Notes { get; set; }

    public List<SelectListItem> Employees { get; set; } = new();
    public List<SelectListItem> Safes { get; set; } = new();
}

public class FinancialCustodyEditModel
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    [Required(ErrorMessage = "اسم العهدة مطلوب")][Display(Name = "اسم العهدة")]
    public string Purpose { get; set; } = string.Empty;
    [Display(Name = "ملاحظات")] public string? Notes { get; set; }
}

/// <summary>Cash through a safe: an extra disbursement to the custodian, or unused cash returned by them.</summary>
public class CustodyCashModel
{
    public Guid CustodyId { get; set; }
    public int Number { get; set; }
    public CustodyMovementKind Kind { get; set; }
    /// <summary>Custodian's balance before this movement (shown in the form).</summary>
    public decimal Balance { get; set; }

    [DataType(DataType.Date)][Display(Name = "التاريخ")] public DateTime Date { get; set; } = ClientTime.Today;
    [Range(0.01, 999_999_999_999, ErrorMessage = "أدخل مبلغًا أكبر من صفر")][Display(Name = "المبلغ (ج.م)")]
    public decimal Amount { get; set; }
    [Required(ErrorMessage = "اختر الخزنة")][Display(Name = "الخزنة")] public Guid? SafeId { get; set; }
    [Display(Name = "البيان")] public string? Description { get; set; }

    public List<SelectListItem> Safes { get; set; } = new();
}

/// <summary>Settlement with the accountant: the expenses the custodian spent from the custody (one line per receipt).</summary>
public class CustodySettleModel
{
    public Guid CustodyId { get; set; }
    public int Number { get; set; }
    public decimal Balance { get; set; }
    public List<CustodyExpenseLine> Lines { get; set; } = new();

    public List<SelectListItem> Categories { get; set; } = new();
    public List<SelectListItem> Projects { get; set; } = new();
}

public class CustodyExpenseLine
{
    public DateTime? Date { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? Description { get; set; }
    public decimal? Amount { get; set; }
    public IFormFile? Receipt { get; set; }
}

public class FinancialCustodyRow
{
    public Guid Id { get; set; }
    public int Number { get; set; }
    public DateTime Date { get; set; }
    public string Employee { get; set; } = "—";
    public string Purpose { get; set; } = string.Empty;
    public decimal Disbursed { get; set; }
    public decimal Spent { get; set; }
    public decimal Returned { get; set; }
    public decimal Balance => Disbursed - Spent - Returned;
    public FinancialCustodyStatus Status { get; set; }
}

public class FinancialCustodyListVm
{
    public List<FinancialCustodyRow> Rows { get; set; } = new();
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public Guid? EmployeeId { get; set; }
    public FinancialCustodyStatus? Status { get; set; }
    public string? Q { get; set; }
    public List<SelectListItem> Employees { get; set; } = new();

    // Register-wide counters (not filtered).
    public int OpenCount { get; set; }
    /// <summary>Cash currently held by custodians (Σ positive balances of open custodies).</summary>
    public decimal HeldByEmployees { get; set; }
    /// <summary>Owed back to custodians who spent more than they received (Σ negative balances, as a positive number).</summary>
    public decimal OwedToEmployees { get; set; }
}

public class FinancialCustodyMovementRow
{
    public Guid Id { get; set; }
    public CustodyMovementKind Kind { get; set; }
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? Safe { get; set; }
    public string? Category { get; set; }
    public string? Project { get; set; }
    public Guid? VoucherId { get; set; }
    public long? VoucherSerial { get; set; }
    public string? ReceiptFileName { get; set; }
    /// <summary>Custodian's balance after this movement.</summary>
    public decimal Balance { get; set; }
    /// <summary>Raises what the custodian holds (+): disbursements only.</summary>
    public bool Increases => Kind == CustodyMovementKind.Disbursement;
}

public class FinancialCustodyDetailsVm
{
    public FinancialCustody Custody { get; set; } = default!;
    public Employee? Employee { get; set; }
    public string? Department { get; set; }
    public List<FinancialCustodyMovementRow> Movements { get; set; } = new();
    public decimal Disbursed { get; set; }
    public decimal Spent { get; set; }
    public decimal Returned { get; set; }
    public decimal Balance => Disbursed - Spent - Returned;
    public bool IsOpen => Custody.Status == FinancialCustodyStatus.Open;
}

