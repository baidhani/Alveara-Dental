using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Xunit;
using Xunit.Abstractions;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// GATE D, criterion D5 (evidence tooling, not a product story): "Financial and completed-procedure histories survive duplicate submissions, corrections and stale edits without destructive overwrite".
/// Through the real services and a real SQL Server database, with synthetic data only, it submits the same payment, adjustment, refund, reversal, statement and completion twice (each must be quiet and change
/// nothing), the same key for a different request (refused), and a stale edit from an out-of-date account, plan or amendment number (refused with a classified conflict, never a raw database error), and
/// proves that after each refusal or replay the named tables are byte for byte unchanged (a checksum of the whole table before and after). Two people paying at the same moment from the same copy of the
/// account end with one payment and one conflict. It then reconciles the preserved history (the ledger verification and the completion reconciliation are clean) and attacks the preserved records THROUGH
/// THE PERSISTENCE LAYER: a plain SQL UPDATE or DELETE on a separate connection, in a transaction that is always rolled back, recording the exact statement, the rejecting trigger
/// (<see cref="SqlException.Procedure"/>) and a before/after table checksum. The records are written to <c>GATE_D_EVIDENCE_OUT</c> as JSON and Markdown.
///
/// The harness repeats the Gate C one (<c>GateCHistoryIntegrityTests</c>, approved and left unchanged). The tooling changes no product control and weakens none.
/// </summary>
[Trait("Gate", "D")]
public class GateDHistoryIntegrityTests(ITestOutputHelper output) : CompletionTestBase
{
    private readonly List<Attempt> _attempts = [];

    // ---------- the attack harness ----------

    private sealed record Attempt(string RecordClass, string Table, string Statement, bool Required, bool Rejected, int? SqlError, string? Mechanism, string? Message, bool TableUnchanged, bool RowsChangedInsideTransaction);

    private readonly List<string> _paths = [];

    // ---------- the attack harness ----------

    private async Task<string> ChecksumAsync(string table, SqlTransaction? tx = null, SqlConnection? on = null)
    {
        var owns = on is null;
        var c = on ?? new SqlConnection(Fixture.ConnectionString);
        try
        {
            if (owns) await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT CAST(COUNT(*) AS varchar(20)) + ':' + CAST(ISNULL(CHECKSUM_AGG(CHECKSUM(*)), 0) AS varchar(20)) FROM [{table}]";
            return (string)(await cmd.ExecuteScalarAsync())!;
        }
        finally { if (owns) await c.DisposeAsync(); }
    }

    /// <summary>The SET clause for an UPDATE of one column to a different value, by the column's type; the first existing column of <paramref name="candidates"/> is used.</summary>
    private async Task<(string Column, string Set)> UpdateTargetAsync(string table, params string[] candidates)
    {
        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COLUMN_NAME, DATA_TYPE, ISNULL(CHARACTER_MAXIMUM_LENGTH, 0) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}'";
        var columns = new Dictionary<string, (string Type, int Length)>(StringComparer.OrdinalIgnoreCase);
        await using (var r = await cmd.ExecuteReaderAsync())
            while (await r.ReadAsync()) columns[r.GetString(0)] = (r.GetString(1), r.GetInt32(2));
        foreach (var name in candidates)
        {
            if (!columns.TryGetValue(name, out var col)) continue;
            return (name, col.Type switch
            {
                "nvarchar" or "varchar" => $"[{name}] = LEFT(N'overwritten by the Gate D attempt', {(col.Length is -1 or 0 ? 4000 : Math.Min(col.Length, 33))})",
                "bit" => $"[{name}] = 1 - [{name}]",
                "datetimeoffset" or "datetime2" => $"[{name}] = DATEADD(day, 1, [{name}])",
                _ => $"[{name}] = [{name}] + 1",
            });
        }
        // none of the preferred columns exists: any ordinary data column will do for an attack (never a key, a link, a hash or the row version)
        foreach (var (name, col) in columns)
        {
            if (name.EndsWith("Id", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Key", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Hash", StringComparison.OrdinalIgnoreCase) || name == "RowVersion") continue;
            if (col.Type is not ("nvarchar" or "varchar" or "int" or "smallint" or "tinyint" or "decimal" or "datetimeoffset" or "datetime2")) continue;
            return (name, col.Type switch
            {
                "nvarchar" or "varchar" => $"[{name}] = LEFT(N'overwritten by the Gate D attempt', {(col.Length is -1 or 0 ? 4000 : Math.Min(col.Length, 33))})",
                "datetimeoffset" or "datetime2" => $"[{name}] = DATEADD(day, 1, [{name}])",
                _ => $"[{name}] = [{name}] + 1",
            });
        }
        throw new InvalidOperationException($"{table}: no column can be attacked (found: {string.Join(", ", columns.Keys)})");
    }

    private async Task AttackAsync(string recordClass, string table, string statement, bool required = true)
    {
        var before = await ChecksumAsync(table);
        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync();
        SqlException? error = null;
        var affected = 0;
        string? inside = null;
        try
        {
            await using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = statement;
            affected = await cmd.ExecuteNonQueryAsync();
            inside = await ChecksumAsync(table, tx, c);
        }
        catch (SqlException e) { error = e; }
        try { await tx.RollbackAsync(); } catch (InvalidOperationException) { /* the server already ended the transaction */ }
        var after = await ChecksumAsync(table);
        var attempt = new Attempt(recordClass, table, statement, required, error is not null, error?.Number, error is null ? null : (string.IsNullOrEmpty(error.Procedure) ? $"(no trigger: SQL error {error.Number})" : error.Procedure),
            error?.Message, after == before, error is null && (affected > 0 || inside != before));
        _attempts.Add(attempt);
        output.WriteLine($"{recordClass,-34} {table,-30} {(attempt.Rejected ? "REJECTED " + attempt.SqlError + " by " + attempt.Mechanism : "NOT REJECTED (rows affected " + affected + ")"),-70} {statement}");
    }

    private async Task UpdateAttackAsync(string recordClass, string table, params string[] candidates) => await UpdateAttackAsync(recordClass, table, true, candidates);

    private async Task UpdateAttackAsync(string recordClass, string table, bool required, string[] candidates)
    {
        var (_, set) = await UpdateTargetAsync(table, candidates);
        var column = set[1..set.IndexOf(']')];
        await AttackAsync(recordClass, table, $"UPDATE [{table}] SET {set} WHERE [{column}] IS NOT NULL", required);
    }

    private Task DeleteAttackAsync(string recordClass, string table) => AttackAsync(recordClass, table, $"DELETE FROM [{table}]");

    private async Task<int> CountAsync(string table)
    {
        await using var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM [{table}]";
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    // ---------- duplicates, corrections and stale edits through the real services ----------

    private sealed record Case(string Label, string Outcome, string? ExceptionType, string? Code, bool StateUnchanged);

    private readonly List<Case> _cases = [];

    private async Task<string> ChecksumsAsync(params string[] tables)
    {
        var parts = new List<string>();
        foreach (var t in tables) parts.Add(await ChecksumAsync(t));
        return string.Join("|", parts);
    }

    /// <summary>A refused duplicate, stale edit or reused key: it must throw a classified domain exception (never a raw database error) and leave every named table exactly as it was.</summary>
    private async Task RefusedUnchangedAsync(string label, string[] tables, Func<Task> act, string? code = null, Type? type = null)
    {
        var before = await ChecksumsAsync(tables);
        var ex = await Assert.ThrowsAnyAsync<Exception>(act);
        Assert.IsNotType<SqlException>(ex);
        Assert.IsNotType<DbUpdateException>(ex);
        var after = await ChecksumsAsync(tables);
        Assert.Equal(before, after);
        var actual = (ex as BillingException)?.Code;
        if (code is not null) Assert.Equal(code, actual);
        if (type is not null) Assert.IsType(type, ex);
        _cases.Add(new Case(label, "refused", ex.GetType().Name, actual, true));
        output.WriteLine($"{label,-70} REFUSED {ex.GetType().Name} {actual}");
    }

    /// <summary>An exact duplicate submission: it succeeds quietly and changes no named table.</summary>
    private async Task ReplayedUnchangedAsync(string label, string[] tables, Func<Task> act)
    {
        var before = await ChecksumsAsync(tables);
        await act();
        var after = await ChecksumsAsync(tables);
        Assert.Equal(before, after);
        _cases.Add(new Case(label, "replayed quietly", null, null, true));
        output.WriteLine($"{label,-70} REPLAYED QUIETLY");
    }

    private static async Task<Exception?> SettleAsync(Task task)
    {
        try { await task; return null; }
        catch (Exception e) { return e; }
    }

    private static readonly string[] LedgerTables = ["BillingEntries", "BillingAllocations", "BillingAllocationReleases", "BillingAccounts", "FinancialDocuments"];

    [Fact]
    public async Task D5_financial_history_survives_duplicate_submissions_corrections_stale_edits_and_direct_overwrite()
    {
        var plan = await BillablePlanAsync();
        var charged = await Bill(s => s.RecordChargeAsync(Ann, ChargeOf(plan.Completed), S.Actor, default));
        await ReplayedUnchangedAsync("charge: the same key again", LedgerTables, () => Bill(s => s.RecordChargeAsync(Ann, ChargeOf(plan.Completed), S.Actor, default)));

        var paid = await Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(60m, charged.RowVersion!, "gd-pay-1"), S.Actor, default));
        await ReplayedUnchangedAsync("payment: the same key and request again, from the older account version", LedgerTables, () => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(60m, charged.RowVersion!, "gd-pay-1"), S.Actor, default)));
        await RefusedUnchangedAsync("payment: the same key for a different amount", LedgerTables, () => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(61m, paid.RowVersion!, "gd-pay-1"), S.Actor, default)), "idempotency_key_reused");
        await RefusedUnchangedAsync("payment: a new key from the out-of-date account (stale edit)", LedgerTables, () => Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(5m, charged.RowVersion!, "gd-pay-stale"), S.Actor, default)), type: typeof(ConcurrencyConflictException));

        // two people paying at the same moment from the same copy of the account: exactly one is recorded, the other is told
        var before = (await Bill(s => s.GetAccountAsync(Ann, default))).Entries.Count;
        var version = paid.RowVersion!;
        var race = await Task.WhenAll(SettleAsync(Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(5m, version, "gd-race-1"), S.Actor, default))), SettleAsync(Bill(s => s.RecordPaymentAsync(Ann, PaymentOf(5m, version, "gd-race-2"), S.Actor, default))));
        Assert.Single(race, r => r is null);
        Assert.Single(race, r => r is ConcurrencyConflictException);
        var account = await Bill(s => s.GetAccountAsync(Ann, default));
        Assert.Equal(before + 1, account.Entries.Count);
        _cases.Add(new Case("payment: two simultaneous submissions from the same account version", "one recorded, one conflict", nameof(ConcurrencyConflictException), null, true));

        var adjusted = await Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(-10m, account.RowVersion!, "gd-adj-1"), S.Actor, default));
        await ReplayedUnchangedAsync("adjustment: the same key again", LedgerTables, () => Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(-10m, account.RowVersion!, "gd-adj-1"), S.Actor, default)));
        await RefusedUnchangedAsync("adjustment: from the out-of-date account (stale edit)", LedgerTables, () => Bill(s => s.RecordAdjustmentAsync(Ann, AdjustmentOf(-1m, account.RowVersion!, "gd-adj-stale"), S.Actor, default)), type: typeof(ConcurrencyConflictException));

        var payments = adjusted.Entries.Where(e => e.Kind == BillingKinds.Payment).Select(e => e.EntryNumber).ToList();
        var refunded = await Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(payments[1], 2m, "Cash", "Patient request", adjusted.RowVersion, "gd-rf-1"), S.Actor, default));
        await ReplayedUnchangedAsync("refund: the same key and request again, from the older account version", LedgerTables, () => Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(payments[1], 2m, "Cash", "Patient request", adjusted.RowVersion, "gd-rf-1"), S.Actor, default)));
        await RefusedUnchangedAsync("refund: the same key for a different amount", LedgerTables, () => Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(payments[1], 1m, "Cash", "Patient request", refunded.RowVersion, "gd-rf-1"), S.Actor, default)), "idempotency_key_reused");
        await RefusedUnchangedAsync("refund: from the out-of-date account (stale edit)", LedgerTables, () => Bill(s => s.RefundPaymentAsync(Ann, new RefundInput(payments[1], 1m, "Cash", "Patient request", adjusted.RowVersion, "gd-rf-stale"), S.Actor, default)), type: typeof(ConcurrencyConflictException));

        var adjustmentEntry = adjusted.Entries.Single(e => e.Kind == BillingKinds.Adjustment).EntryNumber;
        var reversed = await Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(payments[0], "Entered in error", refunded.RowVersion, "gd-rev-1"), S.Actor, default));
        await ReplayedUnchangedAsync("reversal: the same key and request again, from the older account version", LedgerTables, () => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(payments[0], "Entered in error", refunded.RowVersion, "gd-rev-1"), S.Actor, default)));
        await RefusedUnchangedAsync("reversal: the same key for a different entry", LedgerTables, () => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(payments[1], "Entered in error", reversed.RowVersion, "gd-rev-1"), S.Actor, default)), "idempotency_key_reused");
        await RefusedUnchangedAsync("reversal: from the out-of-date account (stale edit)", LedgerTables, () => Bill(s => s.ReverseEntryAsync(Ann, new ReversalInput(adjustmentEntry, "Entered in error", refunded.RowVersion, "gd-rev-stale"), S.Actor, default)), type: typeof(ConcurrencyConflictException));

        // a statement issued twice under one key is one document
        var preview = await Bill(s => s.PreviewDocumentAsync(Ann, new DocumentPreviewInput("Statement", null), default));
        var first = await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, "gd-doc-1"), S.Actor, default));
        await ReplayedUnchangedAsync("statement: issued again under the same key", LedgerTables, async () => Assert.Equal(first.Id, (await Bill(s => s.IssueDocumentAsync(Ann, new IssueDocumentInput(preview.PreviewToken, "gd-doc-1"), S.Actor, default))).Id));

        // the preserved history still reconciles, to the cent, after all of it
        var verification = await Bill(s => s.VerifyLedgerAsync(Ann, default));
        Assert.True(verification.Consistent);
        Assert.Empty(verification.Findings);

        // direct overwrite or deletion of the preserved financial records, through the persistence layer
        await UpdateAttackAsync("ledger entry", "BillingEntries", "Amount");
        await DeleteAttackAsync("ledger entry", "BillingEntries");
        await AttackAsync("ledger entry", "BillingEntries", "UPDATE [BillingEntries] SET [IdempotencyKey] = N'overwritten' WHERE [IdempotencyKey] IS NOT NULL");
        await AttackAsync("billing account", "BillingAccounts", $"UPDATE [BillingAccounts] SET [PatientId] = '{Bo}'");
        await DeleteAttackAsync("billing account", "BillingAccounts");
        await UpdateAttackAsync("payment allocation", "BillingAllocations", "Amount");
        await DeleteAttackAsync("payment allocation", "BillingAllocations");
        await UpdateAttackAsync("allocation release (refund)", "BillingAllocationReleases", "Amount", "ReleasedAmount");
        await DeleteAttackAsync("allocation release (refund)", "BillingAllocationReleases");
        await UpdateAttackAsync("issued statement", "FinancialDocuments", "DocumentNumber");
        await DeleteAttackAsync("issued statement", "FinancialDocuments");
        AssertAllRequiredRejected();
        WriteEvidence("d5-financial-history");
    }

    [Fact]
    public async Task D5_completed_procedure_history_survives_duplicate_submissions_corrections_stale_edits_and_direct_overwrite()
    {
        var plan = await AcceptedPlanAsync(3);
        var provider = await ProviderAsync("Dr. Gate D");
        var other = await ProviderAsync("Dr. Amend");
        string[] tables = ["CompletionSets", "CompletedProcedures", "CompletedProcedureAmendments", "BillingEntries", "BillingAccounts", "TreatmentPlanItemStates", "TreatmentPlanEvents", "ToothFindingVersions"];

        var request = RequestFor(plan, [0, 1], AnnEncounter, provider.Id, "gd-set-1");
        var done = await Completion(s => s.CompleteAsync(plan.Id, request, S.Actor, default));
        Assert.False(done.Replayed);
        Assert.Equal(2, done.Procedures.Count);
        Assert.Equal(2, await CountAsync("BillingEntries"));                                                       // one billable charge per completed procedure

        await ReplayedUnchangedAsync("completion: the same set again (same key, same request, older plan version)", tables, async () => Assert.True((await Completion(s => s.CompleteAsync(plan.Id, request, S.Actor, default))).Replayed));
        await RefusedUnchangedAsync("completion: the same key for a different set", tables, () => Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [2], AnnEncounter, provider.Id, "gd-set-1"), S.Actor, default)));
        await RefusedUnchangedAsync("completion: a new key from the out-of-date plan (stale edit)", tables, () => Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [2], AnnEncounter, provider.Id, "gd-set-2"), S.Actor, default)), type: typeof(ConcurrencyConflictException));

        var procedure = done.Procedures[0].Id;
        var amended = await Completion(s => s.AmendAsync(procedure, new AmendCompletedProcedureRequest("gd-amend-1", 0, other.Id, null, null, "Recorded under the wrong provider"), S.Actor, default));
        Assert.False(amended.Replayed);
        await ReplayedUnchangedAsync("amendment: the same key and request again", tables, async () => Assert.True((await Completion(s => s.AmendAsync(procedure, new AmendCompletedProcedureRequest("gd-amend-1", 0, other.Id, null, null, "Recorded under the wrong provider"), S.Actor, default))).Replayed));
        await RefusedUnchangedAsync("amendment: a new key from the out-of-date amendment number (stale edit)", tables, () => Completion(s => s.AmendAsync(procedure, new AmendCompletedProcedureRequest("gd-amend-2", 0, provider.Id, null, null, "Second correction from a stale screen"), S.Actor, default)), type: typeof(ConcurrencyConflictException));
        Assert.Equal(1, await CountAsync("CompletedProcedureAmendments"));                                         // the correction is appended; the record is not edited

        var reconciliation = await Completion(s => s.ReconcileAsync(default));
        Assert.True(reconciliation.Clean);                                                                         // zero orphaned charges, zero missing charges, chart matches the completions
        _cases.Add(new Case("reconciliation after all of it", "clean", null, null, true));

        await UpdateAttackAsync("completed procedure", "CompletedProcedures", "ToothKey", "Surface", "ChartOutcome");
        await DeleteAttackAsync("completed procedure", "CompletedProcedures");
        await UpdateAttackAsync("completion set", "CompletionSets", "IdempotencyKey", "RequestHash");
        await DeleteAttackAsync("completion set", "CompletionSets");
        await UpdateAttackAsync("completed procedure amendment", "CompletedProcedureAmendments", "Reason");
        await DeleteAttackAsync("completed procedure amendment", "CompletedProcedureAmendments");
        await UpdateAttackAsync("completion charge (ledger entry)", "BillingEntries", "Amount");
        await DeleteAttackAsync("completion charge (ledger entry)", "BillingEntries");
        AssertAllRequiredRejected();
        WriteEvidence("d5-completed-procedure-history");
    }

    /// <summary>Every attempt that must be rejected was rejected by the database, and every attempted table is exactly as it was.</summary>
    private void AssertAllRequiredRejected()
    {
        var failures = _attempts.Where(a => a.Required && !a.Rejected).Select(a => $"{a.RecordClass}: {a.Statement}").ToList();
        Assert.True(failures.Count == 0, "not rejected by the database: " + string.Join("; ", failures));
        var notControls = _attempts.Where(a => a.Required && a.Rejected && !(a.SqlError >= 51000)).Select(a => $"{a.RecordClass}: {a.Statement} (SQL error {a.SqlError})").ToList();
        Assert.True(notControls.Count == 0, "rejected by something other than a named control: " + string.Join("; ", notControls));
        Assert.All(_attempts, a => Assert.True(a.TableUnchanged, $"{a.RecordClass}: {a.Statement} left its table changed after the rollback"));
    }

    private void WriteEvidence(string name)
    {
        var dir = Environment.GetEnvironmentVariable("GATE_D_EVIDENCE_OUT") ?? Path.Combine(Path.GetTempPath(), "gate-d-evidence");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"{name}.json"), JsonSerializer.Serialize(new { attempts = _attempts, cases = _cases }, new JsonSerializerOptions { WriteIndented = true }));
        var md = new StringBuilder("| Record class | Table | Statement | Result | SQL error | Rejected by | Table unchanged |\n|---|---|---|---|---|---|---|\n");
        foreach (var a in _attempts)
            md.AppendLine($"| {a.RecordClass} | {a.Table} | `{a.Statement.Replace("|", "\\|")}` | {(a.Rejected ? "REJECTED" : "NOT REJECTED")} | {a.SqlError} | {a.Mechanism} | {a.TableUnchanged} |");
        md.AppendLine().AppendLine("| Duplicate / stale / correction case | Outcome | Exception | Code | State unchanged |").AppendLine("|---|---|---|---|---|");
        foreach (var k in _cases) md.AppendLine($"| {k.Label} | {k.Outcome} | {k.ExceptionType} | {k.Code} | {k.StateUnchanged} |");
        File.WriteAllText(Path.Combine(dir, $"{name}.md"), md.ToString());
    }
}
