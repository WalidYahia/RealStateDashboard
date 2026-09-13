using RealState.Application.Entities;

namespace RealState.Application.Accounting;

/// <summary>
/// One debit-or-credit line to post. When <see cref="SubKind"/> + <see cref="SubRefId"/> are given, the
/// engine resolves (or creates) a per-entity subsidiary account under the control account
/// <see cref="AccountCode"/> and posts to it; otherwise it posts to <see cref="AccountCode"/> directly.
/// </summary>
public sealed record LedgerLine(
    string AccountCode,
    decimal Debit,
    decimal Credit,
    string? Memo = null,
    string? SubKind = null,
    Guid? SubRefId = null,
    string? SubName = null,
    Guid? ProjectId = null,
    Guid? CustomerId = null,
    Guid? SupplierId = null,
    Guid? ContractorId = null,
    Guid? EmployeeId = null,
    Guid? UnitId = null);

/// <summary>
/// Central double-entry engine. Every money action posts a balanced <see cref="JournalEntry"/> through
/// this service; the engine validates (Dr==Cr, no mixed/zero lines), assigns a number and resolves
/// accounts. It adds rows to the unit of work but does NOT call SaveChanges — the calling action commits
/// them atomically with its own business rows.
/// </summary>
public interface IAccountingEngine
{
    /// <summary>Seeds the default chart of accounts for the current tenant if it has none. Commits on its own.</summary>
    Task EnsureChartAsync(CancellationToken ct = default);

    /// <summary>Resolves a postable account by code, or a per-entity subsidiary under it. Creates subsidiaries on demand.</summary>
    Task<Account> ResolveAccountAsync(string code, string? subKind = null, Guid? subRefId = null, string? subName = null, CancellationToken ct = default);

    /// <summary>Validates + builds a balanced posted entry and adds it to the context (no SaveChanges).</summary>
    Task<JournalEntry> PostAsync(DateTime date, string description, string? sourceType, Guid? sourceId, IReadOnlyList<LedgerLine> lines, CancellationToken ct = default);

    /// <summary>Validates + builds a balanced entry from explicit account ids (manual entries, reversing entries).</summary>
    Task<JournalEntry> PostByIdsAsync(DateTime date, string description, string? sourceType,
        IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit, string? Memo)> lines, CancellationToken ct = default,
        Guid? sourceId = null);

    /// <summary>Removes the journal entries produced by a given business record (used when that record is deleted).</summary>
    Task RemoveBySourceAsync(string sourceType, Guid sourceId, CancellationToken ct = default);

    /// <summary>Next year-prefixed entry number for the given year (considers unsaved local entries).</summary>
    Task<int> NextNumberAsync(int year, CancellationToken ct = default);
}
