using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 5b: the rows step of a commit for an opening-balance batch. Like the appointment commit it does not stage on the background runner's transaction: the ledger has its own guarded save, so each row is
/// recorded in a scope of its own (<see cref="IImportWorkScopeFactory"/>) and its result is saved there straight after. The ledger entry carries the key <c>import-bal-{batch}-{row}</c>, so recording a row again returns the
/// entry the first time made and never adds a second (a crash between the entry and the row simply repeats and finds it). For each row of the window that is still Valid, in order:
/// 1. the row is read again as the dry-run read it (<see cref="ImportBalanceEvaluator.ResolveRowsAsync"/>); a problem now (a patient renamed, made inactive or merged) rejects the row;
/// 2. an entry already under this row's key is the row's result (Accepted); otherwise <see cref="BillingService.RecordOpeningBalanceAsync"/> checks as of now that the account is still empty: a history created since the
///    dry-run rejects the row (the billing rule's own code and a fixed message), the balance is never added to it;
/// 3. a concurrency conflict or an unavailable save (503) is not a verdict about the row: it is thrown, the job is retried and the row stays Valid.
/// Failure modes handled: a patient changed or an account used since the dry-run, a replayed row, a busy ledger. No value from the file is ever written to a message.
/// </summary>
internal sealed class ImportBalanceCommit(IImportWorkScopeFactory scopes, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock)
{
    private readonly ImportOptions _options = options.Value;

    public static string EntryKey(Guid batchId, int rowNumber) => $"import-bal-{batchId:N}-{rowNumber}";

    public async Task RowsAsync(ImportCommitJob job, ImportBatch batch, CancellationToken ct)
    {
        var actor = batch.CommitConfirmedByUserId ?? throw new InvalidOperationException("A committing batch records who confirmed it.");
        List<int> numbers;
        await using (var read = scopes.Create())
            numbers = await read.Db.ImportBatchRows.AsNoTracking().Where(r => r.BatchId == batch.Id && r.RowNumber >= job.FromRow && r.RowNumber <= job.ToRow && r.Status == ImportRowStatus.Valid)
                .OrderBy(r => r.RowNumber).Select(r => r.RowNumber).ToListAsync(ct);
        if (numbers.Count == 0) return;                                                  // already done (a replay) or nothing valid in this window

        var table = await ImportPatientRows.ReadTableAsync(files, batch, _options, ct);
        var mapping = JsonSerializer.Deserialize<Dictionary<string, string>>(batch.MappingJson ?? "{}") ?? [];
        List<ImportBalanceEvaluator.ResolvedBalance> resolved;
        await using (var read = scopes.Create())
            resolved = await new ImportBalanceEvaluator(read.Db, clock).ResolveRowsAsync(table, mapping, numbers.ToHashSet(), ct);

        foreach (var row in resolved) await CommitRowAsync(batch.Id, row, actor, ct);
    }

    private async Task CommitRowAsync(Guid batchId, ImportBalanceEvaluator.ResolvedBalance resolved, Guid actor, CancellationToken ct)
    {
        await using var work = scopes.Create();
        var db = work.Db;
        var stillValid = await db.ImportBatchRows.AsNoTracking().AnyAsync(r => r.BatchId == batchId && r.RowNumber == resolved.RowNumber && r.Status == ImportRowStatus.Valid, ct);
        if (!stillValid) return;                                                         // settled by an earlier attempt

        string status, reason = "";
        IReadOnlyDictionary<string, string>? errors = null;
        Guid? entryId = null;
        if (resolved.PatientId is not Guid patientId)
        {
            (status, reason, errors) = (ImportRowStatus.Rejected, ImportRowReason.InvalidFields, resolved.Errors);
        }
        else
        {
            var key = EntryKey(batchId, resolved.RowNumber);
            try
            {
                entryId = await work.Billing.RecordOpeningBalanceAsync(patientId, resolved.Amount, key, batchId, actor, ct);
                status = ImportRowStatus.Accepted;
            }
            catch (BillingException ex) when (ex.StatusCode != 503)
            {
                (status, reason, errors) = (ImportRowStatus.Rejected, ex.Code == "account_not_empty" ? ImportRowReason.AccountNotEmpty : ImportRowReason.InvalidFields,
                    new Dictionary<string, string> { ["balance"] = ex.Message });
            }
            // a concurrency conflict or an unavailable save (503) is not caught: it is no verdict about the row, so the job is retried and the row stays Valid
        }

        db.ChangeTracker.Clear();                                                         // the billing save may have left tracked entries: the row is read afresh, never held across it
        var row = await db.ImportBatchRows.SingleAsync(r => r.BatchId == batchId && r.RowNumber == resolved.RowNumber, ct);
        row.Status = status;
        row.Reason = reason.Length == 0 ? null : reason;
        row.ErrorsJson = errors is null ? null : JsonSerializer.Serialize(errors);
        row.ResultRecordId = entryId;
        row.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);                                                    // this row's result commits by itself, apart from every other row
    }
}
