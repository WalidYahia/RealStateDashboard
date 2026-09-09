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
    ContractorPayment = 7,   // سداد دفعة لمقاول على أمر شغل (مصروف)
}

/// <summary>The five classic account classes of a double-entry chart of accounts.</summary>
public enum AccountType
{
    Asset = 0,      // الأصول — normally Debit
    Liability = 1,  // الخصوم — normally Credit
    Equity = 2,     // حقوق الملكية — normally Credit
    Revenue = 3,    // الإيرادات — normally Credit
    Expense = 4,    // المصروفات — normally Debit
}

/// <summary>Lifecycle of a journal entry. Posted entries are the financial source of truth.</summary>
public enum JournalEntryStatus
{
    Draft = 0,
    Posted = 1,
    Reversed = 2,
}

/// <summary>The progress field of a work order that a log entry records a change to.</summary>
public enum WorkOrderField
{
    Execution = 0,   // نسبة التنفيذ
    Uplift = 1,      // نسبة التعلية
    Deductions = 2,  // الخصومات
}
