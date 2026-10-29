using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>
/// The balances of every account at one moment: <see cref="Accounts"/> that hold at least one entry, how many <see cref="AccountsOwing"/> (balance above zero) and the <see cref="TotalOwed"/> they add up to, how many are
/// <see cref="AccountsInCredit"/> (balance below zero) and the <see cref="TotalCredit"/> (shown as a positive amount), and the <see cref="NetBalance"/> = owed - credit. An account's balance is the plain sum of its ledger
/// entries recorded before that moment, never a stored figure. The ledger refuses an entry that would leave an account below zero (a database rule), so <see cref="AccountsInCredit"/> is always 0 today; it is
/// still reported so that a broken rule could never hide.
/// </summary>
public sealed record BalanceSnapshot(int Accounts, int AccountsOwing, decimal TotalOwed, int AccountsInCredit, decimal TotalCredit, decimal NetBalance);

/// <summary>
/// The balances report: the snapshot at the START of the first day (<see cref="AtStart"/>) and at the END of the last day (<see cref="AtEnd"/>), the <see cref="Change"/> between them, and the proof that the change equals the
/// ledger itself: <see cref="LedgerChange"/> is an independent sum of the <see cref="LedgerEntries"/> recorded in the range, and <see cref="Reconciled"/> is true when both match. The change is the sum of every kind of entry
/// (production, collections, adjustments and loaded opening balances); the other financial reports break it down.
/// </summary>
public sealed record BalancesData(BalanceSnapshot AtStart, BalanceSnapshot AtEnd, decimal Change, decimal LedgerChange, int LedgerEntries, bool Reconciled);

/// <summary>
/// ALV-N006 slice 2b: balances. Source: the sum of each account's ledger entries recorded before a moment. Provider and location filters are refused (<c>filter_not_supported</c>): a balance belongs to an account, not to a
/// provider or a location. <c>NoData</c> means no account holds any entry at the end of the range (a range after all the activity still shows the balances). Failure modes handled: more accounts than the limit (413), an audit
/// entry that cannot be saved (503). Definition version 1.
/// </summary>
public class BalancesReportService(AlveraDbContext db, IPracticeClock clock, IOptions<ReportOptions> options, ILogger<BalancesReportService> log)
{
    public const int DefinitionVersion = 1;

    public async Task<ReportResult<BalancesData>> RunAsync(ReportFilter filter, Guid actor, CancellationToken ct)
    {
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "Balances cannot be filtered by provider or location: a balance belongs to an account.", 400);
        string consistency;
        BalancesData data;
        await using (var session = await ReportReadSession.OpenAsync(db, log, ct))
        {
            consistency = session.ReadConsistency;
            var atStart = await SnapshotAsync(filter.StartUtc, ct);
            var atEnd = await SnapshotAsync(filter.EndUtc, ct);
            var inRange = db.BillingEntries.AsNoTracking().Where(e => e.RecordedAtUtc >= filter.StartUtc && e.RecordedAtUtc < filter.EndUtc);
            var ledgerChange = await inRange.SumAsync(e => e.Amount, ct);
            var ledgerEntries = await inRange.CountAsync(ct);
            var change = atEnd.NetBalance - atStart.NetBalance;
            data = new BalancesData(atStart, atEnd, change, ledgerChange, ledgerEntries, change == ledgerChange);
        }

        await ReportSupport.AuditAsync(db, log, ReportIds.Balances, false, filter, data.AtEnd.Accounts, actor, ct);
        return new ReportResult<BalancesData>(ReportIds.Balances, DefinitionVersion, ReportSupport.View(filter), clock.UtcNow, consistency, data.AtEnd.Accounts, data.AtEnd.Accounts == 0, data);
    }

    private async Task<BalanceSnapshot> SnapshotAsync(DateTimeOffset before, CancellationToken ct)
    {
        var balances = await db.BillingEntries.AsNoTracking().Where(e => e.RecordedAtUtc < before).GroupBy(e => e.AccountId).Select(g => g.Sum(e => e.Amount)).ToListAsync(ct);
        ReportSupport.GuardSize(balances.Count, options.Value);
        var owing = balances.Where(b => b > 0).ToList();
        var credit = balances.Where(b => b < 0).ToList();
        return new BalanceSnapshot(balances.Count, owing.Count, owing.Sum(), credit.Count, -credit.Sum(), balances.Sum());
    }
}
