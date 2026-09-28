using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VoucherSerials11Digits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "ReceiptNo",
                table: "WorkOrderPayments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "ReceiptNo",
                table: "SupplierPayments",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "Serial",
                table: "SafeTransactions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            // Voucher numbers (سند قبض / سند صرف) move from year × 100000 + seq (e.g. 202600007) to
            // year × 10000000 + seq (e.g. 20260000007) — same year, same sequence, so every voucher keeps
            // its number's meaning. Payment receipt numbers mirror the expense serial and follow the same map.
            // Only 9-digit year-prefixed values are touched (transfer legs carry 0).
            migrationBuilder.Sql(@"
UPDATE SafeTransactions  SET Serial    = (Serial / 100000) * 10000000 + (Serial % 100000)
 WHERE Serial >= 100000000 AND Serial < 1000000000;
UPDATE SupplierPayments  SET ReceiptNo = (ReceiptNo / 100000) * 10000000 + (ReceiptNo % 100000)
 WHERE ReceiptNo >= 100000000 AND ReceiptNo < 1000000000;
UPDATE WorkOrderPayments SET ReceiptNo = (ReceiptNo / 100000) * 10000000 + (ReceiptNo % 100000)
 WHERE ReceiptNo >= 100000000 AND ReceiptNo < 1000000000;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Back to year × 100000 + seq (fits int again) before narrowing the columns.
            migrationBuilder.Sql(@"
UPDATE SafeTransactions  SET Serial    = (Serial / 10000000) * 100000 + (Serial % 10000000)
 WHERE Serial >= 1000000000;
UPDATE SupplierPayments  SET ReceiptNo = (ReceiptNo / 10000000) * 100000 + (ReceiptNo % 10000000)
 WHERE ReceiptNo >= 1000000000;
UPDATE WorkOrderPayments SET ReceiptNo = (ReceiptNo / 10000000) * 100000 + (ReceiptNo % 10000000)
 WHERE ReceiptNo >= 1000000000;");

            migrationBuilder.AlterColumn<int>(
                name: "ReceiptNo",
                table: "WorkOrderPayments",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "ReceiptNo",
                table: "SupplierPayments",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AlterColumn<int>(
                name: "Serial",
                table: "SafeTransactions",
                type: "int",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
