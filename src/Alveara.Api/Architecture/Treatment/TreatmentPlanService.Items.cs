using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Procedures;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>An item after every rule has accepted it: the diagnosis, the catalog version it is proposed under and the fee copied from that version.</summary>
public sealed record BuiltItem(Guid DiagnosisId, Guid ProcedureId, Guid VersionId, string ProcedureCode, string? ToothKey, string? Surface, decimal Fee)
{
    /// <summary>The same procedure for the same diagnosis in the same place: proposing it twice in one plan is almost certainly a slip.</summary>
    public bool SameAs(BuiltItem other) => DiagnosisId == other.DiagnosisId && ProcedureId == other.ProcedureId && ToothKey == other.ToothKey && Surface == other.Surface;
}

public partial class TreatmentPlanService
{
    /// <summary>
    /// Adds one proposed procedure to a plan. The save key makes a retry return the plan as the first save left it. The plan must not be withdrawn, and the caller must hold its current version.
    /// </summary>
    public async Task<PlanView> AddItemAsync(Guid planId, PlanItemInput input, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var errors = new Dictionary<string, string>();
        var key = TreatmentPlanRules.CleanKey(errors, "idempotencyKey", input.IdempotencyKey);
        if (key is not null && await db.TreatmentPlanItems.AsNoTracking().AnyAsync(i => i.PlanId == planId && i.IdempotencyKey == key, ct)) return await GetAsync(planId, ct);
        RequireProposed(plan);

        var item = await BuildItemAsync(plan.PatientId, input, "", errors, ct);
        if (item is not null)
        {
            var active = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId && i.WithdrawnAtUtc == null).ToListAsync(ct);
            if (active.Any(a => a.DiagnosisId == item.DiagnosisId && a.ProcedureId == item.ProcedureId && a.ToothKey == item.ToothKey && a.Surface == item.Surface))
                errors["procedureId"] = "This procedure is already in the plan for that diagnosis and place.";
        }
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);

        db.Entry(plan).Property(p => p.RowVersion).OriginalValue = version;
        var now = clock.UtcNow;
        var number = (await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == planId).Select(i => (int?)i.ItemNumber).MaxAsync(ct) ?? 0) + 1;
        var staged = StageItem(plan, number, key!, item!, actor, now);
        Touch(plan, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.ItemAdded, staged.Id, null, null, actor, now);
        AuditService.Record(db, "TreatmentPlanItemAdded", nameof(TreatmentPlan), planId, actor, $"Procedure {item!.ProcedureCode} proposed in a treatment plan (practice fee estimate {item.Fee:F2} USD).");
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>Withdraws one item with a reason; the item and the reason stay on the plan. Withdrawing a withdrawn item changes nothing. The caller must hold the plan's current version.</summary>
    public async Task<PlanView> WithdrawItemAsync(Guid planId, Guid itemId, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var item = await db.TreatmentPlanItems.SingleOrDefaultAsync(i => i.Id == itemId && i.PlanId == planId, ct)
            ?? throw new TreatmentPlanException("item_not_found", "That item was not found in this treatment plan.", 404);
        if (item.WithdrawnAtUtc is not null) return await GetAsync(planId, ct);
        RequireProposed(plan);
        var why = TreatmentPlanRules.RequireReason(reason, "Say why this procedure is being withdrawn from the plan.");
        db.Entry(plan).Property(p => p.RowVersion).OriginalValue = TreatmentPlanRules.ParseVersion(rowVersion);

        var now = clock.UtcNow;
        item.WithdrawnAtUtc = now;
        item.WithdrawnByUserId = actor;
        item.WithdrawnReason = why;
        Touch(plan, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.ItemWithdrawn, itemId, null, why, actor, now);
        AuditService.Record(db, "TreatmentPlanItemWithdrawn", nameof(TreatmentPlan), planId, actor, $"Item {item.ItemNumber} withdrawn from a treatment plan.", why);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    // ---------- the item rules ----------

    private TreatmentPlanItem StageItem(TreatmentPlan plan, int number, string key, BuiltItem b, Guid actor, DateTimeOffset now)
    {
        var item = new TreatmentPlanItem
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, ItemNumber = number, IdempotencyKey = key, DiagnosisId = b.DiagnosisId, ProcedureId = b.ProcedureId,
            ProcedureVersionId = b.VersionId, ToothKey = b.ToothKey, Surface = b.Surface, Fee = b.Fee, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.TreatmentPlanItems.Add(item);
        return item;
    }

    /// <summary>
    /// Checks one proposed procedure against everything the plan promises and returns it ready to store, or adds a message per wrong field (under <paramref name="prefix"/>, which is
    /// <c>items[0].</c> when several are created together and empty for a single add) and returns null. A diagnosis that is missing and one that is another patient's are refused in the same
    /// words, so the answer never confirms that a diagnosis of someone else exists.
    /// </summary>
    private async Task<BuiltItem?> BuildItemAsync(Guid patientId, PlanItemInput input, string prefix, Dictionary<string, string> errors, CancellationToken ct)
    {
        var before = errors.Count;

        if (input.DiagnosisId is null) errors[prefix + "diagnosisId"] = "Choose the diagnosis this procedure is for.";
        else
        {
            var diagnosis = await db.Diagnoses.AsNoTracking().Where(d => d.Id == input.DiagnosisId).Select(d => new { d.PatientId, d.Status }).FirstOrDefaultAsync(ct);
            if (diagnosis is null || diagnosis.PatientId != patientId) errors[prefix + "diagnosisId"] = "That diagnosis was not found for this patient.";
            else if (diagnosis.Status == Clinical.DiagnosisStatuses.Withdrawn) errors[prefix + "diagnosisId"] = "That diagnosis has been withdrawn. Choose a current diagnosis.";
        }

        ProcedureDetailView? procedure = null;
        if (input.ProcedureId is null) errors[prefix + "procedureId"] = "Choose the procedure to propose.";
        else
        {
            try
            {
                procedure = await catalog.GetAsync(input.ProcedureId.Value, Today(), ct);
            }
            catch (ProcedureCatalogException ex) when (ex.StatusCode == 404)
            {
                errors[prefix + "procedureId"] = "That procedure was not found in the catalog.";
            }
            if (procedure is not null && procedure.Summary.Status != "Active")
            {
                errors[prefix + "procedureId"] = procedure.Summary.Status switch
                {
                    "Inactive" => "That procedure is inactive. Choose an active procedure.",
                    "Scheduled" => "That procedure's fee has not started yet. Choose an active procedure.",
                    _ => "That procedure is past its last valid date. Choose an active procedure.",
                };
                procedure = null;
            }
        }

        var (tooth, surface, siteProblem) = TreatmentPlanRules.CleanSite(input.ToothKey, input.Surface);
        if (siteProblem is not null) errors[prefix + SiteField(siteProblem)] = siteProblem;
        else if (procedure is not null)
        {
            var v = procedure.Summary.Version;
            var fit = TreatmentPlanRules.ApplicabilityProblem(v.Scope, v.Dentition, tooth, surface);
            if (fit is not null) errors[prefix + SiteField(fit)] = fit;
        }

        if (errors.Count > before || procedure is null) return null;
        var version = procedure.Summary.Version;
        return new BuiltItem(input.DiagnosisId!.Value, procedure.Summary.Id, version.VersionId, procedure.Summary.Code, tooth, surface, version.Fee);
    }

    /// <summary>The field a site message belongs to: the surface only when the message is about the surface alone; a message that asks for the tooth (or says the area is larger than a tooth) belongs to the tooth.</summary>
    private static string SiteField(string message) =>
        message.Contains("surface", StringComparison.OrdinalIgnoreCase) && !message.Contains("choose the tooth", StringComparison.OrdinalIgnoreCase) && !message.Contains("larger area", StringComparison.OrdinalIgnoreCase)
            ? "surface" : "toothKey";
}
