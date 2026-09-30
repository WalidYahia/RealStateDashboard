using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductSalesInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "GoodsIssues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SalesInvoiceId",
                table: "GoodsIssues",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductSalesInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    InvoiceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WarehouseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_ProductSalesInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSalesInvoices_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSalesInvoices_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductSalesInvoices_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProductSalesInvoiceItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_ProductSalesInvoiceItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSalesInvoiceItems_ProductSalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "ProductSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductSalesInvoiceItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesInvoiceCollections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CollectedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SafeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SafeTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceiptNo = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_SalesInvoiceCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceCollections_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceCollections_ProductSalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "ProductSalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesInvoiceCollections_Safes_SafeId",
                        column: x => x.SafeId,
                        principalTable: "Safes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsIssues_SalesInvoiceId",
                table: "GoodsIssues",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoiceItems_ProductId",
                table: "ProductSalesInvoiceItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoiceItems_SalesInvoiceId",
                table: "ProductSalesInvoiceItems",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoices_CustomerId",
                table: "ProductSalesInvoices",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoices_ProjectId",
                table: "ProductSalesInvoices",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoices_TenantId_Number",
                table: "ProductSalesInvoices",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoices_WarehouseId",
                table: "ProductSalesInvoices",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceCollections_CustomerId",
                table: "SalesInvoiceCollections",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceCollections_SafeId",
                table: "SalesInvoiceCollections",
                column: "SafeId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceCollections_SalesInvoiceId",
                table: "SalesInvoiceCollections",
                column: "SalesInvoiceId");

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsIssues_ProductSalesInvoices_SalesInvoiceId",
                table: "GoodsIssues",
                column: "SalesInvoiceId",
                principalTable: "ProductSalesInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // The posting profile's sales-revenue account was only "reserved" (4100, the real-estate sales
            // account) and never posted to. Product sales invoices now post to it, so point the untouched
            // default at the new «إيرادات مبيعات البضائع» (4200, created per tenant by the inventory defaults).
            migrationBuilder.Sql("UPDATE [InventoryPostingProfiles] SET [SalesRevenueCode] = N'4200' WHERE [SalesRevenueCode] = N'4100';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [InventoryPostingProfiles] SET [SalesRevenueCode] = N'4100' WHERE [SalesRevenueCode] = N'4200';");

            migrationBuilder.DropForeignKey(
                name: "FK_GoodsIssues_ProductSalesInvoices_SalesInvoiceId",
                table: "GoodsIssues");

            migrationBuilder.DropTable(
                name: "ProductSalesInvoiceItems");

            migrationBuilder.DropTable(
                name: "SalesInvoiceCollections");

            migrationBuilder.DropTable(
                name: "ProductSalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_GoodsIssues_SalesInvoiceId",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "GoodsIssues");

            migrationBuilder.DropColumn(
                name: "SalesInvoiceId",
                table: "GoodsIssues");
        }
    }
}
