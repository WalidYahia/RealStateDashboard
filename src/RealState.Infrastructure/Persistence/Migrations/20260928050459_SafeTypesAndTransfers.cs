using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SafeTypesAndTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Safes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "SafeTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromSafeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToSafeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OutTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InTransactionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_SafeTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafeTransfers_SafeTransactions_InTransactionId",
                        column: x => x.InTransactionId,
                        principalTable: "SafeTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SafeTransfers_SafeTransactions_OutTransactionId",
                        column: x => x.OutTransactionId,
                        principalTable: "SafeTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SafeTransfers_Safes_FromSafeId",
                        column: x => x.FromSafeId,
                        principalTable: "Safes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SafeTransfers_Safes_ToSafeId",
                        column: x => x.ToSafeId,
                        principalTable: "Safes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SafeTransfers_FromSafeId",
                table: "SafeTransfers",
                column: "FromSafeId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeTransfers_InTransactionId",
                table: "SafeTransfers",
                column: "InTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeTransfers_OutTransactionId",
                table: "SafeTransfers",
                column: "OutTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_SafeTransfers_TenantId_Number",
                table: "SafeTransfers",
                columns: new[] { "TenantId", "Number" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_SafeTransfers_ToSafeId",
                table: "SafeTransfers",
                column: "ToSafeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SafeTransfers");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Safes");
        }
    }
}
