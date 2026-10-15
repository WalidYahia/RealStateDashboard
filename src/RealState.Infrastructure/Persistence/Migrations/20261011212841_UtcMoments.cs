using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Moments (journal entries, vouchers, transfers, invoices, logs) are stored in UTC. Values written before were the
    /// server's local time, so they're converted from the server's time zone — the one that wrote them — to UTC
    /// (UtcConversionSql; the rest of the date/time columns follow in UtcAllDates).
    /// </summary>
    public partial class UtcMoments : Migration
    {
        private static readonly (string Table, string Column)[] Moments =
        {
            ("JournalEntries", "Date"),
            ("SafeTransactions", "OccurredAt"),
            ("SafeTransfers", "OccurredAt"),
            ("ProductSalesInvoices", "InvoiceDate"),
            ("PurchaseInvoices", "InvoiceDate"),
            ("ActivityLogs", "Timestamp"),
            ("WorkTaskLogs", "At"),
            ("WorkOrderLogs", "At"),
            ("FinancialCustodies", "ClosedAt"),
            ("CampaignLeads", "CreatedTime"),
        };

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => UtcConversionSql.Convert(migrationBuilder, Moments, toUtc: true);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => UtcConversionSql.Convert(migrationBuilder, Moments, toUtc: false);
    }
}
