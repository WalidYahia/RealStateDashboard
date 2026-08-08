using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupplierPaymentPerSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_SupplierOrders_SupplierOrderId",
                table: "SupplierPayments");

            migrationBuilder.RenameColumn(
                name: "SupplierOrderId",
                table: "SupplierPayments",
                newName: "SupplierId");

            migrationBuilder.RenameIndex(
                name: "IX_SupplierPayments_SupplierOrderId",
                table: "SupplierPayments",
                newName: "IX_SupplierPayments_SupplierId");

            // Existing rows hold an order id in the renamed column; repoint them to that order's supplier
            // so the new FK to Suppliers is satisfied.
            migrationBuilder.Sql(
                "UPDATE sp SET sp.SupplierId = so.SupplierId " +
                "FROM SupplierPayments sp INNER JOIN SupplierOrders so ON so.Id = sp.SupplierId;");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_Suppliers_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_Suppliers_SupplierId",
                table: "SupplierPayments");

            migrationBuilder.RenameColumn(
                name: "SupplierId",
                table: "SupplierPayments",
                newName: "SupplierOrderId");

            migrationBuilder.RenameIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                newName: "IX_SupplierPayments_SupplierOrderId");

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_SupplierOrders_SupplierOrderId",
                table: "SupplierPayments",
                column: "SupplierOrderId",
                principalTable: "SupplierOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
