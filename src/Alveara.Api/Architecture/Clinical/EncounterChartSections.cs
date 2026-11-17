using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-N022 A1 (OD-01, OD-02, OD-04): how an encounter reads its allergy, medication and medical-history sections now that the patient's chart is the one current record.
///
/// - The chart now: the live chart items of that kind.
/// - What this encounter did: the chart item versions and section statements carrying this encounter's id. Both tables are append-only (database triggers), so what a
///   finalized encounter shows never changes when the chart changes later.
/// - What it recorded before the change: its own legacy entries and "none reported" mark, kept and shown read-only.
/// - A section is addressed when this encounter changed it on the chart, made a statement about it (that was not withdrawn here), or holds legacy entries or a legacy mark.
/// - Contradiction (brief 4.2; computed, never stored): a legacy "none reported" mark, this encounter's "none known", or the chart's "none known", for a section that had
///   an active item when it was said or has one now.
/// </summary>
internal sealed class EncounterChartSections(AlveraDbContext db)
{
    /// <summary>Allergies, medications and medical history live on the chart; dental history keeps the encounter's own entries (BR-3).</summary>
    public static bool OnChart(string kind) => kind != EncounterEntryKinds.DentalHistory;

    public sealed record Inputs(
        IReadOnlyList<ClinicalRecordItem> LiveItems, IReadOnlyList<ClinicalRecordItemVersion> VersionsHere, IReadOnlyList<ClinicalRecordEvent> StatementsHere,
        IReadOnlyList<ClinicalRecordItemVersion> KindHistory, IReadOnlyList<ClinicalSectionReview> ChartReviews);

    public async Task<Inputs> LoadAsync(Guid encounterId, Guid patientId, CancellationToken ct)
    {
        var live = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.PatientId == patientId && i.RemovedAtUtc == null).OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id).ToListAsync(ct);
        var here = await db.ClinicalRecordItemVersions.AsNoTracking().Where(v => v.EncounterId == encounterId).OrderBy(v => v.OccurredAtUtc).ThenBy(v => v.VersionNumber).ToListAsync(ct);
        var statements = await db.ClinicalRecordEvents.AsNoTracking().Where(e => e.EncounterId == encounterId && (e.EventType == "SectionReviewed" || e.EventType == "ReviewCleared"))
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).ToListAsync(ct);
        // the whole version history of the patient's items is needed only to judge a legacy mark; it is small (one row per change)
        var history = await db.ClinicalRecordItemVersions.AsNoTracking().Where(v => v.PatientId == patientId).ToListAsync(ct);
        var reviews = await db.ClinicalSectionReviews.AsNoTracking().Where(r => r.PatientId == patientId).ToListAsync(ct);
        return new Inputs(live, here, statements, history, reviews);
    }

    public static SectionView Build(string kind, Inputs data, IReadOnlyList<EntryView> legacyEntries, EncounterSectionMark? legacyMark, IReadOnlyDictionary<Guid, string> names)
    {
        var chart = data.LiveItems.Where(i => i.Kind == kind).Select(i => ClinicalRecordReader.ToView(i, names)).ToList();
        var here = data.VersionsHere.Where(v => v.Kind == kind)
            .Select(v => new ProvenanceView(v.ItemId, v.VersionNumber, v.ChangeType, v.Name, v.Status, v.OccurredAtUtc, ClinicalNames.Name(names, v.ActorUserId))).ToList();
        var statements = data.StatementsHere.Where(e => e.Section == kind).ToList();
        var statement = LastStatement(statements);
        var status = Status(legacyEntries.Count > 0, legacyMark is not null, here.Count > 0, statement);
        // who and when: a legacy mark keeps its own; otherwise the statement made here (null when none stands)
        var stated = statement is null ? null : statements[^1];
        // brief 4.2: every "none reported / none known" said about this section - the legacy mark, this encounter's standing statement, the chart's current statement
        var noneSaidAt = new List<DateTimeOffset>();
        if (legacyMark is not null) noneSaidAt.Add(legacyMark.MarkedAtUtc);
        if (statement == ClinicalReviewStates.NoneKnown) noneSaidAt.Add(stated!.OccurredAtUtc);
        var chartReview = data.ChartReviews.SingleOrDefault(r => r.Section == kind);
        if (chartReview?.State == ClinicalReviewStates.NoneKnown) noneSaidAt.Add(chartReview.ReviewedAtUtc);
        var activeNow = data.LiveItems.Count(i => i.Kind == kind && i.Status == ClinicalItemStatuses.Active);
        var contradiction = ContradictionFor(kind, noneSaidAt, data.KindHistory, activeNow);
        return new SectionView(kind, status, legacyEntries, legacyMark?.MarkedAtUtc ?? stated?.OccurredAtUtc, legacyMark?.MarkedByUserId ?? stated?.ActorUserId,
            SectionSources.Chart, chart, here, statement, contradiction);
    }

    /// <summary>The section status, shared by the encounter detail and the encounter list so both say the same thing.</summary>
    public static string Status(bool hasLegacyEntries, bool hasLegacyMark, bool changedHere, string? statementHere) =>
        hasLegacyEntries || changedHere || statementHere == ClinicalReviewStates.Reviewed ? SectionStatuses.Recorded
        : statementHere == ClinicalReviewStates.NoneKnown || hasLegacyMark ? SectionStatuses.NoneReported
        : statementHere == ClinicalReviewStates.Unknown ? SectionStatuses.Unknown
        : SectionStatuses.Empty;

    /// <summary>
    /// The statement this encounter last made about the section: Reviewed, NoneKnown or Unknown; null when none was made or the last one here was withdrawn here.
    /// A statement line in an older wording still counts as a made statement (Reviewed), so it is never lost.
    /// </summary>
    public static string? LastStatement(IEnumerable<ClinicalRecordEvent> statements)
    {
        var last = statements.LastOrDefault();
        if (last is null || last.EventType == "ReviewCleared") return null;
        return ClinicalReviewPhrases.StateOf(last.Detail) ?? ClinicalReviewStates.Reviewed;
    }

    /// <summary>
    /// Brief 4.2: a "none reported / none known" statement contradicts the chart when the section had an active item at the moment it was said (read from the append-only
    /// version history) or has one now. Computed on every read, never stored; both records are kept and shown.
    /// </summary>
    internal static string? ContradictionFor(string kind, IReadOnlyList<DateTimeOffset> noneSaidAt, IReadOnlyList<ClinicalRecordItemVersion> history, int activeNow)
    {
        if (noneSaidAt.Count == 0) return null;
        var activeThen = noneSaidAt.Max(at => ActiveAt(kind, at, history));
        if (activeThen > 0)
            return $"Contradiction: marked none reported while {activeThen} active item{(activeThen == 1 ? " was" : "s were")} listed on the chart. Both records are kept.";
        return activeNow == 0 ? null
            : $"Contradiction: marked none reported, and {activeNow} active item{(activeNow == 1 ? " is" : "s are")} listed on the chart now. Both records are kept.";
    }

    private static int ActiveAt(string kind, DateTimeOffset at, IReadOnlyList<ClinicalRecordItemVersion> history) =>
        history.Where(v => v.Kind == kind && v.OccurredAtUtc <= at).GroupBy(v => v.ItemId)
            .Select(g => g.OrderBy(v => v.VersionNumber).Last())
            .Count(v => v.Status == ClinicalItemStatuses.Active && v.ChangeType != ClinicalChangeTypes.RemovedInError);
}
