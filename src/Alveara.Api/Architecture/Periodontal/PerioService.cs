using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// STORY-012: periodontal charting - probing depth, recession and bleeding per site.
///
/// How the promises are kept:
/// - <b>Bad data is refused, with every problem named.</b> A chart is judged by <see cref="PerioRules"/> before anything is written; one problem means nothing is saved
///   (<c>validation_failed</c>, 400, each problem naming tooth, site, field and the allowed range). The database refuses the same data again if a writer bypasses the service.
/// - <b>Every chart is logged, in the same save.</b> The chart, its readings and its PHI-free audit entry (who and when; never a tooth or a measurement) are saved once, so a chart
///   can never exist without its log entry: if the audit write fails, the whole save fails and nothing is stored.
/// - <b>A failed save says so and leaves nothing behind.</b> Any database failure is reported as <c>save_failed</c> (503, "nothing was saved, try again") and logged with its error class
///   only. Not retried here: the caller retries with the same key, which is safe.
/// - <b>Retries are quiet.</b> A chart is saved under the caller's idempotency key (one per patient). The same key with the same readings returns the chart already saved, with no
///   second chart and no second audit entry; the same key with different readings is refused (<c>idempotency_key_reused</c>, 409) rather than silently ignored. Two simultaneous
///   saves with one key end with one chart (a unique index) and both get it.
/// - <b>Charts are never edited or deleted</b> (triggers). A correction is a new chart; the history keeps what was found at each visit.
/// Not handled (by design): charts for primary teeth, negative recession, and linking a chart to an encounter (left for the diagnosis story).
/// </summary>
public class PerioService(AlveraDbContext db, IPracticeClock clock, ILogger<PerioService>? logger = null)
{
    public const int KeyMax = 64;
    public const int DefaultHistory = 20;
    public const int MaxHistory = 100;

    public async Task<PerioExamView> RecordAsync(Guid patientId, string? idempotencyKey, IReadOnlyList<PerioReadingInput>? readings, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim();
        var problems = new List<PerioProblem>();
        if (string.IsNullOrEmpty(key) || key.Length > KeyMax)
            problems.Add(new(null, null, "idempotencyKey", "invalid", $"Send a key of 1 to {KeyMax} characters with each save, so a retry cannot create a second chart."));
        problems.AddRange(PerioRules.Validate(readings));
        if (problems.Count > 0) throw new PerioException("validation_failed", "The chart has entries that need correcting. Nothing was saved.", 400, problems);

        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PerioException("patient_not_found", "That patient was not found.", 404);

        var existing = await FindAsync(patientId, key!, ct);
        if (existing is not null) return await ReplayAsync(existing, readings!, ct);

        var exam = new PerioExam { Id = Guid.NewGuid(), PatientId = patientId, IdempotencyKey = key!, RecordedAtUtc = clock.UtcNow, RecordedByUserId = actor, ReadingCount = readings!.Count };
        db.PerioExams.Add(exam);
        db.PerioReadings.AddRange(readings.Select(r => new PerioReading
        {
            Id = Guid.NewGuid(), ExamId = exam.Id, ToothKey = r.ToothKey, Site = r.Site, ProbingDepthMm = (byte)r.ProbingDepthMm, RecessionMm = (byte)r.RecessionMm, Bleeding = r.Bleeding, Suppuration = r.Suppuration, Plaque = r.Plaque,
        }));
        AuditService.Record(db, "PerioExamRecorded", nameof(PerioExam), exam.Id, actor, "Periodontal chart recorded.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // a simultaneous save with the same key won: forget what we staged and answer with its chart (or refuse if it was a different chart)
            db.ChangeTracker.Clear();
            var winner = await FindAsync(patientId, key!, ct) ?? throw SaveFailed(ex);
            return await ReplayAsync(winner, readings, ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
        return await ViewAsync(exam, ct);
    }

    /// <summary>A patient's saved charts, newest first (the most recent <paramref name="take"/>, 20 unless asked, never more than 100).</summary>
    public async Task<PerioHistoryView> HistoryAsync(Guid patientId, int? take, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PerioException("patient_not_found", "That patient was not found.", 404);
        var n = Math.Clamp(take ?? DefaultHistory, 1, MaxHistory);
        var exams = await db.PerioExams.AsNoTracking().Where(e => e.PatientId == patientId).OrderByDescending(e => e.RecordedAtUtc).ThenByDescending(e => e.Id).Take(n).ToListAsync(ct);
        return new PerioHistoryView(patientId, await PerioReads.ViewsAsync(db, exams, ct));
    }

    private Task<PerioExam?> FindAsync(Guid patientId, string key, CancellationToken ct) =>
        db.PerioExams.AsNoTracking().SingleOrDefaultAsync(e => e.PatientId == patientId && e.IdempotencyKey == key, ct);

    /// <summary>The same key again: the chart already saved if it holds exactly these readings, otherwise a refusal that says the key belongs to a different chart.</summary>
    private async Task<PerioExamView> ReplayAsync(PerioExam existing, IReadOnlyList<PerioReadingInput> readings, CancellationToken ct)
    {
        var view = await ViewAsync(existing, ct);
        var saved = view.Readings.Select(r => (r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque)).OrderBy(x => x.ToothKey, StringComparer.Ordinal).ThenBy(x => x.Site, StringComparer.Ordinal);
        var sent = readings.Select(r => (r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque)).OrderBy(x => x.ToothKey, StringComparer.Ordinal).ThenBy(x => x.Site, StringComparer.Ordinal);
        if (!saved.SequenceEqual(sent))
            throw new PerioException("idempotency_key_reused", "That save key was already used for a different chart. Nothing was changed; save again to record this chart as a new one.", 409);
        return view;
    }

    private async Task<PerioExamView> ViewAsync(PerioExam exam, CancellationToken ct) => (await PerioReads.ViewsAsync(db, [exam], ct)).Single();

    private PerioException SaveFailed(DbUpdateException ex)
    {
        // error class only: never the readings, the patient or the SQL text
        logger?.LogError("perio_save_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
        return new PerioException("save_failed", "The chart could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503);
    }
}
