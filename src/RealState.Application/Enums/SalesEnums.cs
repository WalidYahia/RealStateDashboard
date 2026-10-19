namespace RealState.Application.Enums;

/// <summary>How far apart installments fall due.</summary>
public enum InstallmentStep
{
    Monthly = 0,     // شهري
    Quarterly = 1,   // كل 3 أشهر
    SemiAnnual = 2,  // كل 6 أشهر
    Yearly = 3,      // سنوي
}

/// <summary>What a scheduled contract payment is for. All kinds together make up the contract's total price.</summary>
public enum InstallmentType
{
    Installment = 0,   // قسط
    DownPayment = 1,   // مقدم
    Maintenance = 2,   // صيانة
}

/// <summary>Computed payment state of an installment.</summary>
public enum InstallmentStatus
{
    Pending = 0,   // مستحق لاحقًا
    Overdue = 1,   // متأخر
    Paid = 2,      // مدفوع
}

public static class InstallmentStepExtensions
{
    public static int Months(this InstallmentStep s) => s switch
    {
        InstallmentStep.Monthly => 1,
        InstallmentStep.Quarterly => 3,
        InstallmentStep.SemiAnnual => 6,
        InstallmentStep.Yearly => 12,
        _ => 1
    };
}
