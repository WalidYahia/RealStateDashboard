using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CancelFeeAmount",
                table: "SaleContracts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CancelFeePercent",
                table: "SaleContracts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CancelPaidAmount",
                table: "SaleContracts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "SaleContracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancelledAt",
                table: "SaleContracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelledByName",
                table: "SaleContracts",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCancelled",
                table: "SaleContracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "SaleContractId",
                table: "SafeTransactions",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancelFeeAmount",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "CancelFeePercent",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "CancelPaidAmount",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "CancelledByName",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "IsCancelled",
                table: "SaleContracts");

            migrationBuilder.DropColumn(
                name: "SaleContractId",
                table: "SafeTransactions");
        }
    }
}
