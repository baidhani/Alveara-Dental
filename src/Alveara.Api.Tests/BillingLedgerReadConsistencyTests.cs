using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-007-C01 correction (ALV-N016-R02-01). The ledger is read with three queries. A hook on the reading context lets a competing payment commit exactly between the entries query and the allocations query,
/// so the formerly inconsistent window (an allocation whose entry the first query did not return, which escaped as <see cref="KeyNotFoundException"/>) happens every time instead of once in a long run.
/// The documented outcome: the reader repeats the read, so it sees one coherent picture; a writer whose account version is out of date then gets the controlled <see cref="ConcurrencyConflictException"/> and records
/// nothing; the ledger verifies clean; and a reader that cannot get a stable picture is told to reload (the same conflict), never an implementation exception.
/// </summary>
public class BillingLedgerReadConsistencyTests : BillingTestBase
{
    /// <summary>Runs <c>competing</c> once per matching query, right after the reading context's entries query has returned its rows and before its next query.</summary>
    private sealed class BetweenQueriesHook(Func<Task> competing, int times) : DbCommandInterceptor
    {
        public int Fired;

        /// <summary>The ledger load's entries query: all entries of the account, not the idempotency lookup, the balance sum or the next-number maximum.</summary>
        private static bool IsLedgerEntriesQuery(string sql)
        {
            var where = sql.Contains("WHERE", StringComparison.Ordinal) ? sql[sql.IndexOf("WHERE", StringComparison.Ordinal)..] : "";
            return sql.Contains("FROM [BillingEntries]", StringComparison.Ordinal) && !where.Contains("IdempotencyKey", StringComparison.Ordinal)
                && !sql.Contains("SUM(", StringComparison.Ordinal) && !sql.Contains("MAX(", StringComparison.Ordinal);
        }
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Fired < times && IsLedgerEntriesQuery(command.CommandText))
            {
                Fired++;
                await competing();
            }
            return result;
        }
    }

    private async Task<T> WithHookedAsync<T>(BetweenQueriesHook hook, Func<BillingService, Task<T>> action)
    {
        var options = new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(Fixture.ConnectionString).AddInterceptors(hook).Options;
        await using var db = new AlveraDbContext(options);
        return await action(Billing(db));
    }

    private async Task<(string Version, int Charges)> ChargedAsync()
    {
        var plan = await BillablePlanAsync();
        var charged = await Bill(s => s.RecordChargeAsync(Ann, ChargeOf(plan.Completed), S.Actor, default));
        return (charged.RowVersion!, charged.Entries.Count);
    }

    private async Task<string> FreshVersionAsync() => (await Bill(s => s.GetAccountAsync(Ann, default))).RowVersion!;

    [Fact]
    public async Task A_payment_committed_between_the_ledger_queries_gives_the_writer_the_controlled_conflict_and_never_a_missing_key()
    {
        var (version, _) = await ChargedAsync();
        var hook = new BetweenQueriesHook(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(5m, version, "rc-winner"), S.Actor, default)), 1);

        var loser = await Record.ExceptionAsync(() => WithHookedAsync(hook, s => s.RecordPaymentAsync(Ann, PaymentOf(7m, version, "rc-loser"), S.Actor, default)));

        Assert.Equal(1, hook.Fired);                                                                       // the window was really forced
        Assert.IsType<ConcurrencyConflictException>(loser);                                               // not KeyNotFoundException
        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Single(account.Entries, e => e.Kind == BillingKinds.Payment);                              // exactly one payment is recorded: the winner's
        Assert.Equal(5m, Math.Abs(account.Entries.Single(e => e.Kind == BillingKinds.Payment).Amount));   // the winner's 5, not the loser's 7
        Assert.Equal(95m, account.BalanceOwed);
        var verification = await Bill(s => s.VerifyLedgerAsync(Ann, default));
        Assert.True(verification.Consistent);
        Assert.Empty(verification.Findings);
    }

    [Fact]
    public async Task The_loser_retried_with_the_fresh_version_is_recorded_once_and_the_same_key_again_changes_nothing()
    {
        var (version, _) = await ChargedAsync();
        var hook = new BetweenQueriesHook(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(5m, version, "rc-winner"), S.Actor, default)), 1);
        Assert.IsType<ConcurrencyConflictException>(await Record.ExceptionAsync(() => WithHookedAsync(hook, s => s.RecordPaymentAsync(Ann, PaymentOf(7m, version, "rc-loser"), S.Actor, default))));

        var fresh = await FreshVersionAsync();
        var retried = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(7m, fresh, "rc-loser"), S.Actor, default));
        var again = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(7m, fresh, "rc-loser"), S.Actor, default));   // the same request again is quiet
        Assert.Equal(2, retried.Entries.Count(e => e.Kind == BillingKinds.Payment));
        Assert.Equal(2, again.Entries.Count(e => e.Kind == BillingKinds.Payment));
        Assert.Equal(88m, again.BalanceOwed);
        Assert.Empty((await Bill(s => s.VerifyLedgerAsync(Ann, default))).Findings);
    }

    [Fact]
    public async Task A_reader_that_meets_a_payment_mid_read_repeats_the_read_and_returns_one_coherent_account()
    {
        var (version, _) = await ChargedAsync();
        var hook = new BetweenQueriesHook(() => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(5m, version, "rc-winner"), S.Actor, default)), 1);

        var view = await WithHookedAsync(hook, s => s.GetAccountAsync(Ann, default));

        Assert.Equal(1, hook.Fired);
        Assert.Single(view.Entries, e => e.Kind == BillingKinds.Payment);                                 // the repeated read includes the payment and its allocation together
        Assert.Equal(95m, view.BalanceOwed);
        Assert.Empty((await Bill(s => s.VerifyLedgerAsync(Ann, default))).Findings);
    }

    [Fact]
    public async Task A_reader_that_never_gets_a_stable_picture_is_refused_as_a_conflict_after_the_capped_attempts()
    {
        await ChargedAsync();
        var n = 0;
        var hook = new BetweenQueriesHook(async () => { var v = await FreshVersionAsync(); await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(1m, v, $"rc-storm-{++n}"), S.Actor, default)); }, 100);

        var refused = await Record.ExceptionAsync(() => WithHookedAsync(hook, s => s.GetAccountAsync(Ann, default)));

        Assert.IsType<ConcurrencyConflictException>(refused);
        Assert.Equal(4, hook.Fired);                                                                       // the attempts are capped, never an endless loop
        Assert.Empty((await Bill(s => s.VerifyLedgerAsync(Ann, default))).Findings);
        Assert.Equal(96m, (await Bill(s => s.GetAccountAsync(Ann, default))).BalanceOwed);                 // the charge of 100 less the four payments of 1 made in the gaps
    }

    [Fact]
    public async Task An_account_nobody_has_billed_yet_reads_as_empty_without_the_version_check_failing()
    {
        var view = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Empty(view.Entries);
    }
}
