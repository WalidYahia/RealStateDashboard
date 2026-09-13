using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Enums;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

public class AccountingEngine : IAccountingEngine
{
    private readonly IApplicationDbContext _db;
    public AccountingEngine(IApplicationDbContext db) => _db = db;

    public async Task EnsureChartAsync(CancellationToken ct = default)
    {
        if (await _db.Accounts.AnyAsync(ct)) return;

        var map = new Dictionary<string, Account>();
        var order = 0;
        foreach (var d in LedgerAccounts.Defaults)
            map[d.Code] = new Account
            {
                Code = d.Code, Name = d.Name, Type = d.Type,
                IsPostable = d.IsPostable, SubKind = d.SubKind, IsActive = true, SortOrder = order++
            };
        foreach (var d in LedgerAccounts.Defaults)
            if (d.ParentCode != null && map.TryGetValue(d.ParentCode, out var parent))
                map[d.Code].Parent = parent;

        _db.Accounts.AddRange(map.Values);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Account> ResolveAccountAsync(string code, string? subKind = null, Guid? subRefId = null, string? subName = null, CancellationToken ct = default)
    {
        if (subKind is null || subRefId is null)
        {
            var acc = _db.Accounts.Local.FirstOrDefault(a => a.Code == code && !a.IsDeleted)
                      ?? await _db.Accounts.FirstOrDefaultAsync(a => a.Code == code, ct);
            if (acc is null)
            {
                await EnsureChartAsync(ct);
                acc = await _db.Accounts.FirstOrDefaultAsync(a => a.Code == code, ct);
            }
            return acc ?? throw new InvalidOperationException($"الحساب {code} غير موجود في دليل الحسابات.");
        }

        // Per-entity subsidiary under the control account `code`.
        var sub = _db.Accounts.Local.FirstOrDefault(a => a.SubKind == subKind && a.SubRefId == subRefId && !a.IsDeleted)
                  ?? await _db.Accounts.FirstOrDefaultAsync(a => a.SubKind == subKind && a.SubRefId == subRefId, ct);
        if (sub is not null) return sub;

        var parent = await ResolveAccountAsync(code, null, null, null, ct);
        var child = new Account
        {
            Code = $"{code}.{subRefId.Value.ToString("N")[..8]}",
            Name = string.IsNullOrWhiteSpace(subName) ? $"{parent.Name} {code}" : subName!,
            Type = parent.Type,
            ParentId = parent.Id,
            IsPostable = true,
            IsActive = true,
            SubKind = subKind,
            SubRefId = subRefId
        };
        _db.Accounts.Add(child);
        return child;
    }

    public async Task<JournalEntry> PostAsync(DateTime date, string description, string? sourceType, Guid? sourceId, IReadOnlyList<LedgerLine> lines, CancellationToken ct = default)
    {
        if (lines.Count < 2) throw new InvalidOperationException("القيد يجب أن يحتوي على سطرين على الأقل.");

        decimal totalDebit = 0, totalCredit = 0;
        foreach (var l in lines)
        {
            if (l.Debit < 0 || l.Credit < 0) throw new InvalidOperationException("لا يُسمح بقيم سالبة في القيد.");
            if (l.Debit > 0 && l.Credit > 0) throw new InvalidOperationException("لا يمكن أن يحمل السطر مدينًا ودائنًا معًا.");
            if (l.Debit == 0 && l.Credit == 0) throw new InvalidOperationException("لا يمكن أن يكون السطر بقيمة صفرية.");
            totalDebit += l.Debit;
            totalCredit += l.Credit;
        }
        if (Math.Round(totalDebit - totalCredit, 2) != 0m)
            throw new InvalidOperationException($"القيد غير متوازن: إجمالي المدين {totalDebit:N2} ≠ إجمالي الدائن {totalCredit:N2}.");

        var entry = new JournalEntry
        {
            Number = await NextNumberAsync(date.Year, ct),
            Date = date,
            Description = description,
            Status = JournalEntryStatus.Posted,
            SourceType = sourceType,
            SourceId = sourceId
        };
        foreach (var l in lines)
        {
            var account = await ResolveAccountAsync(l.AccountCode, l.SubKind, l.SubRefId, l.SubName, ct);
            entry.Lines.Add(new JournalLine
            {
                Account = account,
                Debit = l.Debit,
                Credit = l.Credit,
                Memo = l.Memo,
                ProjectId = l.ProjectId,
                CustomerId = l.CustomerId,
                SupplierId = l.SupplierId,
                ContractorId = l.ContractorId,
                EmployeeId = l.EmployeeId,
                UnitId = l.UnitId
            });
        }
        _db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task<JournalEntry> PostByIdsAsync(DateTime date, string description, string? sourceType,
        IReadOnlyList<(Guid AccountId, decimal Debit, decimal Credit, string? Memo)> lines, CancellationToken ct = default,
        Guid? sourceId = null)
    {
        if (lines.Count < 2) throw new InvalidOperationException("القيد يجب أن يحتوي على سطرين على الأقل.");
        decimal dr = 0, cr = 0;
        foreach (var l in lines)
        {
            if (l.Debit < 0 || l.Credit < 0) throw new InvalidOperationException("لا يُسمح بقيم سالبة في القيد.");
            if (l.Debit > 0 && l.Credit > 0) throw new InvalidOperationException("لا يمكن أن يحمل السطر مدينًا ودائنًا معًا.");
            if (l.Debit == 0 && l.Credit == 0) throw new InvalidOperationException("لا يمكن أن يكون السطر بقيمة صفرية.");
            dr += l.Debit; cr += l.Credit;
        }
        if (Math.Round(dr - cr, 2) != 0m)
            throw new InvalidOperationException($"القيد غير متوازن: إجمالي المدين {dr:N2} ≠ إجمالي الدائن {cr:N2}.");

        var entry = new JournalEntry
        {
            Number = await NextNumberAsync(date.Year, ct),
            Date = date, Description = description, Status = JournalEntryStatus.Posted,
            SourceType = sourceType, SourceId = sourceId
        };
        foreach (var l in lines)
            entry.Lines.Add(new JournalLine { AccountId = l.AccountId, Debit = l.Debit, Credit = l.Credit, Memo = l.Memo });
        _db.JournalEntries.Add(entry);
        return entry;
    }

    public async Task RemoveBySourceAsync(string sourceType, Guid sourceId, CancellationToken ct = default)
    {
        var entries = await _db.JournalEntries.Where(e => e.SourceType == sourceType && e.SourceId == sourceId).ToListAsync(ct);
        if (entries.Count == 0) return;
        var ids = entries.Select(e => e.Id).ToList();
        _db.JournalLines.RemoveRange(await _db.JournalLines.Where(l => ids.Contains(l.JournalEntryId)).ToListAsync(ct));
        _db.JournalEntries.RemoveRange(entries);
    }

    public async Task<int> NextNumberAsync(int year, CancellationToken ct = default)
    {
        var yearBase = year * 100000;
        var maxDb = await _db.JournalEntries
            .Where(e => e.Number >= yearBase && e.Number < yearBase + 100000)
            .MaxAsync(e => (int?)e.Number, ct) ?? yearBase;
        var maxLocal = _db.JournalEntries.Local
            .Where(e => e.Number >= yearBase && e.Number < yearBase + 100000)
            .Select(e => (int?)e.Number).DefaultIfEmpty(yearBase).Max() ?? yearBase;
        return Math.Max(maxDb, maxLocal) + 1;
    }
}
