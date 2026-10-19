using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InstallmentTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAuto",
                table: "Installments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Installments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Existing schedules: #0 was the down payment; the numbered rows of a non-manual contract were generated.
            migrationBuilder.Sql("UPDATE [Installments] SET [Type] = 1 WHERE [Number] <= 0;");
            migrationBuilder.Sql(@"UPDATE i SET i.[IsAuto] = 1
FROM [Installments] i INNER JOIN [SaleContracts] c ON c.[Id] = i.[SaleContractId]
WHERE i.[Number] > 0 AND c.[IsManualSchedule] = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAuto",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Installments");
        }
    }
}
