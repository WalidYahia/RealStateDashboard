using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProjectExpensesAndOrderPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SupplierOrderId",
                table: "SupplierPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProjectId",
                table: "SafeTransactions",
                type: "uniqueidentifier",
                nullable: true);

            // Backfill: attribute existing stage-expense transactions to their project so they keep
            // showing under the project's new المصاريف tab after the stage↔expense link is retired.
            migrationBuilder.Sql(
                "UPDATE t SET t.ProjectId = st.ProjectId " +
                "FROM SafeTransactions t " +
                "INNER JOIN StageExpenses se ON se.Id = t.StageExpenseId " +
                "INNER JOIN ProjectStages st ON st.Id = se.StageId " +
                "WHERE t.StageExpenseId IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierOrderId",
                table: "SupplierPayments",
                column: "SupplierOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeTransactions_ProjectId",
                table: "SafeTransactions",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_SafeTransactions_Projects_ProjectId",
                table: "SafeTransactions",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SupplierPayments_SupplierOrders_SupplierOrderId",
                table: "SupplierPayments",
                column: "SupplierOrderId",
                principalTable: "SupplierOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SafeTransactions_Projects_ProjectId",
                table: "SafeTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_SupplierPayments_SupplierOrders_SupplierOrderId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SupplierPayments_SupplierOrderId",
                table: "SupplierPayments");

            migrationBuilder.DropIndex(
                name: "IX_SafeTransactions_ProjectId",
                table: "SafeTransactions");

            migrationBuilder.DropColumn(
                name: "SupplierOrderId",
                table: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "SafeTransactions");
        }
    }
}
