namespace RealState.Application.Enums;

/// <summary>Where a physical custody item (عهدة عينية) stands.</summary>
public enum MaterialCustodyStatus
{
    HandedOver = 0, // لدى الموظف
    Returned = 1,   // مُستلمة (أُعيدت للشركة)
    Damaged = 2,    // أُعيدت تالفة
    Lost = 3,       // مفقودة
    Transferred = 4, // حُوِّلت لموظف آخر (عهدة جديدة باسمه)
    Cancelled = 5,   // أُلغيت (سُجِّلت بالخطأ — تبقى في السجل)
}

/// <summary>How a fixed asset entered the books.</summary>
public enum AssetAcquisitionMethod
{
    Cash = 0,           // شراء نقدي من خزنة (سند صرف)
    OpeningBalance = 1, // أصل مملوك مسبقًا (رصيد افتتاحي)
}

/// <summary>A cash custody (عهدة مالية) is open while the custodian still holds (or is owed) money.</summary>
public enum FinancialCustodyStatus
{
    Open = 0,   // مفتوحة
    Closed = 1, // مُقفلة (تمت التسوية)
}

/// <summary>One movement on a cash custody.</summary>
public enum CustodyMovementKind
{
    Disbursement = 0, // صرف للموظف من خزنة (يزيد الرصيد لديه)
    Expense = 1,      // مصروف مُعتمد في التسوية (ينقص الرصيد)
    Return = 2,       // رد نقدية إلى خزنة (ينقص الرصيد)
}
