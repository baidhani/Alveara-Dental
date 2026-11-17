using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>Closing a session (finalize into a chart, or abandon), and linking a finalized chart. See <see cref="PerioSessionService"/> for the promises.</summary>
public partial class PerioSessionService
{
    /// <summary>
    /// Validates the whole draft as one chart and saves it as the immutable chart of STORY-012, with its whole-tooth records, closing the session, in one save. A chart with any problem is refused
    /// (<c>validation_failed</c>) and the draft stays open and untouched. Finalizing a session that is already finalized returns its chart and changes nothing, whatever version the caller holds.
    /// </summary>
    public async Task<PerioExamView> FinalizeAsync(Guid sessionId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var session = await db.PerioSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct) ?? throw new PerioException("session_not_found", "That chart session was not found.", 404);
        if (session.Status == PerioSessionStatuses.Finalized) return await ExamAsync(session.ExamId!.Value, ct);
        if (session.Status == PerioSessionStatuses.Abandoned) throw Closed(session);
        db.Entry(session).Property(s => s.RowVersion).OriginalValue = ParseVersion(rowVersion);

        var readings = await db.PerioSessionReadings.AsNoTracking().Where(r => r.SessionId == sessionId).ToListAsync(ct);
        var teeth = await db.PerioSessionTeeth.AsNoTracking().Where(t => t.SessionId == sessionId).ToListAsync(ct);
        var absent = await PerioReads.AbsentTeethAsync(db, session.PatientId, ct);
        var problems = PerioChartValidator.Validate(new PerioChartInput(
            [.. readings.Select(r => new PerioReadingInput(r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque))],
            [.. teeth.Select(t => new PerioToothInput(t.ToothKey, t.Mobility, t.Furcation, t.Excluded))]), absent);
        if (problems.Count > 0) throw new PerioException("validation_failed", "The chart cannot be finalized yet. Nothing was saved; the session is still open.", 400, problems);

        var now = clock.UtcNow;
        var exam = new PerioExam { Id = Guid.NewGuid(), PatientId = session.PatientId, IdempotencyKey = $"session-{session.Id:N}", RecordedAtUtc = now, RecordedByUserId = actor, ReadingCount = readings.Count };
        db.PerioExams.Add(exam);
        db.PerioReadings.AddRange(readings.Select(r => new PerioReading
        {
            Id = Guid.NewGuid(), ExamId = exam.Id, ToothKey = r.ToothKey, Site = r.Site, ProbingDepthMm = r.ProbingDepthMm, RecessionMm = r.RecessionMm, Bleeding = r.Bleeding, Suppuration = r.Suppuration, Plaque = r.Plaque,
        }));
        db.PerioToothRecords.AddRange(teeth.Select(t => new PerioToothRecord { Id = Guid.NewGuid(), ExamId = exam.Id, ToothKey = t.ToothKey, Mobility = t.Mobility, Furcation = t.Furcation, Excluded = t.Excluded }));
        (session.Status, session.ClosedAtUtc, session.ClosedByUserId, session.ExamId, session.UpdatedAtUtc, session.UpdatedByUserId) = (PerioSessionStatuses.Finalized, now, actor, exam.Id, now, actor);
        AuditService.Record(db, "PerioExamRecorded", nameof(PerioExam), exam.Id, actor, "Periodontal chart recorded.");
        AuditService.Record(db, "PerioSessionFinalized", nameof(PerioSession), session.Id, actor, "Periodontal chart session finalized.");
        try
        {
            await SaveAsync(session.Id, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // another request finalized it at the same moment: its chart stands, so this is a retry of it
            var winner = await db.PerioSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            if (winner is { Status: PerioSessionStatuses.Finalized, ExamId: not null }) return await ExamAsync(winner.ExamId.Value, ct);
            throw;
        }
        return await ExamAsync(exam.Id, ct);
    }

    /// <summary>Discards a draft; its entries stay in the record but it can never be charted from again. Abandoning an abandoned session is quiet; a finalized one cannot be abandoned.</summary>
    public async Task<PerioSessionView> AbandonAsync(Guid sessionId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var session = await db.PerioSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct) ?? throw new PerioException("session_not_found", "That chart session was not found.", 404);
        if (session.Status == PerioSessionStatuses.Abandoned) return await GetAsync(sessionId, ct);
        if (session.Status == PerioSessionStatuses.Finalized) throw Closed(session);
        db.Entry(session).Property(s => s.RowVersion).OriginalValue = ParseVersion(rowVersion);
        var now = clock.UtcNow;
        (session.Status, session.ClosedAtUtc, session.ClosedByUserId, session.UpdatedAtUtc, session.UpdatedByUserId) = (PerioSessionStatuses.Abandoned, now, actor, now, actor);
        AuditService.Record(db, "PerioSessionAbandoned", nameof(PerioSession), session.Id, actor, "Periodontal chart session abandoned.");
        await SaveAsync(session.Id, ct);
        return await GetAsync(sessionId, ct);
    }

    /// <summary>One saved chart with its readings, whole-tooth records and links.</summary>
    public async Task<PerioExamView> ExamAsync(Guid examId, CancellationToken ct)
    {
        var exam = await db.PerioExams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId, ct) ?? throw new PerioException("exam_not_found", "That periodontal chart was not found.", 404);
        return (await PerioReads.ViewsAsync(db, [exam], ct)).Single();
    }

    /// <summary>
    /// Links a saved chart to a diagnosis, treatment plan, encounter or history entry. Append-only; the same link again is quiet. Linking never changes the chart.
    /// ALV-N022 A3 (D-1 B): a Diagnosis link names one of the SAME patient's diagnoses by id (typed column with a foreign key; same patient also enforced by the database,
    /// 51289); free text is refused for it. The other link types keep their reference text (out of D-1's scope). <see cref="PerioExamLink.Reference"/> then mirrors the id
    /// only so the existing uniqueness and quiet-repeat rule keep working.
    /// </summary>
    public async Task<PerioExamView> LinkAsync(Guid examId, string? linkType, string? reference, Guid? diagnosisId, Guid actor, CancellationToken ct)
    {
        var type = linkType?.Trim();
        var text = reference?.Trim();
        var problems = new List<PerioProblem>();
        if (type is null || !PerioLinkTypes.All.Contains(type)) problems.Add(new(null, null, "linkType", "invalid", $"Choose what the chart is linked to: {string.Join(", ", PerioLinkTypes.All)}."));
        else if (type == PerioLinkTypes.Diagnosis)
        {
            if (!string.IsNullOrEmpty(text)) problems.Add(new(null, null, "reference", "free_text_retired", "Free-text diagnosis references are no longer accepted. Choose one of this patient's diagnoses."));
            if (diagnosisId is null) problems.Add(new(null, null, "diagnosisId", "required", "Choose one of this patient's diagnoses."));
            else text = diagnosisId.Value.ToString();
        }
        else
        {
            if (diagnosisId is not null) problems.Add(new(null, null, "diagnosisId", "invalid", "A diagnosis can be chosen only for a diagnosis link."));
            if (string.IsNullOrEmpty(text) || text.Length > MaxLinkReference) problems.Add(new(null, null, "reference", "invalid", $"Enter a reference of 1 to {MaxLinkReference} characters."));
        }
        if (problems.Count > 0) throw new PerioException("validation_failed", "The link needs correcting. Nothing was saved.", 400, problems);

        var exam = await db.PerioExams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId, ct) ?? throw new PerioException("exam_not_found", "That periodontal chart was not found.", 404);
        if (diagnosisId is { } dx && !await db.Diagnoses.AsNoTracking().AnyAsync(d => d.Id == dx && d.PatientId == exam.PatientId && d.Status != Alveara.Api.Architecture.Clinical.DiagnosisStatuses.Withdrawn, ct))
            throw DiagnosisNotUsable();
        if (await db.PerioExamLinks.AsNoTracking().AnyAsync(l => l.ExamId == examId && l.LinkType == type && l.Reference == text, ct)) return await ExamAsync(examId, ct);

        db.PerioExamLinks.Add(new PerioExamLink { Id = Guid.NewGuid(), ExamId = examId, PatientId = exam.PatientId, LinkType = type!, Reference = text!, DiagnosisId = diagnosisId, CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor });
        AuditService.Record(db, "PerioExamLinked", nameof(PerioExam), examId, actor, "Periodontal chart linked.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                  // the same link was made at the same moment: it stands
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: DiagnosisSamePatientGuard })
        {
            db.ChangeTracker.Clear();
            throw DiagnosisNotUsable();                                                // the database guard caught it: same answer, no SQL text
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
        return await ExamAsync(examId, ct);
    }

    /// <summary>The database guard's error number (migration AddTypedClearanceDocumentAndPerioDiagnosisLinks, trigger TR_PerioExamLinks_DiagnosisSamePatient).</summary>
    internal const int DiagnosisSamePatientGuard = 51289;

    private static PerioException DiagnosisNotUsable() =>
        new("validation_failed", "The link needs correcting. Nothing was saved.", 400,
            [new(null, null, "diagnosisId", "not_found", "That diagnosis was not found among this patient's diagnoses. Choose one of this patient's diagnoses.")]);

    private static PerioException Closed(PerioSession s) =>
        new("session_closed", $"This chart session was {s.Status.ToLowerInvariant()}, so it can no longer be changed. Start a new session to chart again.", 409);
}
