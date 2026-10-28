using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Imports;

/// <summary>
/// ALV-018-C02 slice 4b: the rows step of a commit for an appointment batch. Unlike the patient commit it does NOT stage its changes on the background runner's transaction: the scheduler opens its own transaction and takes its
/// own application locks (provider, operatory and patient) to make double-booking impossible under a race, so each row is scheduled in a scope of its own (<see cref="IImportWorkScopeFactory"/>) and its result is saved in that
/// scope straight after. This is the pattern the runner's contract allows for an effect that carries its own idempotency token: the scheduler honours the key <c>import-appt-{batch}-{row}</c>, so scheduling a row again returns the
/// appointment the first time made and never books twice (a crash between scheduling and saving the row simply repeats and finds it). Rows already Accepted, Skipped or Rejected are not touched again.
/// For each row of the window that is still Valid, in order:
/// 1. the row is read and resolved exactly as the dry-run read it (<see cref="ImportAppointmentResolver"/>); a problem now (a provider renamed or deactivated, a patient gone) rejects the row;
/// 2. an appointment already under this row's key is the row's result (Accepted); otherwise an appointment on the schedule exactly as the row says (booked by someone else meanwhile) makes the row Skipped (already_scheduled);
/// 3. otherwise the row goes to <see cref="AppointmentScheduler.ScheduleAsync"/>, which checks again as of now: the start still in the future, the provider working, and no clash of provider, operatory or patient. A refusal
///    (the scheduler's own code and fixed message) rejects the row; it is never double-booked. A busy schedule (503) is not a refusal: it is thrown, the job is retried and the row stays Valid.
/// Failure modes handled: a clash or an unavailable provider created since the dry-run, a start that has passed, a changed reference, a replayed row, a busy schedule. The scheduler records its own refusals in the audit log.
/// No value from the file is ever written to a message.
/// </summary>
internal sealed class ImportAppointmentCommit(IImportWorkScopeFactory scopes, ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock)
{
    private readonly ImportOptions _options = options.Value;

    public static string ScheduleKey(Guid batchId, int rowNumber) => $"import-appt-{batchId:N}-{rowNumber}";

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
        List<ResolutionOutcome> resolved;
        await using (var read = scopes.Create())
            resolved = await new ImportAppointmentResolver(read.Db, clock).ResolveAsync(table, mapping, numbers.ToHashSet(), ct);

        foreach (var outcome in resolved.OrderBy(r => r.RowNumber)) await CommitRowAsync(batch.Id, outcome, actor, ct);
    }

    private async Task CommitRowAsync(Guid batchId, ResolutionOutcome outcome, Guid actor, CancellationToken ct)
    {
        await using var work = scopes.Create();
        var db = work.Db;
        var stillValid = await db.ImportBatchRows.AsNoTracking().AnyAsync(r => r.BatchId == batchId && r.RowNumber == outcome.RowNumber && r.Status == ImportRowStatus.Valid, ct);
        if (!stillValid) return;                                                         // settled by an earlier attempt

        string status, reason = "";
        IReadOnlyDictionary<string, string>? errors = null;
        Guid? appointmentId = null;
        if (outcome.Rejection is { } rejection)
        {
            (status, reason, errors) = (rejection.Status, rejection.Reason ?? "", rejection.Errors);
        }
        else
        {
            var a = outcome.Resolved!;
            var key = ScheduleKey(batchId, a.RowNumber);
            var end = a.StartUtc.AddMinutes(a.DurationMinutes);
            var mine = await db.Appointments.AsNoTracking().Where(x => x.ScheduleKey == key).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
            if (mine is not null)
            {
                (status, appointmentId) = (ImportRowStatus.Accepted, mine);              // an earlier attempt of this very row scheduled it
            }
            else if (await db.Appointments.AsNoTracking().AnyAsync(x => x.Status == AppointmentStatuses.Scheduled && x.PatientId == a.Request.PatientId && x.ProviderProfileId == a.Request.ProviderId
                         && x.OperatoryId == a.Request.OperatoryId && x.StartUtc == a.StartUtc && x.EndUtc == end, ct))
            {
                (status, reason) = (ImportRowStatus.Skipped, ImportRowReason.AlreadyScheduled);
            }
            else
            {
                try
                {
                    var scheduled = await work.Scheduler.ScheduleAsync(a.Request, key, actor, ct);
                    (status, appointmentId) = (ImportRowStatus.Accepted, scheduled.Appointment.Id);
                }
                catch (SchedulingException ex) when (ex.StatusCode != 503)
                {
                    var message = ex.Reason is null ? ex.Message : $"{ex.Message.TrimEnd('.')} ({ex.Reason}).";
                    (status, reason, errors) = (ImportRowStatus.Rejected, ex.Code, new Dictionary<string, string> { ["schedule"] = message });
                }
            }
        }

        // The scheduler may have cleared the change tracker (it does when a refusal is recorded), so the row is read afresh now, never held across the scheduling call.
        db.ChangeTracker.Clear();
        var row = await db.ImportBatchRows.SingleAsync(r => r.BatchId == batchId && r.RowNumber == outcome.RowNumber, ct);
        Apply(row, status, reason.Length == 0 ? null : reason, errors, appointmentId);
        row.UpdatedAtUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);                                                    // this row's result commits by itself, apart from every other row
    }

    private static void Apply(ImportBatchRow row, string status, string? reason, IReadOnlyDictionary<string, string>? errors, Guid? appointmentId)
    {
        row.Status = status;
        row.Reason = reason;
        row.ErrorsJson = errors is null ? null : JsonSerializer.Serialize(errors);
        row.ResultRecordId = appointmentId;
    }
}
