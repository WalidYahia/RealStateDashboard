using Microsoft.EntityFrameworkCore;
using RealState.Application.Common;
using RealState.Application.Entities;
using RealState.Application.Interfaces;

namespace RealState.Application.Accounting;

/// <summary>
/// Opening balances (الأرصدة الافتتاحية) of postable accounts. Each one is a single journal entry dated the tenant's opening
/// date: Dr / Cr the account, the other side «رصيد افتتاحي» (3200). A safe's opening is its <see cref="Safe.InitialAmount"/>
/// (which the safe balance and the overdraft guard read), posted by the existing «SafeOpening» entry.
/// </summary>
public interface IOpeningBalanceService
{
    /// <summary>The tenant's opening date: the one already used, else the earliest journal entry's day, else today.</summary>
    Task<DateTime> OpeningDateAsync(CancellationToken ct = default);

    /// <summary>Sets an account's opening balance (0 clears it) and re-posts its entry. Adds to the unit of work only.</summary>
    Task<AccountOpeningBalance?> SetAsync(Account account, decimal amount, bool isDebit, CancellationToken ct = default);

    /// <summary>Moves every opening balance (and its entry) to a new opening date. Returns how many were moved.</summary>
    Task<int> RedateAsync(DateTime date, CancellationToken ct = default);

    /// <summary>Keeps a safe's opening-balance row in step after the safe form changed its InitialAmount.</summary>
    Task SyncSafeAsync(Safe safe, CancellationToken ct = default);
}

public sealed class OpeningBalanceService : IOpeningBalanceService
{
    private readonly IApplicationDbContext _db;
    private readonly IAccountingEngine _engine;
    private readonly IAccountingService _accounting;
    private readonly IDateTimeProvider _clock;

    public OpeningBalanceService(IApplicationDbContext db, IAccountingEngine engine, IAccountingService accounting, IDateTimeProvider clock)
    {
        _db = db; _engine = engine; _accounting = accounting; _clock = clock;
    }

    /// <summary>
    /// The date set on the opening balances; before any is set, the first day of the running year — or the day of the
    /// earliest posted transaction when the books already go back further, so the opening still comes first.
    /// </summary>
    public async Task<DateTime> OpeningDateAsync(CancellationToken ct = default)
    {
        if (await _db.AccountOpeningBalances.Select(b => (DateTime?)b.Date).MinAsync(ct) is DateTime set) return set;
        var yearStart = new DateTime(_clock.Today.Year, 1, 1);
        var earliest = await _db.JournalEntries
            .Where(e => e.SourceType != AccountingSources.OpeningBalance && e.SourceType != "SafeOpening")
            .Select(e => (DateTime?)e.Date).MinAsync(ct);
        return earliest is DateTime first && first.Date < yearStart ? first.Date : yearStart;
    }

    public async Task<AccountOpeningBalance?> SetAsync(Account account, decimal amount, bool isDebit, CancellationToken ct = default)
    {
        if (!account.IsPostable) throw new InvalidOperationException("الرصيد الافتتاحي للحسابات الفرعية (القابلة للترحيل) فقط.");
        if (account.Code == LedgerAccounts.OpeningBalanceEquity) throw new InvalidOperationException("حساب «رصيد افتتاحي» هو الطرف المقابل للأرصدة الافتتاحية.");
        if (amount < 0) throw new InvalidOperationException("أدخل الرصيد موجبًا واختر مدين أو دائن.");
        amount = Math.Round(amount, 2);
        var date = await OpeningDateAsync(ct);

        if (account.SubKind == "Safe" && account.SubRefId is Guid safeId)
        {
            var safe = await _db.Safes.FirstOrDefaultAsync(s => s.Id == safeId, ct)
                       ?? throw new InvalidOperationException("الخزنة غير موجودة.");
            safe.InitialAmount = isDebit ? amount : -amount;
            await _accounting.PostSafeOpeningBalanceAsync(safe, ct, date);   // Dr النقدية / Cr رصيد افتتاحي (or reversed)
        }
        else await PostAsync(account, amount, isDebit, date, ct);

        return await UpsertAsync(account.Id, amount, isDebit, date, ct);
    }

    public async Task<int> RedateAsync(DateTime date, CancellationToken ct = default)
    {
        await _db.AccountOpeningBalances.ToListAsync(ct);   // track every row
        // Safes whose opening balance predates this page get their row first, so they move with the others.
        var withRow = _db.AccountOpeningBalances.Local.Select(b => b.AccountId).ToHashSet();
        foreach (var safe in await _db.Safes.Where(s => s.InitialAmount != 0).ToListAsync(ct))
        {
            var acc = await _engine.ResolveAccountAsync(LedgerAccounts.CashAndBanks, "Safe", safe.Id, safe.Name, ct);
            if (!withRow.Contains(acc.Id)) await UpsertAsync(acc.Id, Math.Abs(safe.InitialAmount), safe.InitialAmount >= 0, date, ct);
        }
        var rows = _db.AccountOpeningBalances.Local.Where(b => !b.IsDeleted).ToList();
        var ids = rows.Select(r => r.AccountId).ToList();
        var accounts = await _db.Accounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var r in rows)
        {
            r.Date = date;
            if (!accounts.TryGetValue(r.AccountId, out var account)) continue;
            if (account is { SubKind: "Safe", SubRefId: Guid safeId } && await _db.Safes.FirstOrDefaultAsync(s => s.Id == safeId, ct) is { } safe)
                await _accounting.PostSafeOpeningBalanceAsync(safe, ct, date);
            else
                await PostAsync(account, r.Amount, r.IsDebit, date, ct);
        }
        return rows.Count;
    }

    public async Task SyncSafeAsync(Safe safe, CancellationToken ct = default)
    {
        var account = await _engine.ResolveAccountAsync(LedgerAccounts.CashAndBanks, "Safe", safe.Id, safe.Name, ct);
        await UpsertAsync(account.Id, Math.Abs(safe.InitialAmount), safe.InitialAmount >= 0, await OpeningDateAsync(ct), ct);
    }

    private async Task PostAsync(Account account, decimal amount, bool isDebit, DateTime date, CancellationToken ct)
    {
        await _engine.RemoveBySourceAsync(AccountingSources.OpeningBalance, account.Id, ct);
        if (amount <= 0) return;
        var equity = await _engine.ResolveAccountAsync(LedgerAccounts.OpeningBalanceEquity, null, null, null, ct);
        var desc = $"رصيد افتتاحي — {account.Code} {account.Name}";
        await _engine.PostByIdsAsync(date, desc, AccountingSources.OpeningBalance, new List<(Guid, decimal, decimal, string?)>
        {
            (account.Id, isDebit ? amount : 0m, isDebit ? 0m : amount, desc),
            (equity.Id, isDebit ? 0m : amount, isDebit ? amount : 0m, desc),
        }, ct, account.Id);
    }

    private async Task<AccountOpeningBalance?> UpsertAsync(Guid accountId, decimal amount, bool isDebit, DateTime date, CancellationToken ct)
    {
        var row = _db.AccountOpeningBalances.Local.FirstOrDefault(b => b.AccountId == accountId && !b.IsDeleted)
                  ?? await _db.AccountOpeningBalances.FirstOrDefaultAsync(b => b.AccountId == accountId, ct);
        if (amount == 0)
        {
            if (row is not null) _db.AccountOpeningBalances.Remove(row);
            return null;
        }
        if (row is null) _db.AccountOpeningBalances.Add(row = new AccountOpeningBalance { AccountId = accountId });
        row.Amount = amount; row.IsDebit = isDebit; row.Date = date;
        return row;
    }
}
