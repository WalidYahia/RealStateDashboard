using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;
using RealState.Application.Interfaces;

namespace RealState.Web.Areas.Hr.Controllers;

/// <summary>Work locations / branches (المواقع والفروع) — the built-in «الموقع الرئيسي» and the auto code sequence.</summary>
internal static class HrBranches
{
    public const string MainName = "الموقع الرئيسي";

    /// <summary>
    /// The tenant's default branch («الموقع الرئيسي»), created when missing; every employee without a branch is put on it
    /// (the migration did this for existing data — this covers tenants created later).
    /// </summary>
    public static async Task<Branch> EnsureDefaultAsync(IApplicationDbContext db, CancellationToken ct)
    {
        var main = await db.Branches.FirstOrDefaultAsync(b => b.IsDefault, ct);
        var changed = false;
        if (main is null)
        {
            main = new Branch { Number = await NextNumberAsync(db, ct), Name = MainName, IsDefault = true };
            db.Branches.Add(main);
            changed = true;
        }
        foreach (var e in await db.Employees.Where(e => e.BranchId == null).ToListAsync(ct))
        {
            e.BranchId = main.Id;
            changed = true;
        }
        if (changed) await db.SaveChangesAsync(ct);
        return main;
    }

    /// <summary>The next branch number (BR-001, BR-002, …).</summary>
    public static async Task<int> NextNumberAsync(IApplicationDbContext db, CancellationToken ct) =>
        (await db.Branches.MaxAsync(b => (int?)b.Number, ct) ?? 0) + 1;
}
