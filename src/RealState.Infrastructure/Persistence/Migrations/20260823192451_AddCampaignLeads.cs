using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCampaignLeads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignLeads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AdId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdsetId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AdsetName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CampaignExternalId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CampaignName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FormId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FormName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsOrganic = table.Column<bool>(type: "bit", nullable: false),
                    Platform = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LeadStatus = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtraFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_CampaignLeads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CampaignLeads_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CampaignLeads_CustomerId",
                table: "CampaignLeads",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CampaignLeads_TenantId_ExternalId",
                table: "CampaignLeads",
                columns: new[] { "TenantId", "ExternalId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignLeads");
        }
    }
}
