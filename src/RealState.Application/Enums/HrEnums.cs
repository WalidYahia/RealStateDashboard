namespace RealState.Application.Enums;

/// <summary>Employee job type. Salespersons are entered here and reused by the HR module later.</summary>
public enum EmployeeType
{
    Salesperson = 0,
    SalesManager = 1,
    Accountant = 2,
    HrOfficer = 3,
    Other = 4,
}

/// <summary>Employment/contract type of an employee.</summary>
public enum EmploymentType
{
    Permanent = 0,   // دائم
    Temporary = 1,   // مؤقت
    Training = 2,     // تدريب
}

/// <summary>Kind of an employee document/attachment.</summary>
public enum EmployeeAttachmentKind
{
    NationalId = 0,       // بطاقة الرقم القومي
    PersonalImage = 1,    // صورة شخصية
    CriminalRecord = 2,   // فيش وتشبيه
    Qualification = 3,    // مؤهل دراسي
    Cv = 4,               // السيرة الذاتية
    WorkExperience = 5,   // خبرات عمل
    Other = 6,            // أخرى
}

/// <summary>Vacation category.</summary>
public enum VacationType
{
    Sick = 0,        // مرضية
    Normal = 1,      // اعتيادية
    Emergency = 2,   // عارضة/طارئة
}

/// <summary>Late-arrival brackets used by the deduction rules.</summary>
public enum LateBracket
{
    Upto15 = 0,   // حتى 15 دقيقة
    Upto30 = 1,   // حتى 30 دقيقة
    Upto60 = 2,   // حتى ساعة
    Over60 = 3,   // أكثر من ساعة
}

/// <summary>Deduction size expressed as a fraction of a day's pay (None = no deduction).</summary>
public enum DeductionFraction
{
    None = 0,      // بدون
    Quarter = 1,   // ربع يوم
    Half = 2,      // نصف يوم
    Full = 3,      // يوم كامل
}

/// <summary>How an advance is repaid.</summary>
public enum AdvanceRepaymentMethod
{
    FromSalary = 0,   // خصم من الراتب
    Cash = 1,         // نقدًا
}

/// <summary>Whether the money has left the safe yet.</summary>
public enum DisbursementStatus
{
    NotDisbursed = 0,  // لم يُصرف
    Disbursed = 1,     // تم الصرف
}

/// <summary>Paid / not-paid status for repayments and reward payouts.</summary>
public enum PayStatus
{
    NotPaid = 0,   // لم يُسدَّد
    Paid = 1,      // تم السداد
}

/// <summary>How a reward is paid to the employee.</summary>
public enum RewardPayVia
{
    Salary = 0,   // مع الراتب
    Cash = 1,     // نقدًا
}

/// <summary>Category chosen on an income/expense entry (drives HR advance/reward linkage).</summary>
public enum AccountingEntryKind
{
    General = 0,   // عام
    Advance = 1,   // سلفة
    Reward = 2,    // مكافأة
}

/// <summary>Where a customer came from.</summary>
public enum CustomerSource
{
    Facebook = 0,
    Instagram = 1,
    Google = 2,
    Website = 3,
    Referral = 4,
    WalkIn = 5,
    Other = 6,
    Salesperson = 7, // shown as the top option in the source dropdown
}
