using RealState.Application.Common;
using RealState.Application.Enums;

namespace RealState.Application.Entities;

/// <summary>An organizational department (قسم).</summary>
public class Department : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

/// <summary>A job role / title (محاسب، مهندس، ...).</summary>
public class JobRole : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    /// <summary>Employees in this role are salespersons (the only employees assignable to customers).</summary>
    public bool IsSalesperson { get; set; }
}

/// <summary>Per-tenant attendance settings (single row). Daily hours = TimeOut − TimeIn.</summary>
public class AttendanceSetting : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public TimeSpan TimeIn { get; set; } = new(9, 0, 0);
    public TimeSpan TimeOut { get; set; } = new(17, 0, 0);

    /// <summary>Whether overtime is counted.</summary>
    public bool OvertimeEnabled { get; set; }
    /// <summary>Normal working hours that equal one overtime hour.</summary>
    public decimal OvertimeNormalHours { get; set; }

    public decimal DailyHours => (decimal)(TimeOut - TimeIn).TotalHours;
}

/// <summary>A late-arrival deduction rule for one bracket. Seeded once per tenant (4 brackets).</summary>
public class LateDeductionRule : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public LateBracket Bracket { get; set; }
    public DeductionFraction Fraction { get; set; } = DeductionFraction.None;
    public bool IsActive { get; set; }
}

/// <summary>
/// A company employee. Salespersons (Type = Salesperson) are created from the Customers section;
/// the same entity is the HR registry (personal + work data, attachments, vacations, advances, rewards).
/// </summary>
public class Employee : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }

    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public EmployeeType Type { get; set; } = EmployeeType.Salesperson;
    public bool IsActive { get; set; } = true;
    public DateTime? HireDate { get; set; } // start date

    /// <summary>The login account created for this employee (default dummy password, no permissions until an admin grants them).</summary>
    public Guid? UserId { get; set; }

    // --- Personal ---
    public DateTime? BirthDate { get; set; }
    public string? Nationality { get; set; }
    public string? NationalId { get; set; }
    public string? AcademicQualification { get; set; }
    public string? CurrentLocation { get; set; }
    public string? AltPhone { get; set; }

    // --- Work ---
    public Guid? DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? JobRoleId { get; set; }
    public JobRole? JobRole { get; set; }
    public EmploymentType EmploymentType { get; set; } = EmploymentType.Permanent;
    public bool SocialInsurance { get; set; }
    public bool MedicalInsurance { get; set; }
    public decimal BasicSalary { get; set; }
    public decimal IncentivesCommissions { get; set; }

    public ICollection<EmployeeAttachment> Attachments { get; set; } = new List<EmployeeAttachment>();
}

/// <summary>A document attached to an employee (stored in DB).</summary>
public class EmployeeAttachment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public EmployeeAttachmentKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>An employee vacation request.</summary>
public class Vacation : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public DateTime ApplyDate { get; set; } = DateTime.Today;
    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public VacationType Type { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }

    public int Days => (ToDate.Date - FromDate.Date).Days + 1;
}

/// <summary>An employee salary advance (سلفة).</summary>
public class Advance : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }              // serial, shown as ADV-####
    public DateTime Date { get; set; } = DateTime.Today;

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public decimal Amount { get; set; }
    public AdvanceRepaymentMethod RepaymentMethod { get; set; }
    public int InstallmentsCount { get; set; }   // for FromSalary
    public DateTime? MonthlyStartDate { get; set; } // for FromSalary
    public string? Notes { get; set; }

    public DisbursementStatus Status { get; set; } = DisbursementStatus.NotDisbursed;
    public Guid? ExpenseTxnId { get; set; }       // the disbursement expense transaction

    public ICollection<AdvanceRepayment> Repayments { get; set; } = new List<AdvanceRepayment>();
}

/// <summary>One repayment installment / cash payment against an advance.</summary>
public class AdvanceRepayment : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public Guid AdvanceId { get; set; }
    public Advance? Advance { get; set; }

    public int SeqNo { get; set; }
    public decimal Amount { get; set; }
    public DateTime? DueDate { get; set; }
    public PayStatus Status { get; set; } = PayStatus.NotPaid;
    public DateTime? PaidDate { get; set; }
    public Guid? IncomeTxnId { get; set; }        // for cash repayment (income)
}

/// <summary>An employee reward / bonus (مكافأة). Paid once via salary or cash.</summary>
public class Reward : AuditableEntity, ITenantEntity
{
    public Guid TenantId { get; set; }
    public int Number { get; set; }              // serial, shown as RWD-####
    public DateTime Date { get; set; } = DateTime.Today;

    public Guid EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public decimal Amount { get; set; }
    public RewardPayVia PayVia { get; set; }
    public string? Notes { get; set; }

    public PayStatus Status { get; set; } = PayStatus.NotPaid;
    public Guid? ExpenseTxnId { get; set; }       // the payout expense transaction (cash)
}
