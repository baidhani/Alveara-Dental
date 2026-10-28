using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Imports;

public sealed record ImportReasonCount(string Status, string? Reason, int Rows);

public sealed record ImportReconciliationProblem(string Code, string Message);

/// <summary>
/// Everything a batch did, counted from the rows and from the records themselves, with no value from the file. <see cref="Reconciled"/> is true only when <see cref="Problems"/> is empty.
/// <see cref="RecordsFound"/> is how many Accepted rows point at a record that really exists (for balances: a ledger entry of this batch); <see cref="RecordsMadeByBatch"/> is how many records carry this batch's keys.
/// The three amounts are for opening balances only (null otherwise): the dry-run total, the total the batch recorded, and what the ledger really holds for the batch.
/// </summary>
public sealed record ImportReconciliation(
    Guid BatchId, string SourceKind, string Status, string Policy, string? CommitOutcome, int RowCount, int Accepted, int Rejected, int Skipped, int Unresolved, int NotCommitted,
    IReadOnlyList<ImportReasonCount> Reasons, int RecordsFound, int RecordsMadeByBatch, decimal? ValidAmount, decimal? AcceptedAmount, decimal? LedgerTotal, int AuditEvents,
    bool Reconciled, IReadOnlyList<ImportReconciliationProblem> Problems);

/// <summary>
/// ALV-018-C02 slice 6: the two reports an operator reads after a batch. Neither writes anything, and neither contains a value from the file.
/// <b>Reconciliation</b>: the rows are counted by status and reason straight from the database; the records the batch made are counted from their own keys (patients <c>import-{batch}-</c>, appointments <c>import-appt-{batch}-</c>,
/// ledger entries carrying the batch id) and compared with the Accepted rows; a finished batch's stored counters, and for balances its amount, are compared with what is really there. Each difference is a named problem,
/// so "nothing to explain" means exactly that. Checks that only make sense once nothing is running (records made by the batch, stored counters) are skipped while the batch is Committing.
/// <b>Error report</b>: a CSV (row, status, reason, field, message, duplicate of row) of every Rejected, Unresolved and Skipped row, in row order, read in pages so a large file is not loaded at once. The messages are the
/// fixed texts the dry-run stored; a cell that a spreadsheet could read as a formula is neutralised anyway.
/// </summary>
public class ImportReportService(AlveraDbContext db)
{
    private const int PageSize = 1_000;

    private static readonly Dictionary<string, string> ReasonText = new()
    {
        [ImportRowReason.AlreadyRegistered] = "A registered patient is exactly this person.",
        [ImportRowReason.DuplicateInFile] = "An earlier row of this file is the same person.",
        [ImportRowReason.PossibleDuplicate] = "This may be a registered patient; a person must decide.",
        [ImportRowReason.AlreadyScheduled] = "This appointment is already on the schedule.",
        [ImportRowReason.PatientRepeatedInFile] = "An earlier row of this file already carries a balance for this patient.",
        [ImportRowReason.AccountNotEmpty] = "This patient's account already has entries.",
        [ImportRowReason.ReviewedSamePerson] = "A person decided this is a registered patient.",
    };

    private async Task<ImportBatch> RequireAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await db.ImportBatches.AsNoTracking().SingleOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new ImportException("batch_not_found", "There is no import batch with that id.", 404);
        if (batch.DryRunAtUtc is null && batch.Status == ImportBatchStatus.Uploaded) throw new ImportException("dry_run_required", "Run the dry-run first; there is nothing to report yet.", 409);
        return batch;
    }

    public async Task<ImportReconciliation> ReconcileAsync(Guid batchId, CancellationToken ct)
    {
        var batch = await RequireAsync(batchId, ct);
        var groups = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batchId).GroupBy(r => new { r.Status, r.Reason }).Select(g => new { g.Key.Status, g.Key.Reason, Rows = g.Count() }).ToListAsync(ct);
        int Count(string status) => groups.Where(g => g.Status == status).Sum(g => g.Rows);
        var total = groups.Sum(g => g.Rows);
        var (accepted, rejected, skipped, unresolved, notCommitted) = (Count(ImportRowStatus.Accepted), Count(ImportRowStatus.Rejected), Count(ImportRowStatus.Skipped), Count(ImportRowStatus.Unresolved), Count(ImportRowStatus.Valid));

        var accepts = db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batchId && r.Status == ImportRowStatus.Accepted);
        int found, made;
        decimal? ledger = null;
        switch (batch.SourceKind)
        {
            case ImportSourceKind.Patients:
                var patientKey = ImportPatientRows.RegistrationKey(batchId, 0)[..^1];                  // "import-{batch}-"
                found = await accepts.CountAsync(r => db.Patients.Any(p => p.Id == r.ResultRecordId), ct);
                made = await db.Patients.AsNoTracking().CountAsync(p => p.RegistrationKey != null && p.RegistrationKey.StartsWith(patientKey), ct);
                break;
            case ImportSourceKind.Appointments:
                var apptKey = ImportAppointmentCommit.ScheduleKey(batchId, 0)[..^1];                   // "import-appt-{batch}-"
                found = await accepts.CountAsync(r => db.Appointments.Any(a => a.Id == r.ResultRecordId), ct);
                made = await db.Appointments.AsNoTracking().CountAsync(a => a.ScheduleKey != null && a.ScheduleKey.StartsWith(apptKey), ct);
                break;
            default:
                found = await accepts.CountAsync(r => db.BillingEntries.Any(e => e.Id == r.ResultRecordId && e.CorrelationId == batchId), ct);
                made = await db.BillingEntries.AsNoTracking().CountAsync(e => e.CorrelationId == batchId, ct);
                ledger = await db.BillingEntries.AsNoTracking().Where(e => e.CorrelationId == batchId).SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
                break;
        }

        var problems = new List<ImportReconciliationProblem>();
        void Problem(string code, string message) => problems.Add(new ImportReconciliationProblem(code, message));
        var running = batch.Status == ImportBatchStatus.Committing;
        if (total != batch.RowCount) Problem("row_count_mismatch", $"The file has {batch.RowCount} row(s) but {total} have a result.");
        if (found != accepted) Problem("accepted_result_missing", $"{accepted} row(s) are Accepted but only {found} point at a record that exists.");
        if (!running && made != accepted) Problem("unexplained_records", $"{made} record(s) carry this batch's keys but {accepted} row(s) are Accepted.");
        if (batch.Status == ImportBatchStatus.Committed)
        {
            if (notCommitted > 0) Problem("valid_rows_left", $"{notCommitted} row(s) are still waiting to be committed.");
            if ((batch.AcceptedRows, batch.RejectedRows, batch.SkippedRows, batch.UnresolvedRows) != (accepted, rejected, skipped, unresolved))
                Problem("counters_mismatch", "The batch's stored counters differ from its rows.");
            if (ledger is { } held && held != batch.AcceptedAmount) Problem("amount_mismatch", $"The batch recorded {batch.AcceptedAmount:F2} but the ledger holds {held:F2} for it.");
        }

        var audits = await db.AuditLogEntries.AsNoTracking().CountAsync(a => a.CorrelationId == batchId, ct);
        var balances = batch.SourceKind == ImportSourceKind.OpeningBalances;
        return new ImportReconciliation(
            batch.Id, batch.SourceKind, batch.Status, batch.Policy, batch.CommitOutcome, batch.RowCount, accepted, rejected, skipped, unresolved, notCommitted,
            groups.OrderBy(g => g.Status).ThenBy(g => g.Reason).Select(g => new ImportReasonCount(g.Status, g.Reason, g.Rows)).ToList(), found, made,
            balances ? batch.ValidAmount : null, balances ? batch.AcceptedAmount : null, ledger, audits, problems.Count == 0, problems);
    }

    public async Task<byte[]> ErrorReportAsync(Guid batchId, CancellationToken ct)
    {
        await RequireAsync(batchId, ct);
        var text = new StringBuilder("row,status,reason,field,message,duplicate_of_row\r\n");
        string[] statuses = [ImportRowStatus.Rejected, ImportRowStatus.Unresolved, ImportRowStatus.Skipped];
        var after = 1;
        while (true)
        {
            var page = await db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batchId && r.RowNumber > after && statuses.Contains(r.Status)).OrderBy(r => r.RowNumber).Take(PageSize).ToListAsync(ct);
            if (page.Count == 0) break;
            foreach (var row in page)
            {
                var errors = row.ErrorsJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(row.ErrorsJson);
                if (errors is { Count: > 0 })
                    foreach (var (field, message) in errors.OrderBy(e => e.Key, StringComparer.Ordinal)) Line(text, row, field, message);
                else
                    Line(text, row, "", row.Reason is not null && ReasonText.TryGetValue(row.Reason, out var fixedText) ? fixedText : row.Reason ?? "");
            }

            after = page[^1].RowNumber;
        }

        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        return [.. utf8.GetPreamble(), .. utf8.GetBytes(text.ToString())];                // GetBytes never writes the byte-order mark itself; a spreadsheet needs it to read non-ASCII text correctly
    }

    private static void Line(StringBuilder text, ImportBatchRow row, string field, string message) =>
        text.Append(row.RowNumber).Append(',').Append(Cell(row.Status)).Append(',').Append(Cell(row.Reason ?? "")).Append(',').Append(Cell(field)).Append(',').Append(Cell(message)).Append(',')
            .Append(row.DuplicateOfRow?.ToString() ?? "").Append("\r\n");

    /// <summary>One CSV cell: quoted when it holds a comma, quote or line break, and prefixed with an apostrophe when a spreadsheet would read it as a formula.</summary>
    internal static string Cell(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
