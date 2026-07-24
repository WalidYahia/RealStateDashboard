namespace RealState.Application.Enums;

public enum LeadStatus
{
    New = 0,
    Contacted = 1,
    Qualified = 2,
    Proposal = 3,
    Won = 4,
    Lost = 5,
}

public enum InvoiceStatus
{
    Draft = 0,
    Confirmed = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Cancelled = 4,
}

public enum TaskState
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Overdue = 3,
}

public enum PaymentMethod
{
    Cash = 0,
    BankTransfer = 1,
    Cheque = 2,
    Card = 3,
}

public enum TransactionType
{
    Income = 0,
    Expense = 1,
}

public enum NotificationLevel
{
    Info = 0,
    Warning = 1,
    Urgent = 2,
}
