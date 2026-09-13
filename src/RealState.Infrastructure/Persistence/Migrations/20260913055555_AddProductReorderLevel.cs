using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductReorderLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ReorderLevel",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReorderLevel",
                table: "Products");
        }
    }
}
