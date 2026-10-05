using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// ALV-012-C01: a periodontal chart being entered as a draft session and then finalized into the immutable chart of STORY-012.
///
/// How the promises are kept:
/// - <b>Entry never loses what was already valid.</b> A save is a batch applied together or not at all. If any entry in it is wrong the whole batch is refused (<c>validation_failed</c>, every problem named
///   with tooth, site and field) and the draft is exactly as it was, so a mistake on one tooth never discards another tooth's entries.
/// - <b>A stale edit is refused.</b> Every save echoes the session's row version; if someone else changed the draft first it is the shared 409 concurrency conflict, and nothing is merged.
/// - <b>One draft per patient.</b> Starting returns the open draft if there is one (a unique index backs it, so two people starting at once end with one).
/// - <b>Missing teeth do not corrupt entry.</b> A tooth the odontogram records as missing, or one marked not charted, is skipped by the entry sweep; a reading on one is refused with the way out named.
///   Only the teeth a save touches are judged, so a change in the odontogram never blocks an unrelated save.
/// - <b>Every change is logged, in the same save.</b> Starting, each batch, finalizing and abandoning write a PHI-free audit entry with the signed-in user (never a tooth or a measurement); if the log
///   cannot be written the change is not saved. The finalized chart carries the same single <c>PerioExamRecorded</c> entry every chart has.
/// - <b>Finalizing is all or nothing and repeatable.</b> The whole draft is validated as one chart; the chart, its readings, its whole-tooth records, the closed session and the audit entry are one
///   save. Finalizing a session already finalized returns its chart and changes nothing. A failed save says so and leaves the draft open and untouched.
/// - <b>A closed session never changes</b> (triggers), and a chart never changes once saved.
/// Not handled (by design): editing a finalized chart (a correction is a new session), and more than one open draft per patient.
/// </summary>
public partial class PerioSessionService(AlveraDbContext db, IPracticeClock clock, ILogger<PerioSessionService>? logger = null)
{
    public const int MaxLinkReference = 100;

    /// <summary>The patient's open draft, started if there is none. A second call returns the same draft.</summary>
    public async Task<PerioSessionView> StartAsync(Guid patientId, Guid actor, CancellationToken ct)
    {
        await EnsurePatientAsync(patientId, ct);
        var open = await db.PerioSessions.AsNoTracking().SingleOrDefaultAsync(s => s.PatientId == patientId && s.Status == PerioSessionStatuses.Draft, ct);
        if (open is not null) return await ViewAsync(open.Id, ct);

        var session = new PerioSession { Id = Guid.NewGuid(), PatientId = patientId, Status = PerioSessionStatuses.Draft, StartedAtUtc = clock.UtcNow, StartedByUserId = actor };
        db.PerioSessions.Add(session);
        AuditService.Record(db, "PerioSessionStarted", nameof(PerioSession), session.Id, actor, "Periodontal chart session started.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                  // someone started one at the same moment: theirs stands, so this is a retry of it
            var winner = await db.PerioSessions.AsNoTracking().SingleOrDefaultAsync(s => s.PatientId == patientId && s.Status == PerioSessionStatuses.Draft, ct) ?? throw SaveFailed(ex);
            return await ViewAsync(winner.Id, ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
        return await ViewAsync(session.Id, ct);
    }

    /// <summary>The patient's open draft, or null when none is open.</summary>
    public async Task<PerioSessionView?> CurrentAsync(Guid patientId, CancellationToken ct)
    {
        await EnsurePatientAsync(patientId, ct);
        var open = await db.PerioSessions.AsNoTracking().SingleOrDefaultAsync(s => s.PatientId == patientId && s.Status == PerioSessionStatuses.Draft, ct);
        return open is null ? null : await ViewAsync(open.Id, ct);
    }

    public async Task<PerioSessionView> GetAsync(Guid sessionId, CancellationToken ct) => await ViewAsync(sessionId, ct);

    /// <summary>
    /// Saves a batch of entries into a draft. All of it or none of it. An empty batch changes nothing and returns the draft as it is. Returns the draft with its new row version.
    /// </summary>
    public async Task<PerioSessionView> SaveEntriesAsync(Guid sessionId, string? rowVersion, PerioEntryBatch? batch, Guid actor, CancellationToken ct)
    {
        var expected = ParseVersion(rowVersion);
        var session = await LoadDraftAsync(sessionId, expected, ct);
        if (batch is null || IsEmpty(batch)) return await ViewAsync(sessionId, ct);

        var readings = await db.PerioSessionReadings.Where(r => r.SessionId == sessionId).ToListAsync(ct);
        var teeth = await db.PerioSessionTeeth.Where(t => t.SessionId == sessionId).ToListAsync(ct);
        var absent = await PerioReads.AbsentTeethAsync(db, session.PatientId, ct);
        var problems = Judge(batch, readings, teeth, absent);
        if (problems.Count > 0) throw new PerioException("validation_failed", "Some entries need correcting. Nothing in this save was applied; everything entered before is as it was.", 400, problems);

        Apply(batch, sessionId, readings, teeth);
        session.UpdatedAtUtc = clock.UtcNow;
        session.UpdatedByUserId = actor;
        AuditService.Record(db, "PerioSessionUpdated", nameof(PerioSession), session.Id, actor, "Periodontal chart session updated.");
        await SaveAsync(session.Id, ct);
        return await ViewAsync(sessionId, ct);
    }

    // ---------- judging a batch ----------

    private static bool IsEmpty(PerioEntryBatch b) => (b.Readings?.Count ?? 0) + (b.Teeth?.Count ?? 0) + (b.ClearSites?.Count ?? 0) + (b.ClearTeeth?.Count ?? 0) == 0;

    /// <summary>
    /// Everything wrong with the batch, judged as the draft would be after it: the batch's own entries (ranges, names, duplicates, grades, absent teeth) and the conflicts it would create with what is
    /// already there (a reading on a tooth that is excluded, an exclusion of a tooth that still has readings). Only the teeth the batch touches are judged.
    /// </summary>
    private static List<PerioProblem> Judge(PerioEntryBatch batch, List<PerioSessionReading> readings, List<PerioSessionTooth> teeth, HashSet<string> absent)
    {
        var problems = new List<PerioProblem>();
        if ((batch.Readings?.Count ?? 0) > PerioRules.MaxReadings || (batch.ClearSites?.Count ?? 0) > PerioRules.MaxReadings || (batch.Teeth?.Count ?? 0) > 32 || (batch.ClearTeeth?.Count ?? 0) > 32)
            return [new(null, null, "batch", "too_many", "That is more entries than a chart can hold (192 sites, 32 teeth). Nothing was saved.")];
        problems.AddRange(PerioChartValidator.Validate(new PerioChartInput(batch.Readings, batch.Teeth), absent, requireReadings: false));
        if (batch.ClearSites?.Any(c => c is null || c.ToothKey is null || c.Site is null) == true || batch.ClearTeeth?.Any(c => c is null) == true)
            problems.Add(new(null, null, "batch", "invalid", "An entry to clear is missing its tooth or site."));
        if (problems.Count > 0) return problems;

        // the draft as it would be after the batch
        var clearedSites = (batch.ClearSites ?? []).Select(c => (c.ToothKey, c.Site)).ToHashSet();
        var clearedTeeth = (batch.ClearTeeth ?? []).ToHashSet();
        var mergedReadings = readings.Where(r => !clearedSites.Contains((r.ToothKey, r.Site)) && batch.Readings?.Any(b => b.ToothKey == r.ToothKey && b.Site == r.Site) != true)
            .Select(r => new PerioReadingInput(r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque)).Concat(batch.Readings ?? []).ToList();
        var mergedTeeth = teeth.Where(t => !clearedTeeth.Contains(t.ToothKey) && batch.Teeth?.Any(b => b.ToothKey == t.ToothKey) != true)
            .Select(t => new PerioToothInput(t.ToothKey, t.Mobility, t.Furcation, t.Excluded)).Concat(batch.Teeth ?? []).ToList();
        var touched = (batch.Readings ?? []).Select(r => r.ToothKey).Concat((batch.Teeth ?? []).Select(t => t.ToothKey)).ToHashSet();
        problems.AddRange(PerioChartValidator.Validate(new PerioChartInput(mergedReadings, mergedTeeth), null, requireReadings: false)
            .Where(p => p.Code == "excluded_tooth" && p.ToothKey is not null && touched.Contains(p.ToothKey)));
        return problems;
    }

    private void Apply(PerioEntryBatch batch, Guid sessionId, List<PerioSessionReading> readings, List<PerioSessionTooth> teeth)
    {
        foreach (var c in batch.ClearSites ?? []) foreach (var r in readings.Where(r => r.ToothKey == c.ToothKey && r.Site == c.Site).ToList()) { db.PerioSessionReadings.Remove(r); readings.Remove(r); }
        foreach (var k in batch.ClearTeeth ?? []) foreach (var t in teeth.Where(t => t.ToothKey == k).ToList()) { db.PerioSessionTeeth.Remove(t); teeth.Remove(t); }
        foreach (var r in batch.Readings ?? [])
        {
            var row = readings.SingleOrDefault(x => x.ToothKey == r.ToothKey && x.Site == r.Site);
            if (row is null) { row = new PerioSessionReading { Id = Guid.NewGuid(), SessionId = sessionId, ToothKey = r.ToothKey, Site = r.Site }; db.PerioSessionReadings.Add(row); readings.Add(row); }
            (row.ProbingDepthMm, row.RecessionMm, row.Bleeding, row.Suppuration, row.Plaque) = ((byte)r.ProbingDepthMm, (byte)r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque);
        }
        foreach (var t in batch.Teeth ?? [])
        {
            var row = teeth.SingleOrDefault(x => x.ToothKey == t.ToothKey);
            if (row is null) { row = new PerioSessionTooth { Id = Guid.NewGuid(), SessionId = sessionId, ToothKey = t.ToothKey }; db.PerioSessionTeeth.Add(row); teeth.Add(row); }
            (row.Mobility, row.Furcation, row.Excluded) = ((byte?)t.Mobility, (byte?)t.Furcation, t.Excluded);
        }
    }

    // ---------- shared plumbing ----------

    private async Task EnsurePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PerioException("patient_not_found", "That patient was not found.", 404);
    }

    private static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new PerioException("row_version_required", "The version of the chart session you are editing is required, so a change made by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new PerioException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400); }
    }

    /// <summary>The session, tracked against the version the caller read, refused if it is not a Draft any more.</summary>
    private async Task<PerioSession> LoadDraftAsync(Guid sessionId, byte[] expected, CancellationToken ct)
    {
        var session = await db.PerioSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct) ?? throw new PerioException("session_not_found", "That chart session was not found.", 404);
        if (session.Status != PerioSessionStatuses.Draft)
            throw new PerioException("session_closed", $"This chart session was {session.Status.ToLowerInvariant()}, so it can no longer be changed. Start a new session to chart again.", 409);
        db.Entry(session).Property(s => s.RowVersion).OriginalValue = expected;
        return session;
    }

    private async Task SaveAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(PerioSession), sessionId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                  // two writers claimed the same new site: the later one is stale and must look again
            throw new ConcurrencyConflictException(nameof(PerioSession), sessionId);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
    }

    private PerioException SaveFailed(DbUpdateException ex)
    {
        // error class only: never the readings, the patient or the SQL text
        logger?.LogError("perio_session_save_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
        return new PerioException("save_failed", "The change could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503);
    }

    private async Task<PerioSessionView> ViewAsync(Guid sessionId, CancellationToken ct)
    {
        var s = await db.PerioSessions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessionId, ct) ?? throw new PerioException("session_not_found", "That chart session was not found.", 404);
        var readings = await db.PerioSessionReadings.AsNoTracking().Where(r => r.SessionId == sessionId).ToListAsync(ct);
        var teeth = await db.PerioSessionTeeth.AsNoTracking().Where(t => t.SessionId == sessionId).ToListAsync(ct);
        var absent = s.Status == PerioSessionStatuses.Draft ? await PerioReads.AbsentTeethAsync(db, s.PatientId, ct) : [];
        var names = await ClinicalNames.ResolveAsync(db, [s.StartedByUserId, s.UpdatedByUserId], ct);
        var skipped = teeth.Where(t => t.Excluded).Select(t => t.ToothKey).Concat(absent).ToHashSet();
        var entered = readings.Select(r => (r.ToothKey, r.Site)).ToHashSet();
        var next = PerioSiteModel.Sweep(skipped).Where(x => !entered.Contains(x)).Select(x => new PerioSiteRef(x.Tooth, x.Site)).FirstOrDefault();
        return new PerioSessionView(
            s.Id, s.PatientId, s.Status, s.StartedAtUtc, ClinicalNames.Name(names, s.StartedByUserId) ?? ClinicalNames.Fallback, s.UpdatedAtUtc, ClinicalNames.Name(names, s.UpdatedByUserId),
            s.ClosedAtUtc, s.ExamId, Convert.ToBase64String(s.RowVersion),
            PerioReads.Ordered(readings.Select(r => new PerioReading { Id = r.Id, ExamId = s.Id, ToothKey = r.ToothKey, Site = r.Site, ProbingDepthMm = r.ProbingDepthMm, RecessionMm = r.RecessionMm, Bleeding = r.Bleeding, Suppuration = r.Suppuration, Plaque = r.Plaque }))
                .Select(r => new PerioReadingView(r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.ProbingDepthMm + r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque)).ToList(),
            teeth.OrderBy(t => t.ToothKey, StringComparer.Ordinal).Select(t => new PerioToothView(t.ToothKey, t.Mobility, t.Furcation, t.Excluded)).ToList(),
            absent.OrderBy(k => k, StringComparer.Ordinal).ToList(), next, (PerioSiteModel.ArchOrder.Count - skipped.Count(k => PerioSiteModel.ArchOrder.Contains(k))) * PerioRules.Sites.Count);
    }
}
