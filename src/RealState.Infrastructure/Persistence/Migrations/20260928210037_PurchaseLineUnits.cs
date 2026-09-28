using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PurchaseLineUnits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "SupplierOrderItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Unit",
                table: "PurchaseInvoiceItems",
                type: "nvarchar(max)",
                nullable: true);

            // Existing lines: snapshot the product's current unit of measure (lines without a product/unit stay null).
            migrationBuilder.Sql(@"
UPDATE i SET i.Unit = u.Name
FROM SupplierOrderItems i
JOIN Products p ON p.Id = i.ProductId
JOIN UnitsOfMeasure u ON u.Id = p.UnitOfMeasureId;

UPDATE i SET i.Unit = u.Name
FROM PurchaseInvoiceItems i
JOIN Products p ON p.Id = i.ProductId
JOIN UnitsOfMeasure u ON u.Id = p.UnitOfMeasureId;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unit",
                table: "SupplierOrderItems");

            migrationBuilder.DropColumn(
                name: "Unit",
                table: "PurchaseInvoiceItems");
        }
    }
}
