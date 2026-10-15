using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixedAssetLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisposalReason",
                table: "FixedAssets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "MaterialCustodies",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferredFromId",
                table: "MaterialCustodies",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcquisitionMethod",
                table: "FixedAssets",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DepreciationStartDate",
                table: "FixedAssets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisposalDate",
                table: "FixedAssets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DisposalProceeds",
                table: "FixedAssets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "DisposalSafeId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DisposalTransactionId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningAccumulatedDepreciation",
                table: "FixedAssets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseSafeId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseTransactionId",
                table: "FixedAssets",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalvageValue",
                table: "FixedAssets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "UsefulLifeMonths",
                table: "FixedAssets",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "FixedAssetCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultUsefulLifeMonths = table.Column<int>(type: "int", nullable: false),
                    AssetAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AccumulatedDepreciationAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DepreciationExpenseAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_FixedAssetCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FixedAssetDepreciations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FixedAssetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Period = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_FixedAssetDepreciations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FixedAssetDepreciations_FixedAssets_FixedAssetId",
                        column: x => x.FixedAssetId,
                        principalTable: "FixedAssets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssets_CategoryId",
                table: "FixedAssets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FixedAssetDepreciations_FixedAssetId_Period",
                table: "FixedAssetDepreciations",
                columns: new[] { "FixedAssetId", "Period" });

            migrationBuilder.AddForeignKey(
                name: "FK_FixedAssets_FixedAssetCategories_CategoryId",
                table: "FixedAssets",
                column: "CategoryId",
                principalTable: "FixedAssetCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Each distinct free-text category becomes a category record (blank → «عام»); its accounts are created on first use.
            migrationBuilder.Sql(@"
INSERT INTO FixedAssetCategories (Id, TenantId, Name, DefaultUsefulLifeMonths, CreatedAt, IsDeleted)
SELECT NEWID(), x.TenantId, x.Name, 0, SYSUTCDATETIME(), 0
FROM (SELECT DISTINCT TenantId, COALESCE(NULLIF(LTRIM(RTRIM(Category)), N''), N'عام') AS Name FROM FixedAssets) x;
UPDATE a SET a.CategoryId = c.Id
FROM FixedAssets a
JOIN FixedAssetCategories c ON c.TenantId = a.TenantId AND c.Name = COALESCE(NULLIF(LTRIM(RTRIM(a.Category)), N''), N'عام');");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "FixedAssets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "FixedAssets",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.Sql(@"
UPDATE a SET a.Category = c.Name
FROM FixedAssets a JOIN FixedAssetCategories c ON c.Id = a.CategoryId;");

            migrationBuilder.DropForeignKey(
                name: "FK_FixedAssets_FixedAssetCategories_CategoryId",
                table: "FixedAssets");

            migrationBuilder.DropTable(
                name: "FixedAssetCategories");

            migrationBuilder.DropTable(
                name: "FixedAssetDepreciations");

            migrationBuilder.DropIndex(
                name: "IX_FixedAssets_CategoryId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "MaterialCustodies");

            migrationBuilder.DropColumn(
                name: "TransferredFromId",
                table: "MaterialCustodies");

            migrationBuilder.DropColumn(
                name: "AcquisitionMethod",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DepreciationStartDate",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DisposalDate",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DisposalProceeds",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DisposalSafeId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DisposalTransactionId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "OpeningAccumulatedDepreciation",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "PurchaseSafeId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "PurchaseTransactionId",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "SalvageValue",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "UsefulLifeMonths",
                table: "FixedAssets");

            migrationBuilder.DropColumn(
                name: "DisposalReason",
                table: "FixedAssets");
        }
    }
}
