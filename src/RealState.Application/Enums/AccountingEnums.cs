namespace RealState.Application.Enums;

/// <summary>Direction of a safe (treasury) transaction.</summary>
public enum TxnType
{
    Income = 0,   // إيراد (+)
    Expense = 1,  // مصروف (-)
}

/// <summary>Where a transaction originated.</summary>
public enum TxnSource
{
    Manual = 0,          // أُدخل يدويًا
    Collection = 1,      // تحصيل قسط (إيراد)
    ProjectExpense = 2,  // مصروف مرحلة مشروع
    SupplierPayment = 3, // سداد دفعة لمورد (مصروف)
    AdvanceDisbursement = 4, // صرف سلفة لموظف (مصروف)
    RewardPayment = 5,       // صرف مكافأة لموظف (مصروف)
    AdvanceRepayment = 6,    // تحصيل سداد سلفة (إيراد)
}
