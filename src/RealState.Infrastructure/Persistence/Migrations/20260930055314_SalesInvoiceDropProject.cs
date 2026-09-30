using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SalesInvoiceDropProject : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ProductSalesInvoices_Projects_ProjectId",
                table: "ProductSalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_ProductSalesInvoices_ProjectId",
                table: "ProductSalesInvoices");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ProductSalesInvoices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "ProductSalesInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductSalesInvoices_ProjectId",
                table: "ProductSalesInvoices",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_ProductSalesInvoices_Projects_ProjectId",
                table: "ProductSalesInvoices",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
