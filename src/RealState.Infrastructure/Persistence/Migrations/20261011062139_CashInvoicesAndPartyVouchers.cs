using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashInvoicesAndPartyVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultCash",
                table: "Suppliers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoiceCash",
                table: "SupplierPayments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsInvoiceCash",
                table: "SalesInvoiceCollections",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "SafeTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupplierId",
                table: "SafeTransactions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CashSafeId",
                table: "PurchaseInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentType",
                table: "PurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "CashSafeId",
                table: "ProductSalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentType",
                table: "ProductSalesInvoices",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefaultCash",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDefaultCash",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "IsInvoiceCash",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "IsInvoiceCash",
                table: "SalesInvoiceCollections");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "SafeTransactions");

            migrationBuilder.DropColumn(
                name: "SupplierId",
                table: "SafeTransactions");

            migrationBuilder.DropColumn(
                name: "CashSafeId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentType",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "CashSafeId",
                table: "ProductSalesInvoices");

            migrationBuilder.DropColumn(
                name: "PaymentType",
                table: "ProductSalesInvoices");

            migrationBuilder.DropColumn(
                name: "IsDefaultCash",
                table: "Customers");
        }
    }
}
