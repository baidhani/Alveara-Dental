using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>Reads shared by the chart service and the session service, so a saved chart reads the same whichever way it was made.</summary>
internal static class PerioReads
{
    /// <summary>Saved charts as they are shown: readings in site order with derived attachment loss, whole-tooth records and links, and who recorded each.</summary>
    public static async Task<List<PerioExamView>> ViewsAsync(AlveraDbContext db, IReadOnlyList<PerioExam> exams, CancellationToken ct)
    {
        if (exams.Count == 0) return [];
        var ids = exams.Select(e => e.Id).ToList();
        var readings = (await db.PerioReadings.AsNoTracking().Where(r => ids.Contains(r.ExamId)).ToListAsync(ct)).ToLookup(r => r.ExamId);
        var teeth = (await db.PerioToothRecords.AsNoTracking().Where(t => ids.Contains(t.ExamId)).ToListAsync(ct)).ToLookup(t => t.ExamId);
        var links = (await db.PerioExamLinks.AsNoTracking().Where(l => ids.Contains(l.ExamId)).OrderBy(l => l.CreatedAtUtc).ToListAsync(ct)).ToLookup(l => l.ExamId);
        var names = await ClinicalNames.ResolveAsync(db, exams.Select(e => (Guid?)e.RecordedByUserId).Concat(links.SelectMany(g => g).Select(l => l.CreatedByUserId)), ct);
        return exams.Select(e => new PerioExamView(
            e.Id, e.PatientId, e.RecordedAtUtc, ClinicalNames.Name(names, e.RecordedByUserId) ?? ClinicalNames.Fallback, e.ReadingCount,
            Ordered(readings[e.Id]).Select(r => new PerioReadingView(r.ToothKey, r.Site, r.ProbingDepthMm, r.RecessionMm, r.ProbingDepthMm + r.RecessionMm, r.Bleeding, r.Suppuration, r.Plaque)).ToList(),
            teeth[e.Id].OrderBy(t => t.ToothKey, StringComparer.Ordinal).Select(t => new PerioToothView(t.ToothKey, t.Mobility, t.Furcation, t.Excluded)).ToList(),
            links[e.Id].Select(l => new PerioLinkView(l.LinkType, l.Reference, ClinicalNames.Name(names, l.CreatedByUserId), l.CreatedAtUtc)).ToList())).ToList();
    }

    /// <summary>Readings by tooth then by the order of the six sites (not alphabetical).</summary>
    public static IEnumerable<PerioReading> Ordered(IEnumerable<PerioReading> rows) =>
        rows.OrderBy(r => r.ToothKey, StringComparer.Ordinal).ThenBy(r => PerioRules.Sites.ToList().IndexOf(r.Site));

    /// <summary>
    /// The permanent teeth the odontogram records as absent for this patient: an active finding whose condition has the Absent effect, in state Existing or Completed (a planned or merely diagnosed
    /// extraction does not make a tooth absent yet). The same rule the odontogram applies when it refuses a finding on a missing tooth.
    /// </summary>
    public static async Task<HashSet<string>> AbsentTeethAsync(AlveraDbContext db, Guid patientId, CancellationToken ct)
    {
        var keys = await (from x in db.ToothFindings.AsNoTracking()
                          join t in db.ConditionTypes.AsNoTracking() on x.Condition equals t.Code
                          where x.PatientId == patientId && x.Status == FindingStatuses.Active && t.ToothEffect == ToothEffects.Absent
                                && (x.State == FindingStates.Existing || x.State == FindingStates.Completed)
                          select x.ToothKey).Distinct().ToListAsync(ct);
        return keys.Where(k => !ToothKeys.IsPrimary(k)).ToHashSet();
    }
}
