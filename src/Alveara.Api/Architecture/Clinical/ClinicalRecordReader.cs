using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: reads a patient's longitudinal clinical record and the history of one item. Read-only; gating by permission is the controller's job. A section's status is
/// DERIVED here from its items and its review row, so "reviewed", "needs review", "none known", "unknown" and "not reviewed" can never drift from the data.
/// </summary>
public class ClinicalRecordReader(AlveraDbContext db)
{
    public const int TimelineLimit = 50;

    public async Task<ClinicalRecordView> GetAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new ClinicalException("patient_not_found", "That patient was not found.", 404);
        var items = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.PatientId == patientId && i.RemovedAtUtc == null).OrderBy(i => i.CreatedAtUtc).ThenBy(i => i.Id).ToListAsync(ct);
        var reviews = await db.ClinicalSectionReviews.AsNoTracking().Where(r => r.PatientId == patientId).ToListAsync(ct);
        var events = await db.ClinicalRecordEvents.AsNoTracking().Where(e => e.PatientId == patientId).OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id).Take(TimelineLimit).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, items.SelectMany(i => new[] { i.CreatedByUserId, i.UpdatedByUserId })
            .Concat(reviews.Select(r => r.ReviewedByUserId)).Concat(events.Select(e => e.ActorUserId)), ct);

        var sections = new List<RecordSectionView>();
        foreach (var kind in EncounterEntryKinds.All)
        {
            var mine = items.Where(i => i.Kind == kind).ToList();
            var review = reviews.SingleOrDefault(r => r.Section == kind);
            sections.Add(new RecordSectionView(kind, SectionStatus(mine, review), mine.Select(i => ToView(i, names)).ToList(), review?.ReviewedAtUtc, ClinicalNames.Name(names, review?.ReviewedByUserId)));
        }
        return new ClinicalRecordView(patientId, sections,
            events.Select(e => new RecordEventView(e.Section, e.EventType, ClinicalNames.Name(names, e.ActorUserId), e.OccurredAtUtc, e.EncounterId, e.Detail)).ToList());
    }

    /// <summary>The status of a section: with items, "Reviewed" only while no item changed after the review; without items, what the clinician said, else "not reviewed".</summary>
    internal static string SectionStatus(IReadOnlyList<ClinicalRecordItem> liveItems, ClinicalSectionReview? review)
    {
        if (liveItems.Count > 0)
        {
            var newest = liveItems.Max(i => i.UpdatedAtUtc ?? i.CreatedAtUtc);
            return review is { State: ClinicalReviewStates.Reviewed } && review.ReviewedAtUtc >= newest ? ClinicalSectionStatuses.Reviewed : ClinicalSectionStatuses.NeedsReview;
        }
        return review?.State switch
        {
            ClinicalReviewStates.NoneKnown => ClinicalSectionStatuses.NoneKnown,
            ClinicalReviewStates.Unknown => ClinicalSectionStatuses.Unknown,
            _ => ClinicalSectionStatuses.NotReviewed,
        };
    }

    public async Task<ItemHistoryView> HistoryAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.ClinicalRecordItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == itemId, ct) ?? throw new ClinicalException("item_not_found", "That item was not found.", 404);
        var versions = await db.ClinicalRecordItemVersions.AsNoTracking().Where(v => v.ItemId == itemId).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => v.ActorUserId), ct);
        return new ItemHistoryView(itemId, item.Kind, versions.Select(v => new ItemVersionView(
            v.VersionNumber, v.ChangeType, v.Name, v.Detail, v.Reaction, v.Severity, v.Dose, v.Frequency, v.Status, v.Reason, v.EncounterId, ClinicalNames.Name(names, v.ActorUserId), v.OccurredAtUtc)).ToList());
    }

    private static ItemView ToView(ClinicalRecordItem i, IReadOnlyDictionary<Guid, string> names) =>
        new(i.Id, i.Kind, i.Name, i.Detail, i.Reaction, i.Severity, i.Dose, i.Frequency, i.Status, i.CreatedAtUtc, ClinicalNames.Name(names, i.CreatedByUserId),
            i.UpdatedAtUtc, ClinicalNames.Name(names, i.UpdatedByUserId), Convert.ToBase64String(i.RowVersion));
}
