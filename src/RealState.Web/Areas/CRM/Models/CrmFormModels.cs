using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using RealState.Application.Entities;
using RealState.Application.Enums;

namespace RealState.Web.Areas.CRM.Models;

/// <summary>One contract's slice of a customer's account statement.</summary>
public class ContractStatement
{
    public string Code { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string UnitLabel { get; set; } = string.Empty;
    public DateTime ReceiveDate { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal DownPayment { get; set; }
    public List<Installment> Installments { get; set; } = new();

    public decimal InstallmentsPaid => Installments.Sum(i => i.PaidAmount);
    // Down payment is scheduled as installment #0, so it's part of InstallmentsPaid once collected.
    public decimal Paid => InstallmentsPaid;
    public decimal Remaining => TotalPrice - Paid;
}

public class CustomerStatementVm
{
    public Customer Customer { get; set; } = default!;
    public List<ContractStatement> Contracts { get; set; } = new();

    public decimal TotalObligations => Contracts.Sum(c => c.TotalPrice);
    public decimal TotalPaid => Contracts.Sum(c => c.Paid);
    public decimal TotalRemaining => Contracts.Sum(c => c.Remaining);
    public bool HasContracts => Contracts.Count > 0;
}

public class SalespersonFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "الاسم مطلوب")]
    [Display(Name = "الاسم")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "بريد إلكتروني غير صالح")]
    [Display(Name = "البريد الإلكتروني (اختياري)")]
    public string? Email { get; set; }
}

public class CustomerFormModel
{
    public Guid Id { get; set; }

    [Required(ErrorMessage = "اسم العميل مطلوب")]
    [Display(Name = "اسم العميل")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "رقم الهاتف مطلوب")]
    [Phone(ErrorMessage = "رقم هاتف غير صالح")]
    [Display(Name = "رقم الهاتف")]
    public string Phone { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "بريد إلكتروني غير صالح")]
    [Display(Name = "البريد الإلكتروني (اختياري)")]
    public string? Email { get; set; }

    [Display(Name = "المصدر")]
    public CustomerSource Source { get; set; }

    [Required(ErrorMessage = "يجب اختيار المندوب")]
    [Display(Name = "المندوب")]
    public Guid? SalesPersonId { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    public List<SelectListItem> SalesPersons { get; set; } = new();
}
