using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductMultiUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "SupplierOrderItems",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "SupplierOrderItems",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockTransferLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "StockTransferLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "StockTransferLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "StockTransferLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "StockCountLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "StockCountLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "StockCountLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "PurchaseInvoiceItems",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "PurchaseInvoiceItems",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "ProductSalesInvoiceItems",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "ProductSalesInvoiceItems",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<byte>(
                name: "DefaultUnitLevel",
                table: "Products",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice",
                table: "Products",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice2",
                table: "Products",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice3",
                table: "Products",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "Unit2Factor",
                table: "Products",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "Unit2Id",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Unit3Factor",
                table: "Products",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "Unit3Id",
                table: "Products",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "InventoryMovements",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "InventoryAdjustmentLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "InventoryAdjustmentLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "InventoryAdjustmentLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "InventoryAdjustmentLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "GoodsReceiptLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "GoodsReceiptLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "GoodsReceiptLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "GoodsReceiptLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "GoodsIssueLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,4)",
                oldPrecision: 18,
                oldScale: 4);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitFactor",
                table: "GoodsIssueLines",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<byte>(
                name: "UnitLevel",
                table: "GoodsIssueLines",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);

            migrationBuilder.AddColumn<string>(
                name: "UnitName",
                table: "GoodsIssueLines",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Unit2Id",
                table: "Products",
                column: "Unit2Id");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Unit3Id",
                table: "Products",
                column: "Unit3Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_UnitsOfMeasure_Unit2Id",
                table: "Products",
                column: "Unit2Id",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_UnitsOfMeasure_Unit3Id",
                table: "Products",
                column: "Unit3Id",
                principalTable: "UnitsOfMeasure",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_UnitsOfMeasure_Unit2Id",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_UnitsOfMeasure_Unit3Id",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Unit2Id",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Unit3Id",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "SupplierOrderItems");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "SupplierOrderItems");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "StockTransferLines");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "StockTransferLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "StockTransferLines");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "StockCountLines");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "StockCountLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "StockCountLines");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "ProductSalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "ProductSalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "DefaultUnitLevel",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalePrice",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalePrice2",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalePrice3",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Unit2Factor",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Unit2Id",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Unit3Factor",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Unit3Id",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "InventoryAdjustmentLines");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "UnitFactor",
                table: "GoodsIssueLines");

            migrationBuilder.DropColumn(
                name: "UnitLevel",
                table: "GoodsIssueLines");

            migrationBuilder.DropColumn(
                name: "UnitName",
                table: "GoodsIssueLines");

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "StockTransferLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "InventoryMovements",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "InventoryAdjustmentLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "GoodsReceiptLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);

            migrationBuilder.AlterColumn<decimal>(
                name: "UnitCost",
                table: "GoodsIssueLines",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);
        }
    }
}
