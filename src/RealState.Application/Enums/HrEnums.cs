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
}
