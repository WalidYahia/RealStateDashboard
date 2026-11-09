using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixedAssetManualDepreciation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DepreciationStartDate",
                table: "FixedAssets");

            migrationBuilder.RenameColumn(
                name: "Period",
                table: "FixedAssetDepreciations",
                newName: "Date");

            migrationBuilder.RenameIndex(
                name: "IX_FixedAssetDepreciations_FixedAssetId_Period",
                table: "FixedAssetDepreciations",
                newName: "IX_FixedAssetDepreciations_FixedAssetId_Date");

            // Depreciation is now recorded by the user at a date, not per month. A monthly row held the month's first day while
            // its journal entry was dated the month's last day — take the entry's date (both stored in UTC), so the row and
            // its entry agree.
            migrationBuilder.Sql(@"
UPDATE d SET d.[Date] = je.[Date]
FROM [FixedAssetDepreciations] d
INNER JOIN [JournalEntries] je ON je.[SourceType] = N'FixedAssetDepreciation' AND je.[SourceId] = d.[Id] AND je.[IsDeleted] = 0;");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "FixedAssetDepreciations",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Notes",
                table: "FixedAssetDepreciations");

            migrationBuilder.RenameColumn(
                name: "Date",
                table: "FixedAssetDepreciations",
                newName: "Period");

            migrationBuilder.RenameIndex(
                name: "IX_FixedAssetDepreciations_FixedAssetId_Date",
                table: "FixedAssetDepreciations",
                newName: "IX_FixedAssetDepreciations_FixedAssetId_Period");

            migrationBuilder.AddColumn<DateTime>(
                name: "DepreciationStartDate",
                table: "FixedAssets",
                type: "datetime2",
                nullable: true);
        }
    }
}
