using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealState.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedProjectTypesForAllTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed the three built-in project types (عمارة/مول/أرض) for every existing tenant that
            // doesn't have any yet. Idempotent via NOT EXISTS.
            migrationBuilder.Sql(@"
INSERT INTO [ProjectTypes] ([Id],[TenantId],[Name],[BaseType],[SortOrder],[IsActive],[CreatedAt],[CreatedBy],[IsDeleted])
SELECT NEWID(), t.[Id], v.[Name], v.[BaseType], v.[SortOrder], 1, SYSUTCDATETIME(), N'system', 0
FROM [Tenants] t
CROSS JOIN (VALUES (N'عمارة', 0, 1), (N'مول', 1, 2), (N'أرض', 2, 3)) AS v([Name],[BaseType],[SortOrder])
WHERE t.[IsDeleted] = 0
  AND NOT EXISTS (SELECT 1 FROM [ProjectTypes] pt WHERE pt.[TenantId] = t.[Id]);");

            // Backfill existing projects: point each project at its tenant's type whose behaviour
            // (BaseType) matches the project's current built-in Type.
            migrationBuilder.Sql(@"
UPDATE p
SET p.[ProjectTypeId] = pt.[Id]
FROM [Projects] p
INNER JOIN [ProjectTypes] pt
    ON pt.[TenantId] = p.[TenantId] AND pt.[BaseType] = p.[Type] AND pt.[IsDeleted] = 0
WHERE p.[ProjectTypeId] IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data seed — nothing to reverse (leaving the seeded types + backfilled links in place).
        }
    }
}
