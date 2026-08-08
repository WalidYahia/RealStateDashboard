namespace RealState.Application.Enums;

/// <summary>How far apart installments fall due.</summary>
public enum InstallmentStep
{
    Monthly = 0,     // شهري
    Quarterly = 1,   // كل 3 أشهر
    SemiAnnual = 2,  // كل 6 أشهر
    Yearly = 3,      // سنوي
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
