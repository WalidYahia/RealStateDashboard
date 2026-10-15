using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// All date/time values are stored in UTC from now on: the DbContext converts every DateTime between the user's time
    /// zone (in code) and UTC (in the database). The audit columns, the user / navigation timestamps and the moments of
    /// UtcMoments already are UTC; every other date/time column was written in the server's local time and is converted
    /// here (calendar dates become that local midnight in UTC, read back as the same day).
    /// </summary>
    public partial class UtcAllDates : Migration
    {
        private static readonly (string Table, string Column)[] Columns =
        {
            ("AdvanceRepayments", "DueDate"), ("AdvanceRepayments", "PaidDate"),
            ("Advances", "Date"), ("Advances", "MonthlyStartDate"),
            ("AuditLogs", "Timestamp"),
            ("Campaigns", "EndDate"), ("Campaigns", "StartDate"), ("CampaignUpdates", "Date"),
            ("CostRevaluations", "Date"),
            ("CustomerLogs", "Date"),
            ("Employees", "BirthDate"), ("Employees", "HireDate"),
            ("Expenses", "Date"), ("Incomes", "Date"), ("SalesInvoices", "InvoiceDate"),
            ("FinancialCustodies", "Date"), ("FinancialCustodyMovements", "Date"),
            ("FixedAssetDepreciations", "Period"),
            ("FixedAssets", "DepreciationStartDate"), ("FixedAssets", "DisposalDate"), ("FixedAssets", "PurchaseDate"),
            ("GoodsIssues", "Date"), ("GoodsReceipts", "Date"), ("InventoryAdjustments", "Date"),
            ("InventoryMovements", "Date"), ("StockCounts", "Date"), ("StockTransfers", "Date"),
            ("Installments", "DueDate"), ("Installments", "PaidDate"),
            ("LeaveRequests", "Date"),
            ("MaterialCustodies", "ExpectedReturnDate"), ("MaterialCustodies", "HandoverDate"), ("MaterialCustodies", "ReturnDate"),
            ("Notifications", "Timestamp"),
            ("ProductSalesReturns", "ReturnDate"), ("PurchaseReturns", "ReturnDate"),
            ("Projects", "ActualEndDate"), ("Projects", "ActualStartDate"), ("Projects", "PlannedEndDate"), ("Projects", "PlannedStartDate"),
            ("ProjectStages", "ActualEndDate"), ("ProjectStages", "ActualStartDate"), ("ProjectStages", "PlannedEndDate"), ("ProjectStages", "PlannedStartDate"),
            ("Rewards", "Date"),
            ("SaleContracts", "ContractDate"), ("SaleContracts", "ReceiveDate"),
            ("SalesInvoiceCollections", "CollectedDate"), ("SupplierPayments", "PaidDate"), ("WorkOrderPayments", "PaidDate"),
            ("StageActivities", "Date"), ("StageExpenses", "Date"),
            ("SupplierOrders", "OrderDate"), ("WorkOrders", "OrderDate"),
            ("Tasks", "CompletedAt"), ("Tasks", "DueDate"),
            ("Vacations", "ApplyDate"), ("Vacations", "FromDate"), ("Vacations", "ToDate"),
            ("WorkTasks", "AssignedOn"), ("WorkTasks", "DueAt"), ("WorkTasks", "StartAt"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => UtcConversionSql.Convert(migrationBuilder, Columns, toUtc: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => UtcConversionSql.Convert(migrationBuilder, Columns, toUtc: false);
    }
}
