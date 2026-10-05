using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// ALV-012-C01: loads the two charts to compare and hands them to <see cref="PerioComparison"/>. The current chart is either a saved chart or the draft being entered; the previous one is the
/// chart named, or by default the patient's latest FINALIZED chart before the current one (a draft is never the "previous"). Both must belong to the patient asked about. Read-only: nothing is
/// written, so there is nothing to audit and nothing to retry.
/// </summary>
public class PerioComparisonService(AlveraDbContext db)
{
    public async Task<PerioComparisonView> CompareAsync(Guid patientId, Guid? currentExamId, Guid? currentSessionId, Guid? previousExamId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PerioException("patient_not_found", "That patient was not found.", 404);
        if ((currentExamId is null) == (currentSessionId is null))
            throw new PerioException("validation_failed", "Name the chart to compare: either a saved chart or the draft being entered.", 400, [new(null, null, "current", "invalid", "Give exactly one of currentExamId or currentSessionId.")]);

        PerioChartSnapshot current;
        DateTimeOffset currentAt;
        Guid? currentExam = currentExamId;
        if (currentExamId is { } examId)
        {
            var exam = await ExamAsync(patientId, examId, ct);
            current = PerioChartSnapshot.From(exam);
            currentAt = exam.RecordedAtUtc;
        }
        else
        {
            var session = await db.PerioSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == currentSessionId && s.PatientId == patientId, ct) ?? throw new PerioException("session_not_found", "That chart session was not found.", 404);
            current = await SessionSnapshotAsync(session.Id, ct);
            currentAt = DateTimeOffset.MaxValue;                                         // a draft is always the newest thing
            currentExam = session.ExamId;
        }

        PerioExamView previous;
        if (previousExamId is { } prevId) previous = await ExamAsync(patientId, prevId, ct);
        else
        {
            var latest = await db.PerioExams.AsNoTracking().Where(e => e.PatientId == patientId && e.RecordedAtUtc < currentAt && e.Id != currentExam)
                .OrderByDescending(e => e.RecordedAtUtc).ThenByDescending(e => e.Id).FirstOrDefaultAsync(ct)
                ?? throw new PerioException("no_previous_chart", "There is no earlier finalized chart to compare with.", 404);
            previous = (await PerioReads.ViewsAsync(db, [latest], ct)).Single();
        }
        return PerioComparison.Compare(PerioChartSnapshot.From(previous), current);
    }

    private async Task<PerioExamView> ExamAsync(Guid patientId, Guid examId, CancellationToken ct)
    {
        var exam = await db.PerioExams.AsNoTracking().SingleOrDefaultAsync(e => e.Id == examId && e.PatientId == patientId, ct) ?? throw new PerioException("exam_not_found", "That periodontal chart was not found.", 404);
        return (await PerioReads.ViewsAsync(db, [exam], ct)).Single();
    }

    private async Task<PerioChartSnapshot> SessionSnapshotAsync(Guid sessionId, CancellationToken ct)
    {
        var readings = await db.PerioSessionReadings.AsNoTracking().Where(r => r.SessionId == sessionId).ToListAsync(ct);
        var teeth = await db.PerioSessionTeeth.AsNoTracking().Where(t => t.SessionId == sessionId).ToListAsync(ct);
        return new PerioChartSnapshot(
            [.. PerioReads.Ordered(readings.Select(r => new PerioReading { Id = r.Id, ExamId = sessionId, ToothKey = r.ToothKey, Site = r.Site, ProbingDepthMm = r.ProbingDepthMm, RecessionMm = r.RecessionMm, Bleeding = r.Bleeding, Suppuration = r.Suppuration, Plaque = r.Plaque }))
                .Select(r => new PerioReadingView(r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.ProbingDepthMm + r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque))],
            [.. teeth.Select(t => new PerioToothView(t.ToothKey, t.Mobility, t.Furcation, t.Excluded))]);
    }
}
