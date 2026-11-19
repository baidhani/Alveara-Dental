using System.Data;
using Alveara.Api.Architecture.Billing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N024 CR-1 (briefing revision 2 section 6, proofs 1, 2, 4 and 5; approved by BRIEFING-R02) against real SQL Server, through the service with truly concurrent callers (two contexts released together
/// by a barrier, 20 rounds each):
/// - two first receipts of one payment with different keys: exactly one is issued, the other is refused <c>receipt_exists</c> (or <c>receipt_busy</c>);
/// - two replacements of the same receipt: exactly one, the other <c>receipt_replaced</c> (or <c>receipt_busy</c>);
/// - the same key twice at once: one document and one audit row, both callers get it;
/// - a receipt that cannot get its payment's lock in time: 503 <c>receipt_busy</c>, nothing written;
/// - after every round no receipt lock is left held (a fresh transaction gets it at once) and refused callers left no document or audit row.
/// </summary>
public class BillingReceiptConcurrencyTests : BillingTestBase
{
    private const int Rounds = 20;

    private Task<DocumentPreview> Preview(int payment, Guid? replaces = null) => Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Receipt", payment, replaces), default));
    private Task<IssuedDocumentView> Issue(DocumentPreview preview, string key, string? reason = null) =>
        Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, key, reason), S.Actor, default));

    /// <summary>Charge 1 is 100; payments 2 .. Rounds + 1 are 1 each.</summary>
    private async Task SmallPaymentsAsync()
    {
        var plan = await BillablePlanAsync();
        var account = await Bill(s => s.RecordChargeAsync(Ann, new ChargeInput(plan.Completed, "c-1"), S.Actor, default));
        for (var i = 1; i <= Rounds; i++)
            account = await Bill(s => s.RecordPaymentAsync(Ann, new PaymentInput(1m, "Cash", null, account.RowVersion, $"p-{i}"), S.Actor, default));
    }

    /// <summary>
    /// Runs both calls at the same moment, each on its own context, and returns what each did: the document, the service's refusal code, or - when the service did not refuse and the database did (a deadlock
    /// victim, a guard, a lock timeout) - <c>database:</c> and the exception's type. Capturing it keeps the round going, so every round checks the receipt count, the audit count and the lock BEFORE the
    /// refusal code (ALV-N024 negative control (e): with the service lock removed the database still refuses, and the invariants are still checked).
    /// </summary>
    private static async Task<(IssuedDocumentView? Doc, string? Refused)[]> TogetherAsync(Func<Task<IssuedDocumentView>> a, Func<Task<IssuedDocumentView>> b)
    {
        using var barrier = new Barrier(2);
        async Task<(IssuedDocumentView?, string?)> Run(Func<Task<IssuedDocumentView>> call)
        {
            barrier.SignalAndWait(TimeSpan.FromSeconds(10));
            try { return (await call(), null); }
            catch (BillingException e) { return (null, e.Code); }
            catch (Exception e) when (e is Microsoft.EntityFrameworkCore.DbUpdateException or InvalidOperationException or SqlException or Alveara.Api.Architecture.Concurrency.ConcurrencyConflictException) { return (null, $"database:{e.GetType().Name}"); }
        }
        return await Task.WhenAll(Task.Run(() => Run(a)), Task.Run(() => Run(b)));
    }

    private Task<int> ReceiptsAsync(int payment) => WithDb(db => db.FinancialDocuments.CountAsync(d => db.BillingEntries.Any(e => e.Id == d.PaymentEntryId && e.EntryNumber == payment && e.PatientId == Ann)));
    private Task<int> AuditCountAsync(string type) => WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == type));

    /// <summary>True when a fresh transaction gets the payment's receipt lock at once: nobody is holding it.</summary>
    private async Task<bool> LockIsFreeAsync(int payment)
    {
        var id = await WithDb(db => db.BillingEntries.Where(e => e.PatientId == Ann && e.EntryNumber == payment).Select(e => e.Id).SingleAsync());
        await using var connection = new SqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0; SELECT @r;";
        command.Parameters.AddWithValue("@resource", $"alveara.receipt:{id:N}");
        var granted = (int)(await command.ExecuteScalarAsync())! >= 0;
        await transaction.RollbackAsync();
        return granted;
    }

    [Fact]
    public async Task Two_first_receipts_of_one_payment_at_the_same_moment_issue_exactly_one_every_time()
    {
        await SmallPaymentsAsync();
        for (var round = 1; round <= Rounds; round++)
        {
            var payment = round + 1;
            var (a, b) = (await Preview(payment), await Preview(payment));                                   // both previewed while there is no receipt yet
            var issuedBefore = await AuditCountAsync("FinancialDocumentIssued");
            var results = await TogetherAsync(() => Issue(a, $"a-{round}"), () => Issue(b, $"b-{round}"));
            Assert.Equal(1, await ReceiptsAsync(payment));                                                        // the invariants first, whoever refused the loser
            Assert.Equal(issuedBefore + 1, await AuditCountAsync("FinancialDocumentIssued"));                     // the refused caller left no audit row
            Assert.True(await LockIsFreeAsync(payment), $"round {round}: the receipt lock is still held");
            Assert.Single(results, r => r.Doc is not null);
            Assert.Contains(results.Single(r => r.Doc is null).Refused, new[] { "receipt_exists", "receipt_busy" });   // then: the service itself refused it
        }
        Assert.Equal(Enumerable.Range(1, Rounds), (await Bill(s => s.ListDocumentsAsync(Ann, default))).Select(d => d.DocumentNumber).Order());   // numbers stay 1, 2, 3 ... with no gap
    }

    [Fact]
    public async Task Two_replacements_of_the_same_receipt_at_the_same_moment_make_exactly_one_every_time()
    {
        await SmallPaymentsAsync();
        for (var round = 1; round <= Rounds; round++)
        {
            var payment = round + 1;
            var original = await Issue(await Preview(payment), $"o-{round}");
            var (a, b) = (await Preview(payment, original.Id), await Preview(payment, original.Id));
            var results = await TogetherAsync(() => Issue(a, $"ra-{round}", "Reprint for the insurer"), () => Issue(b, $"rb-{round}", "Name corrected"));
            Assert.Equal(2, await ReceiptsAsync(payment));                                                        // the invariants first
            Assert.Equal(round, await AuditCountAsync("FinancialDocumentReplaced"));
            Assert.True(await LockIsFreeAsync(payment), $"round {round}: the receipt lock is still held");
            Assert.Single(results, r => r.Doc is not null);
            Assert.Contains(results.Single(r => r.Doc is null).Refused, new[] { "receipt_replaced", "receipt_busy" });
        }
        Assert.Equal(Rounds, await AuditCountAsync("FinancialDocumentReplaced"));
    }

    [Fact]
    public async Task The_same_issue_twice_at_the_same_moment_stores_one_receipt_and_both_callers_get_it()
    {
        await SmallPaymentsAsync();
        for (var round = 1; round <= Rounds; round++)
        {
            var payment = round + 1;
            var preview = await Preview(payment);
            var results = await TogetherAsync(() => Issue(preview, $"same-{round}"), () => Issue(preview, $"same-{round}"));
            Assert.Equal(1, await ReceiptsAsync(payment));                                                        // the invariants first
            Assert.Equal(round, await AuditCountAsync("FinancialDocumentIssued"));
            Assert.True(await LockIsFreeAsync(payment), $"round {round}: the receipt lock is still held");
            Assert.All(results, r => Assert.NotNull(r.Doc));
            Assert.Single(results.Select(r => r.Doc!.Id).Distinct());
        }
        Assert.Equal(Rounds, await AuditCountAsync("FinancialDocumentIssued"));
    }

    [Fact]
    public async Task A_receipt_that_cannot_get_its_payments_lock_in_time_is_refused_busy_and_writes_nothing()
    {
        await SmallPaymentsAsync();
        var preview = await Preview(2);
        var id = await WithDb(db => db.BillingEntries.Where(e => e.PatientId == Ann && e.EntryNumber == 2).Select(e => e.Id).SingleAsync());
        await using (var holder = new SqlConnection(Fixture.ConnectionString))
        {
            await holder.OpenAsync();
            await using var held = (SqlTransaction)await holder.BeginTransactionAsync();
            await using var take = holder.CreateCommand();
            take.Transaction = held;
            take.CommandText = "EXEC sp_getapplock @Resource = @resource, @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 0;";
            take.Parameters.AddWithValue("@resource", $"alveara.receipt:{id:N}");
            await take.ExecuteNonQueryAsync();

            var refused = await Assert.ThrowsAnyAsync<Exception>(() => WithDb(db => new BillingService(db, SchedulingTestSupport.Clock) { ReceiptLockTimeoutMilliseconds = 200 }
                .IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, "busy-1"), S.Actor, default)));
            Assert.Equal((0, 0), (await ReceiptsAsync(2), await AuditCountAsync("FinancialDocumentIssued")));   // nothing written, whoever refused it
            var busy = Assert.IsType<BillingException>(refused);
            Assert.Equal(("receipt_busy", 503), (busy.Code, busy.StatusCode));
            await held.RollbackAsync();
        }
        Assert.True(await LockIsFreeAsync(2));
        Assert.Equal(1, (await Issue(preview, "busy-1")).DocumentNumber);                                  // the same request, once the lock is free
    }
}
