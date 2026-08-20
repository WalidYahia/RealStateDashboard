using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadChannel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceCampaignId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SourceCampaignId",
                table: "Customers");
        }
    }
}
