using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One account behind a balance number: ids and the balance (positive owed, negative credit). No name.</summary>
public sealed record BalanceAccountRow(Guid AccountId, Guid PatientId, decimal Balance);

/// <summary>Where the next page starts: after this balance and, for equal balances, after this account id.</summary>
public sealed record BalanceCursor(decimal AfterBalance, Guid AfterAccountId);

public static class BalanceSides
{
    public const string Owing = "Owing";
    public const string Credit = "Credit";
}

/// <summary><see cref="Total"/> and <see cref="TotalAmount"/> are the number of accounts and their balances added up, which are the figures of the report cell that was clicked (<see cref="TotalAmount"/> is negative for the credit side); <see cref="Rows"/> is one page.</summary>
public sealed record BalancePage(string ReportId, ReportFilterView Filter, string Side, int Total, decimal TotalAmount, IReadOnlyList<BalanceAccountRow> Rows, BalanceCursor? Next);

public sealed record BalanceDrillCriteria(string? Side, int? Limit, decimal? AfterBalance, Guid? AfterAccountId);

/// <summary>
/// ALV-N006 slice 2b: the accounts behind a balance number, as of the END of the report's range, largest balance first (the most credit first for the credit side). The count and the sum equal the report's cell. Pages are keyed
/// on (balance, account id). Every call is recorded like a report run. Failure modes handled: an unknown side, a filter the report does not support, a bad limit or half a cursor (stable 400 codes), an audit entry that
/// cannot be saved (503).
/// </summary>
/// <summary>An account and its balance as the query computes it (a plain class, because EF cannot compose a constructor over a grouped sum).</summary>
internal sealed class AccountBalance
{
    public Guid AccountId { get; set; }
    public Guid PatientId { get; set; }
    public decimal Balance { get; set; }
}

public class BalancesDrillService(AlveraDbContext db, IOptions<ReportOptions> options, ILogger<BalancesDrillService> log)
{
    public async Task<BalancePage> AccountsAsync(ReportFilter filter, BalanceDrillCriteria criteria, Guid actor, CancellationToken ct)
    {
        var opts = options.Value;
        if (filter.ProviderIds.Count > 0 || filter.LocationId is not null) throw new ReportException("filter_not_supported", "Balances cannot be filtered by provider or location.", 400);
        var side = criteria.Side ?? BalanceSides.Owing;
        if (side is not (BalanceSides.Owing or BalanceSides.Credit)) throw new ReportException("invalid_side", $"The side must be {BalanceSides.Owing} or {BalanceSides.Credit}.", 400);
        var limit = criteria.Limit ?? opts.DefaultDrillRows;
        if (limit < 1 || limit > opts.MaxDrillRows) throw new ReportException("invalid_limit", $"The page size must be between 1 and {opts.MaxDrillRows}.", 400);
        if ((criteria.AfterBalance is null) != (criteria.AfterAccountId is null)) throw new ReportException("invalid_cursor", "Give both parts of the page position, or neither.", 400);

        var end = filter.EndUtc;
        int total;
        decimal totalAmount;
        List<AccountBalance> found;
        await using (await ReportReadSession.OpenAsync(db, log, ct))
        {
            var accounts = db.BillingEntries.AsNoTracking().Where(e => e.RecordedAtUtc < end).GroupBy(e => new { e.AccountId, e.PatientId })
                .Select(g => new AccountBalance { AccountId = g.Key.AccountId, PatientId = g.Key.PatientId, Balance = g.Sum(e => e.Amount) });
            var owing = side == BalanceSides.Owing;
            var query = owing ? accounts.Where(a => a.Balance > 0) : accounts.Where(a => a.Balance < 0);
            total = await query.CountAsync(ct);
            totalAmount = total == 0 ? 0m : await query.SumAsync(a => a.Balance, ct);
            if (criteria.AfterBalance is { } afterBalance && criteria.AfterAccountId is { } afterId)
                query = owing
                    ? query.Where(a => a.Balance < afterBalance || (a.Balance == afterBalance && a.AccountId.CompareTo(afterId) > 0))
                    : query.Where(a => a.Balance > afterBalance || (a.Balance == afterBalance && a.AccountId.CompareTo(afterId) > 0));
            found = await (owing ? query.OrderByDescending(a => a.Balance).ThenBy(a => a.AccountId) : query.OrderBy(a => a.Balance).ThenBy(a => a.AccountId)).Take(limit + 1).ToListAsync(ct);
        }

        var more = found.Count > limit;
        if (more) found.RemoveAt(found.Count - 1);
        var rows = found.Select(a => new BalanceAccountRow(a.AccountId, a.PatientId, a.Balance)).ToList();
        await ReportSupport.AuditAsync(db, log, ReportIds.Balances, true, filter, rows.Count, actor, ct);
        return new BalancePage(ReportIds.Balances, ReportSupport.View(filter), side, total, totalAmount, rows, more ? new BalanceCursor(rows[^1].Balance, rows[^1].AccountId) : null);
    }
}
