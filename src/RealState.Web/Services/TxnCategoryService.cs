using Microsoft.EntityFrameworkCore;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Web.Services;

public interface ITxnCategoryService
{
    /// <summary>Creates the locked built-in categories (عام / سلفة / مكافأة) for the current tenant if missing.</summary>
    Task EnsureBuiltInsAsync(CancellationToken ct = default);

    /// <summary>Categories for a transaction type, built-ins first (used by the settings page and the entry form).</summary>
    Task<List<TxnCategory>> ListAsync(TxnType type, bool activeOnly, CancellationToken ct = default);
}

public class TxnCategoryService : ITxnCategoryService
{
    private readonly IApplicationDbContext _db;
    public TxnCategoryService(IApplicationDbContext db) => _db = db;

    // Built-in (locked) categories per type: عام + سلفة for both, plus مكافأة for expenses.
    private static readonly (TxnType Type, AccountingEntryKind Kind, string Name, int Sort)[] BuiltIns =
    {
        (TxnType.Expense, AccountingEntryKind.General, "عام", 0),
        (TxnType.Expense, AccountingEntryKind.Advance, "سلفة", 1),
        (TxnType.Expense, AccountingEntryKind.Reward,  "مكافأة", 2),
        (TxnType.Income,  AccountingEntryKind.General, "عام", 0),
        (TxnType.Income,  AccountingEntryKind.Advance, "سلفة", 1),
    };

    public async Task EnsureBuiltInsAsync(CancellationToken ct = default)
    {
        var existing = await _db.TxnCategories.Where(c => c.IsBuiltIn).ToListAsync(ct);
        var added = false;
        foreach (var b in BuiltIns)
        {
            if (existing.Any(c => c.Type == b.Type && c.BuiltInKind == b.Kind)) continue;
            _db.TxnCategories.Add(new TxnCategory
            {
                Type = b.Type, BuiltInKind = b.Kind, Name = b.Name, IsBuiltIn = true, SortOrder = b.Sort, IsActive = true
            });
            added = true;
        }
        if (added) await _db.SaveChangesAsync(ct);
    }

    public async Task<List<TxnCategory>> ListAsync(TxnType type, bool activeOnly, CancellationToken ct = default)
    {
        var q = _db.TxnCategories.Where(c => c.Type == type);
        if (activeOnly) q = q.Where(c => c.IsActive);
        return await q.OrderByDescending(c => c.IsBuiltIn).ThenBy(c => c.SortOrder).ThenBy(c => c.Name).ToListAsync(ct);
    }
}
