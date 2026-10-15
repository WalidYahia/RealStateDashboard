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
    SafeTransfer = 8,        // تحويل نقدية بين الخزائن (منصرف من خزنة / وارد إلى أخرى) — ليس إيرادًا ولا مصروفًا
    SalesInvoiceCollection = 9, // تحصيل من عميل على فاتورة مبيعات (إيراد)
    SalesReturnRefund = 10,     // رد نقدية لعميل عن مرتجع مبيعات (مصروف — يعيد ذمة العميل)
    PurchaseReturnRefund = 11,  // استرداد نقدية من مورد عن مرتجع مشتريات (إيراد — يعيد ذمة المورد)
    CustodyDisbursement = 12,   // صرف عهدة مالية لموظف (منصرف — يزيد رصيد العُهد المالية)
    CustodyReturn = 13,         // رد نقدية من عهدة مالية إلى خزنة (وارد — ينقص رصيد العُهد المالية)
    FixedAssetPurchase = 14,    // شراء أصل ثابت نقدًا (منصرف — يُضاف لحساب الأصل)
    FixedAssetSale = 15,        // متحصلات بيع أصل ثابت (وارد — قيده ضمن قيد الاستبعاد)
    CustomerAccount = 16,       // سند قبض / صرف على حساب عميل (من صفحتي الإيرادات / المصروفات) — يحرّك ذمة العميل
    SupplierAccount = 17,       // سند صرف / قبض على حساب مورد (من صفحتي المصروفات / الإيرادات) — يحرّك ذمة المورد
}

/// <summary>How a sales / purchase invoice is settled (نقدي / آجل).</summary>
public enum InvoicePaymentType
{
    Credit = 0,   // آجل — يبقى مستحقًا ويُحصَّل / يُسدَّد لاحقًا
    Cash = 1,     // نقدي — يُحصَّل / يُسدَّد بالكامل من خزنة عند الحفظ
}

/// <summary>Kind of safe (نوع الخزنة) — where the money is held.</summary>
public enum SafeType
{
    Normal = 0,        // عادية (نقدية)
    Bank = 1,          // بنك
    DigitalWallet = 2, // محفظة إلكترونية
    Instapay = 3,      // إنستاباي
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
