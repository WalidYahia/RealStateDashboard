using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractManualScheduleAndTaskLogAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WorkTaskLogId",
                table: "WorkTaskAttachments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsManualSchedule",
                table: "SaleContracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTaskAttachments_WorkTaskLogId",
                table: "WorkTaskAttachments",
                column: "WorkTaskLogId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkTaskAttachments_WorkTaskLogs_WorkTaskLogId",
                table: "WorkTaskAttachments",
                column: "WorkTaskLogId",
                principalTable: "WorkTaskLogs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkTaskAttachments_WorkTaskLogs_WorkTaskLogId",
                table: "WorkTaskAttachments");

            migrationBuilder.DropIndex(
                name: "IX_WorkTaskAttachments_WorkTaskLogId",
                table: "WorkTaskAttachments");

            migrationBuilder.DropColumn(
                name: "WorkTaskLogId",
                table: "WorkTaskAttachments");

            migrationBuilder.DropColumn(
                name: "IsManualSchedule",
                table: "SaleContracts");
        }
    }
}
