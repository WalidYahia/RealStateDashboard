using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesAndPurchaseReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Reason",
                table: "GoodsReceipts",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesReturnId",
                table: "GoodsReceipts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseReturnId",
                table: "GoodsIssues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductSalesReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundSafeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundVoucherNo = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSalesReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSalesReturns_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSalesReturns_ProductSalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "ProductSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSalesReturns_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RefundSafeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RefundVoucherNo = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductSalesReturnItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnitLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    UnitFactor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSalesReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSalesReturnItems_ProductSalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "ProductSalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductSalesReturnItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnitLevel = table.Column<byte>(type: "tinyint", nullable: false),
                    UnitFactor = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItems_PurchaseReturns_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_SalesReturnId",
                table: "GoodsReceipts",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsIssues_PurchaseReturnId",
                table: "GoodsIssues",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturnItems_InvoiceItemId",
                table: "ProductSalesReturnItems",
                column: "InvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturnItems_ProductId",
                table: "ProductSalesReturnItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturnItems_SalesReturnId",
                table: "ProductSalesReturnItems",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturns_CustomerId",
                table: "ProductSalesReturns",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturns_SalesInvoiceId",
                table: "ProductSalesReturns",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturns_TenantId_Number",
                table: "ProductSalesReturns",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesReturns_WarehouseId",
                table: "ProductSalesReturns",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItems_InvoiceItemId",
                table: "PurchaseReturnItems",
                column: "InvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItems_ProductId",
                table: "PurchaseReturnItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItems_PurchaseReturnId",
                table: "PurchaseReturnItems",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_PurchaseInvoiceId",
                table: "PurchaseReturns",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_SupplierId",
                table: "PurchaseReturns",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_TenantId_Number",
                table: "PurchaseReturns",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_WarehouseId",
                table: "PurchaseReturns",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsIssues_PurchaseReturns_PurchaseReturnId",
                table: "GoodsIssues",
                column: "PurchaseReturnId",
                principalTable: "PurchaseReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceipts_ProductSalesReturns_SalesReturnId",
                table: "GoodsReceipts",
                column: "SalesReturnId",
                principalTable: "ProductSalesReturns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsIssues_PurchaseReturns_PurchaseReturnId",
                table: "GoodsIssues");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceipts_ProductSalesReturns_SalesReturnId",
                table: "GoodsReceipts");

            migrationBuilder.DropTable(
                name: "ProductSalesReturnItems");

            migrationBuilder.DropTable(
                name: "PurchaseReturnItems");

            migrationBuilder.DropTable(
                name: "ProductSalesReturns");

            migrationBuilder.DropTable(
                name: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceipts_SalesReturnId",
                table: "GoodsReceipts");

            migrationBuilder.DropIndex(
                name: "IX_GoodsIssues_PurchaseReturnId",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "SalesReturnId",
                table: "GoodsReceipts");

            migrationBuilder.DropColumn(
                name: "PurchaseReturnId",
                table: "GoodsIssues");
        }
    }
}
