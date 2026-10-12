using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// The clinical history with the authoritative detail added. The plan service derives the history from completion state rows (STORY-008) and cannot know about completed-procedure records (that would make the two
/// namespaces depend on each other), so this adds, for each procedure completed through the authoritative workflow, its current provider, performed time and encounter, its charge and what happened to the chart.
/// A procedure completed the old way is listed exactly as before, with those fields empty.
/// </summary>
public partial class CompletedProcedureService
{
    public async Task<IReadOnlyList<ClinicalHistoryEntryView>> ClinicalHistoryAsync(Guid patientId, CancellationToken ct)
    {
        var entries = await plans.ClinicalHistoryAsync(patientId, ct);
        if (entries.Count == 0) return entries;
        var itemIds = entries.Select(e => e.ItemId).ToList();
        var rows = await db.CompletedProcedures.AsNoTracking().Where(c => c.PatientId == patientId && itemIds.Contains(c.PlanItemId)).ToListAsync(ct);
        if (rows.Count == 0) return entries;

        var details = (await DetailsAsync(rows, ct)).ToDictionary(d => d.PlanItemId);
        return entries.Select(e => details.TryGetValue(e.ItemId, out var d)
            ? e with { CompletedProcedureId = d.Id, ProviderName = d.ProviderName, PerformedAtUtc = d.PerformedAtUtc, EncounterId = d.EncounterId, ChargeEntryNumber = d.Charge?.EntryNumber, ChargeAmount = d.Charge?.Amount, ChartOutcome = d.ChartOutcome }
            : e).ToList();
    }
}
