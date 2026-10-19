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
    /// <summary>Sum of the schedule's down-payment (<see cref="InstallmentType.DownPayment"/>) rows.</summary>
    public decimal DownPayment { get; set; }
    /// <summary>Number of <see cref="InstallmentType.Installment"/> rows in the schedule (manual + auto).</summary>
    public int InstallmentsCount { get; set; }
    /// <summary>Period of the auto-generated rows.</summary>
    public InstallmentStep Step { get; set; }

    /// <summary>
    /// True when every schedule row was entered by hand (no rows auto-generated from
    /// first-date + count + <see cref="Step"/>).
    /// </summary>
    public bool IsManualSchedule { get; set; }
    public string? Notes { get; set; }

    // ---- فسخ التعاقد (cancellation) ----
    // A cancelled contract stays on file: its unit is released, its sale is reversed in the ledger, and what the
    // customer had paid — less the cancellation fee — becomes owed back to them (the customer turns دائن), refunded
    // part by part through ContractCancelRefund vouchers (SafeTransaction.SaleContractId).
    public bool IsCancelled { get; set; }
    /// <summary>When the contract was cancelled — entered by the user (date + time).</summary>
    public DateTime? CancelledAt { get; set; }
    /// <summary>Total the customer had paid on the contract when it was cancelled.</summary>
    public decimal CancelPaidAmount { get; set; }
    /// <summary>Cancellation fee as a percentage of <see cref="CancelPaidAmount"/> (0–100).</summary>
    public decimal CancelFeePercent { get; set; }
    /// <summary>The fee kept from the refund: <see cref="CancelPaidAmount"/> × <see cref="CancelFeePercent"/> %.</summary>
    public decimal CancelFeeAmount { get; set; }
    public string? CancelReason { get; set; }
    public string? CancelledByName { get; set; }

    /// <summary>What is owed back to the customer after the fee.</summary>
    [NotMapped]
    public decimal CancelRefundDue => IsCancelled ? CancelPaidAmount - CancelFeeAmount : 0m;

    public ICollection<Installment> Installments { get; set; } = new List<Installment>();
}

/// <summary>One scheduled payment (installment / down payment / maintenance) of a sale contract.</summary>
public class Installment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public Guid SaleContractId { get; set; }
    public SaleContract? SaleContract { get; set; }

    /// <summary>Sequence within its <see cref="Type"/> (قسط 1..n, مقدم 1..m …). Legacy down payments are 0.</summary>
    public int Number { get; set; }
    public InstallmentType Type { get; set; }
    /// <summary>Generated from the contract's "auto for the remaining" settings rather than entered by hand.</summary>
    public bool IsAuto { get; set; }
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
