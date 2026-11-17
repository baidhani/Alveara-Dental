using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-N022 A1 (OD-01, OD-02): the encounter's allergy, medication and medical-history actions write to the patient's CHART (<see cref="ClinicalRecordService"/>) with this
/// encounter's id, instead of keeping a second copy on the encounter. The chart service records the version or statement, its PHI-free history and audit, and the encounter's
/// own history row, in one save. The encounter is loaded first with the caller's expected version, so a stale encounter editor is still refused.
///
/// - Rules come from the chart: duplicates (Addendum 02: an existing item is pointed to, never a second one), "none known" only without active items (OD-04), open
///   encounter only (<c>encounter_not_open</c>).
/// - Removing from the encounter is allowed only for an item ADDED in this encounter (marked entered in error, with a reason); other items change by status, so an encounter
///   never erases a chart fact it did not record.
/// - Entries and marks recorded on the encounter before this change are kept and read-only (<c>legacy_entry_read_only</c>).
/// - Dental history is not moved (BR-3): it keeps the encounter's own entries.
/// </summary>
public partial class EncounterService
{
    private ClinicalRecordService Chart => new(db, clock);

    private async Task<EncounterDetail> AddToChartAsync(Guid encounterId, string kind, EntryFields fields, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        await Chart.AddItemAsync(encounter.PatientId, kind, fields, ClinicalItemStatuses.Active, encounterId, actor, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    private async Task<EncounterDetail> UpdateOnChartAsync(Guid encounterId, Guid itemId, EntryFields fields, string? rowVersion, string? itemRowVersion, string? reason, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        await RequireChartItemAsync(encounter, itemId, ct);
        await Chart.UpdateItemAsync(itemId, fields, itemRowVersion, reason, encounterId, actor, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    private async Task<EncounterDetail> RemoveFromChartAsync(Guid encounterId, Guid itemId, string? rowVersion, string? itemRowVersion, string? reason, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        await RequireChartItemAsync(encounter, itemId, ct);
        var addedHere = await db.ClinicalRecordItemVersions.AsNoTracking().AnyAsync(v => v.ItemId == itemId && v.VersionNumber == 1 && v.EncounterId == encounterId, ct);
        if (!addedHere)
            throw new ClinicalException("item_not_added_here", "This item was not added in this encounter, so it cannot be removed from here. Change its status instead.", 409);
        await Chart.RemoveInErrorAsync(itemId, itemRowVersion, reason, encounterId, actor, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    /// <summary>Changes a chart item's status from the encounter (for example reactivating a resolved allergy, Addendum 02): the user chose the status; nothing is preselected here.</summary>
    public async Task<EncounterDetail> SetEntryStatusAsync(Guid encounterId, Guid itemId, string? status, string? rowVersion, string? itemRowVersion, string? reason, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        await RequireChartItemAsync(encounter, itemId, ct);
        await Chart.SetStatusAsync(itemId, status, itemRowVersion, reason, encounterId, actor, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    /// <summary>
    /// A statement about a chart section made in this encounter: "confirm current" (Reviewed), "none reported" (NoneKnown) or withdrawing it. Withdrawing is allowed only for
    /// a statement this encounter made, so one encounter never withdraws what was stated elsewhere.
    /// </summary>
    private async Task<EncounterDetail> StateOnChartAsync(Guid encounterId, string kind, string state, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        if (state == ClinicalReviewStates.NotReviewed)
        {
            var mine = EncounterChartSections.LastStatement(await db.ClinicalRecordEvents.AsNoTracking()
                .Where(e => e.EncounterId == encounterId && e.Section == kind && (e.EventType == "SectionReviewed" || e.EventType == "ReviewCleared"))
                .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id).ToListAsync(ct));
            if (mine is null) return await Reader.GetAsync(encounterId, ct); // nothing stated here: nothing to withdraw
        }
        await Chart.SetReviewAsync(encounter.PatientId, kind, state, encounterId, actor, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    public async Task<EncounterDetail> ConfirmSectionCurrentAsync(Guid encounterId, string? kind, string? rowVersion, Guid actor, CancellationToken ct)
    {
        kind = EncounterRules.RequireKind(kind);
        if (!EncounterChartSections.OnChart(kind))
            throw new ClinicalException("section_not_on_chart", $"{EncounterRules.Label(kind)} is documented in the encounter itself; there is no chart list to confirm.", 409);
        return await StateOnChartAsync(encounterId, kind, ClinicalReviewStates.Reviewed, rowVersion, actor, ct);
    }

    private async Task RequireChartItemAsync(Encounter encounter, Guid itemId, CancellationToken ct)
    {
        var item = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.Id == itemId).Select(i => new { i.PatientId, i.Kind }).SingleOrDefaultAsync(ct);
        if (item is null || item.PatientId != encounter.PatientId || !EncounterChartSections.OnChart(item.Kind))
            throw new ClinicalException("entry_not_found", "That item was not found on this patient's chart.", 404);
    }

    /// <summary>A legacy entry of a moved kind (recorded on the encounter before the chart became the one record): kept, shown, never changed.</summary>
    private async Task<EncounterEntry?> FindLegacyEntryAsync(Guid encounterId, Guid entryId, CancellationToken ct)
    {
        var entry = await db.EncounterEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == entryId && x.EncounterId == encounterId, ct);
        if (entry is not null && EncounterChartSections.OnChart(entry.Kind))
            throw new ClinicalException("legacy_entry_read_only", "This entry was recorded in the encounter before the chart became the one record. It is kept as it was and cannot be changed here.", 409);
        return entry;
    }
}
