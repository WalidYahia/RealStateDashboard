using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Common;

/// <summary>
/// The three built-in project types seeded for every tenant. Existing tenants are seeded by the
/// <c>SeedProjectTypesForAllTenants</c> migration; this on-demand helper covers tenants created
/// afterwards (mirrors how the built-in transaction categories are ensured per tenant).
/// </summary>
public static class ProjectTypeDefaults
{
    public static readonly (string Name, ProjectType BaseType, int Sort)[] Items =
    {
        ("عمارة", ProjectType.Building, 1),
        ("مول",   ProjectType.Mall,     2),
        ("أرض",   ProjectType.Land,     3),
    };

    /// <summary>Seeds the three built-in project types for the current tenant if it has none yet.</summary>
    public static async Task EnsureAsync(IApplicationDbContext db, CancellationToken ct = default)
    {
        if (await db.ProjectTypes.AnyAsync(ct)) return;
        foreach (var (name, baseType, sort) in Items)
            db.ProjectTypes.Add(new ProjectTypeDefinition { Name = name, BaseType = baseType, SortOrder = sort, IsActive = true });
        await db.SaveChangesAsync(ct);
    }
}
