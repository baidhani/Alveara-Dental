using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>One completed procedure in a patient's clinical history, read from the completion record of the plan it was planned in.</summary>
public sealed record ClinicalHistoryEntryView(
    Guid PlanId, string PlanTitle, Guid ItemId, int ItemNumber, string ProcedureCodeSystem, string ProcedureCode, string ProcedureDescription,
    string? ToothKey, string? Surface, string CompletedByName, DateTimeOffset CompletedAtUtc, int PlanRevisionAtCompletion);

public partial class TreatmentPlanService
{
    /// <summary>
    /// STORY-008: the care a patient has had, newest first. Derived on every read from the completion rows that completing a procedure already writes, so there is no second record to drift:
    /// a procedure appears once, with who completed it, when, and the plan revision in force then. Completed care stays here even if its plan is later withdrawn or revised.
    /// </summary>
    public async Task<IReadOnlyList<ClinicalHistoryEntryView>> ClinicalHistoryAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw PatientNotFound();
        // Completed is final, so the first completion row of a procedure is the moment it was completed.
        var rows = await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.PatientId == patientId && s.Completion == ItemCompletion.Completed).ToListAsync(ct);
        var completions = rows.GroupBy(s => s.ItemId).Select(g => g.OrderBy(s => s.Sequence).First()).ToList();
        if (completions.Count == 0) return [];

        var itemIds = completions.Select(c => c.ItemId).ToList();
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => itemIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        var planIds = completions.Select(c => c.PlanId).Distinct().ToList();
        var titles = await db.TreatmentPlans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Title, ct);
        var versionIds = items.Values.Select(i => i.ProcedureVersionId).Distinct().ToList();
        var versions = await db.ProcedureVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, v => v.Description, ct);
        var procedureIds = items.Values.Select(i => i.ProcedureId).Distinct().ToList();
        var procedures = await db.ProcedureDefinitions.AsNoTracking().Where(p => procedureIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => new { p.CodeSystem, p.Code }, ct);
        var names = await ClinicalNames.ResolveAsync(db, completions.Select(c => (Guid?)c.RecordedByUserId), ct);

        return completions.OrderByDescending(c => c.RecordedAtUtc).ThenBy(c => items[c.ItemId].ItemNumber).Select(c =>
        {
            var i = items[c.ItemId];
            var p = procedures[i.ProcedureId];
            return new ClinicalHistoryEntryView(c.PlanId, titles[c.PlanId], i.Id, i.ItemNumber, p.CodeSystem, p.Code, versions[i.ProcedureVersionId], i.ToothKey, i.Surface,
                ClinicalNames.Name(names, c.RecordedByUserId) ?? ClinicalNames.Fallback, c.RecordedAtUtc, c.RevisionNumber);
        }).ToList();
    }
}
