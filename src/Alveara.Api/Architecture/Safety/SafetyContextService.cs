using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Safety;

/// <summary>
/// ALV-N011: the shared patient-safety context. It is a READ-ONLY projection built from what is stored - nothing is inferred and nothing is invented to fill an empty field:
/// - active <b>allergies</b> and <b>current medications</b> are read live from the patient's clinical record (one source of truth, with the record's own timestamps);
/// - <b>alerts</b> are what a clinician explicitly stated (a condition, pregnancy, anticoagulant status, an adverse reaction, a custom alert) with their source;
/// - <b>clearances</b> are the practice's own requested / received / resolved workflow;
/// - what is NOT established (a record section never reviewed, unknown, or changed since it was confirmed) is returned as a <see cref="SafetyGap"/>, so the absence of an entry
///   is never read as "none".
/// Acknowledged state is per person and per revision and is reported beside the status, never instead of it. The shared live board gets only <see cref="SafetyIndicator"/>.
/// </summary>
public class SafetyContextService(AlveraDbContext db, IPracticeClock clock) : ISafetyContextProvider
{
    public async Task<SafetyContext> GetAsync(Guid patientId, Guid forUserId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var asOf = clock.UtcNow;
        var recordItems = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.PatientId == patientId && i.RemovedAtUtc == null).ToListAsync(ct);
        var reviews = await db.ClinicalSectionReviews.AsNoTracking().Where(r => r.PatientId == patientId).ToListAsync(ct);
        var alerts = await db.SafetyAlerts.AsNoTracking().Where(a => a.PatientId == patientId).OrderBy(a => a.CreatedAtUtc).ThenBy(a => a.Id).ToListAsync(ct);
        var alertIds = alerts.Select(a => a.Id).ToList();
        var acks = await db.SafetyAlertAcknowledgements.AsNoTracking().Where(a => a.PatientId == patientId && a.UserId == forUserId && alertIds.Contains(a.AlertId)).ToListAsync(ct);
        var clearances = await db.Clearances.AsNoTracking().Where(c => c.PatientId == patientId).OrderByDescending(c => c.RequestedAtUtc).ThenBy(c => c.Id).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, recordItems.SelectMany(i => new[] { i.CreatedByUserId, i.UpdatedByUserId })
            .Concat(alerts.SelectMany(a => new[] { a.UpdatedByUserId, a.CreatedByUserId, a.ResolvedByUserId }))
            .Concat(clearances.SelectMany(c => new[] { c.RequestedByUserId, c.ReceivedByUserId, c.ClosedByUserId })), ct);

        var entries = new List<SafetyEntry>();
        foreach (var i in recordItems.Where(i => i.Status == ClinicalItemStatuses.Active && i.Kind is EncounterEntryKinds.Allergy or EncounterEntryKinds.Medication))
            entries.Add(RecordEntry(i, names));
        var itemsById = recordItems.ToDictionary(i => i.Id);
        var removedSources = alerts.Where(a => a.SourceItemId is not null && !itemsById.ContainsKey(a.SourceItemId.Value)).Select(a => a.SourceItemId!.Value).ToHashSet();
        foreach (var a in alerts)
            entries.Add(AlertEntry(a, a.SourceItemId is { } sid && itemsById.TryGetValue(sid, out var src) ? src : null, a.SourceItemId is { } rid && removedSources.Contains(rid),
                acks.FirstOrDefault(k => k.AlertId == a.Id && k.Revision == a.Revision), names));

        var active = entries.Where(e => e.Status == SafetyAlertStatuses.Active).OrderBy(e => SafetySeverities.Rank(e.Severity)).ThenBy(e => e.Category).ThenBy(e => e.Title).ToList();
        var resolved = entries.Where(e => e.Status == SafetyAlertStatuses.Resolved).OrderByDescending(e => e.ResolvedAtUtc).ToList();
        var gaps = Gaps(recordItems, reviews);
        var clearanceViews = clearances.OrderBy(c => ClearanceStatuses.IsOpen(c.Status) ? 0 : 1).ThenByDescending(c => c.RequestedAtUtc).Select(c => ToView(c, names)).ToList();
        return new SafetyContext(patientId, asOf, Summarize(patientId, asOf, active, clearances, gaps), active, resolved, clearanceViews, gaps);
    }

    public async Task<SafetySummary> SummaryAsync(Guid patientId, Guid forUserId, CancellationToken ct) => (await GetAsync(patientId, forUserId, ct)).Summary;

    /// <summary>
    /// The minimal board indicators for several patients at once (one pass over the data, not one query per card). Alert = any active alert OR an active allergy recorded as severe;
    /// Clearance = any requested or received clearance. Patients with neither are simply absent from the result.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, SafetyIndicator>> IndicatorsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken ct)
    {
        var ids = patientIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, SafetyIndicator>();
        var withAlert = (await db.SafetyAlerts.AsNoTracking().Where(a => ids.Contains(a.PatientId) && a.Status == SafetyAlertStatuses.Active).Select(a => a.PatientId).Distinct().ToListAsync(ct)).ToHashSet();
        var severeAllergy = (await db.ClinicalRecordItems.AsNoTracking()
            .Where(i => ids.Contains(i.PatientId) && i.Kind == EncounterEntryKinds.Allergy && i.RemovedAtUtc == null && i.Status == ClinicalItemStatuses.Active && i.Severity == EncounterSeverities.Severe)
            .Select(i => i.PatientId).Distinct().ToListAsync(ct)).ToHashSet();
        var withClearance = (await db.Clearances.AsNoTracking().Where(c => ids.Contains(c.PatientId) && (c.Status == ClearanceStatuses.Requested || c.Status == ClearanceStatuses.Received))
            .Select(c => c.PatientId).Distinct().ToListAsync(ct)).ToHashSet();
        return ids.Where(id => withAlert.Contains(id) || severeAllergy.Contains(id) || withClearance.Contains(id))
            .ToDictionary(id => id, id => new SafetyIndicator(withAlert.Contains(id) || severeAllergy.Contains(id), withClearance.Contains(id)));
    }

    private async Task RequirePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new SafetyException("patient_not_found", "That patient was not found.", 404);
    }

    private static SafetyEntry RecordEntry(ClinicalRecordItem i, IReadOnlyDictionary<Guid, string> names)
    {
        var allergy = i.Kind == EncounterEntryKinds.Allergy;
        var severity = allergy ? SafetySeverities.FromAllergy(i.Severity) : null;
        var detail = allergy ? i.Reaction : string.Join(" · ", new[] { i.Dose, i.Frequency }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var attention = allergy && severity is null;
        return new SafetyEntry(
            SafetyOrigins.ClinicalRecord, i.Id, allergy ? SafetyCategories.Allergy : SafetyCategories.Medication, i.Name, string.IsNullOrEmpty(detail) ? null : detail, severity, SafetyAlertStatuses.Active,
            allergy ? "Clinical record: allergies" : "Clinical record: medications", i.Id, i.UpdatedAtUtc ?? i.CreatedAtUtc, ClinicalNames.Name(names, i.UpdatedByUserId ?? i.CreatedByUserId),
            attention, attention ? "Severity is not recorded; it is not assumed." : null, false, null, null, null, null, null, null);
    }

    private static SafetyEntry AlertEntry(SafetyAlert a, ClinicalRecordItem? source, bool sourceGone, SafetyAlertAcknowledgement? ack, IReadOnlyDictionary<Guid, string> names)
    {
        string? attention = null;
        if (a.Status == SafetyAlertStatuses.Active && a.SourceItemId is not null)
        {
            if (sourceGone) attention = "The clinical-record item this alert was based on was removed. Review whether the alert still applies.";
            else if (source is not null && source.Status != ClinicalItemStatuses.Active) attention = $"The clinical-record item this alert was based on is now {source.Status.ToLowerInvariant()}. Review whether the alert still applies.";
        }
        return new SafetyEntry(
            SafetyOrigins.Alert, a.Id, a.Category, a.Title, a.Detail, a.Severity, a.Status, a.SourceNote, a.SourceItemId, a.UpdatedAtUtc ?? a.CreatedAtUtc,
            ClinicalNames.Name(names, a.UpdatedByUserId ?? a.CreatedByUserId), attention is not null, attention, ack is not null, ack?.AcknowledgedAtUtc, a.Revision, Convert.ToBase64String(a.RowVersion),
            a.ResolvedAtUtc, ClinicalNames.Name(names, a.ResolvedByUserId), a.ResolutionReason);
    }

    private static ClearanceView ToView(Clearance c, IReadOnlyDictionary<Guid, string> names) =>
        new(c.Id, c.Kind, c.Reason, c.RequestedFrom, c.Status, c.DocumentReference, c.Status is ClearanceStatuses.Received or ClearanceStatuses.Resolved && c.DocumentReference is null,
            c.RequestedAtUtc, ClinicalNames.Name(names, c.RequestedByUserId), c.ReceivedAtUtc, ClinicalNames.Name(names, c.ReceivedByUserId), c.ReceivedNote,
            c.ClosedAtUtc, ClinicalNames.Name(names, c.ClosedByUserId), c.ClosingReason, c.UpdatedAtUtc, Convert.ToBase64String(c.RowVersion));

    private static readonly (string Kind, string Label)[] WatchedSections =
    [
        (EncounterEntryKinds.Allergy, "Allergies"), (EncounterEntryKinds.Medication, "Medications"), (EncounterEntryKinds.MedicalHistory, "Medical history"),
    ];

    /// <summary>The record sections that matter for safety and are NOT established, in words. A section that is reviewed (or says none known) produces no gap.</summary>
    private static IReadOnlyList<SafetyGap> Gaps(IReadOnlyList<ClinicalRecordItem> items, IReadOnlyList<ClinicalSectionReview> reviews)
    {
        var gaps = new List<SafetyGap>();
        foreach (var (kind, label) in WatchedSections)
        {
            var status = ClinicalRecordReader.SectionStatus(items.Where(i => i.Kind == kind).ToList(), reviews.SingleOrDefault(r => r.Section == kind));
            var message = status switch
            {
                ClinicalSectionStatuses.NotReviewed => $"{label} have not been reviewed. Nothing listed here does not mean none.",
                ClinicalSectionStatuses.Unknown => $"{label} could not be established. Nothing listed here does not mean none.",
                ClinicalSectionStatuses.NeedsReview => $"{label} have changed or were never confirmed as current.",
                _ => null,
            };
            if (message is not null) gaps.Add(new SafetyGap(kind, status, message));
        }
        return gaps;
    }

    private static SafetySummary Summarize(Guid patientId, DateTimeOffset asOf, IReadOnlyList<SafetyEntry> active, IReadOnlyList<Clearance> clearances, IReadOnlyList<SafetyGap> gaps) =>
        new(patientId, asOf, active.Count(e => e.Origin == SafetyOrigins.Alert), active.Count(e => e.Category == SafetyCategories.Allergy), active.Count(e => e.Category == SafetyCategories.Medication),
            active.Select(e => e.Severity).Where(s => s is not null).OrderBy(SafetySeverities.Rank).FirstOrDefault(), clearances.Count(c => ClearanceStatuses.IsOpen(c.Status)),
            active.Count(e => e.Origin == SafetyOrigins.Alert && !e.AcknowledgedByMe), active.Count(e => e.NeedsAttention), gaps);
}
