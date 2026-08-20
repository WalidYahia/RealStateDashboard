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

    /// <summary>Communication/action log (newest first).</summary>
    public List<CustomerLog> Logs { get; set; } = new();
    /// <summary>True when the current user may add/edit/delete communication logs — ONLY the assigned salesperson.</summary>
    public bool CanLog { get; set; }
    /// <summary>True when the current user may change the lead status (assigned salesperson or Leads.Control).</summary>
    public bool CanControl { get; set; }
    /// <summary>True when the current user may convert this lead to a customer (assigned salesperson or Leads.Convert).</summary>
    public bool CanConvert { get; set; }

    public decimal TotalObligations => Contracts.Sum(c => c.TotalPrice);
    public decimal TotalPaid => Contracts.Sum(c => c.Paid);
    public decimal TotalRemaining => Contracts.Sum(c => c.Remaining);
    public bool HasContracts => Contracts.Count > 0;
}

/// <summary>Add/edit a customer communication-log entry.</summary>
public class CustomerLogFormModel
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    [DataType(DataType.Date)][Display(Name = "التاريخ")] public DateTime Date { get; set; } = DateTime.Today;
    [Required(ErrorMessage = "الوصف مطلوب")][Display(Name = "الوصف")] public string Description { get; set; } = string.Empty;
}

/// <summary>Update a lead's interest status (logs the change).</summary>
public class LeadStatusFormModel
{
    public Guid CustomerId { get; set; }
    [Display(Name = "الحالة")] public LeadInterest? Interest { get; set; }
}

/// <summary>One row in the leads list.</summary>
public class LeadRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public DateTime CreatedOn { get; set; }
    public Guid? SalespersonId { get; set; }
    public string SourceLabel { get; set; } = "—";
    public string Salesperson { get; set; } = "—";
    public LeadInterest? Interest { get; set; }
    public int LogCount { get; set; }
}

/// <summary>Leads list with its filters and filter option lists.</summary>
public class LeadListVm
{
    public List<LeadRow> Rows { get; set; } = new();
    public Guid? SalespersonId { get; set; }
    public string? Source { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public List<SelectListItem> Salespersons { get; set; } = new();
    public List<SelectListItem> Sources { get; set; } = new();
}

public record CountRow(string Label, int Count);

/// <summary>Analytics for the CRM summary page.</summary>
public class CrmSummaryVm
{
    public int TotalLeads { get; set; }
    public int NewLeadsThisMonth { get; set; }
    public int TotalCustomers { get; set; }
    public int NewCustomersThisMonth { get; set; }
    public int Salespersons { get; set; }
    public List<CountRow> BySource { get; set; } = new();
    public List<CountRow> BySalesperson { get; set; } = new();
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

    /// <summary>Creation date, editable when creating a lead (defaults to today).</summary>
    [DataType(DataType.Date)][Display(Name = "تاريخ الإنشاء")]
    public DateTime CreatedOn { get; set; } = DateTime.Today;

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

    [Display(Name = "القناة")]
    public LeadChannel Channel { get; set; } = LeadChannel.SocialMedia;

    [Display(Name = "المصدر")]
    public CustomerSource Source { get; set; }

    /// <summary>Selected campaign when Channel = Campaign.</summary>
    [Display(Name = "المصدر")]
    public Guid? SourceCampaignId { get; set; }

    // Optional in general; required only for a lead whose channel is "مندوب مبيعات" (enforced in the controller).
    [Display(Name = "المندوب")]
    public Guid? SalesPersonId { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Notes { get; set; }

    /// <summary>When true this record is created/edited as a lead (potential customer).</summary>
    public bool IsLead { get; set; }
    /// <summary>Optional lead interest, shown only when creating a lead.</summary>
    [Display(Name = "الحالة")]
    public LeadInterest? Interest { get; set; }

    public List<SelectListItem> SalesPersons { get; set; } = new();
    public List<SelectListItem> Campaigns { get; set; } = new();
}
