using System.ComponentModel.DataAnnotations.Schema;
using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>A sale/contract linking a customer to a unit of a project, with an installment plan.</summary>
public class SaleContract : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string Code { get; set; } = string.Empty; // e.g. "S-0001"

    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }

    public Guid UnitId { get; set; }
    public ProjectUnit? Unit { get; set; }

    public DateTime ContractDate { get; set; }
    public DateTime ReceiveDate { get; set; }
    public decimal TotalPrice { get; set; }
    public decimal DownPayment { get; set; }
    public int InstallmentsCount { get; set; }
    public InstallmentStep Step { get; set; }
    public string? Notes { get; set; }

    public ICollection<Installment> Installments { get; set; } = new List<Installment>();
}

/// <summary>One scheduled installment of a sale contract.</summary>
public class Installment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid SaleContractId { get; set; }
    public SaleContract? SaleContract { get; set; }

    public int Number { get; set; }
    public DateTime DueDate { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? PaidDate { get; set; }

    /// <summary>Sequential receipt number, assigned when the installment is collected.</summary>
    public int? ReceiptNo { get; set; }

    [NotMapped]
    public bool IsPaid => PaidAmount >= Amount && Amount > 0;

    [NotMapped]
    public InstallmentStatus Status =>
        IsPaid ? InstallmentStatus.Paid
        : DueDate.Date < DateTime.Today ? InstallmentStatus.Overdue
        : InstallmentStatus.Pending;
}
