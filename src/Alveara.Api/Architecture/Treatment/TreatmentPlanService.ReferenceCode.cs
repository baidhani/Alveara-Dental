using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;

namespace Alveara.Api.Architecture.Treatment;

public partial class TreatmentPlanService
{
    /// <summary>
    /// Gives a plan its reference code: a short code, unique among the patient's plans and set once, that an old diagnosis's free-text treatment-plan reference can be matched to. It is not a change to the
    /// plan's content (no revision), so it works on a withdrawn plan too. Saying the code it already has changes nothing; a different code once set, or a code another of the patient's plans has, is refused.
    /// </summary>
    public async Task<PlanView> SetReferenceCodeAsync(Guid planId, string? code, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        var errors = new Dictionary<string, string>();
        var clean = string.IsNullOrWhiteSpace(code) ? null : Regex.Replace(code.Trim(), "\\s+", " ");
        if (clean is null) errors["referenceCode"] = "Enter the reference code.";
        else if (clean.Any(char.IsControl)) errors["referenceCode"] = "Use a single line of plain text.";
        else if (clean.Length > TreatmentPlanRules.ReferenceCodeMax) errors["referenceCode"] = $"The code can be at most {TreatmentPlanRules.ReferenceCodeMax} characters; this one has {clean.Length}.";
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);

        if (plan.ReferenceCode is not null)
        {
            if (string.Equals(plan.ReferenceCode, clean, StringComparison.Ordinal)) return await GetAsync(planId, ct);
            throw new TreatmentPlanException("reference_code_set", "This plan already has a reference code, and it is set once.", 409);
        }
        if (await db.TreatmentPlans.AsNoTracking().AnyAsync(p => p.PatientId == plan.PatientId && p.Id != planId && p.ReferenceCode == clean, ct))
            throw new TreatmentPlanException("reference_code_in_use", "Another treatment plan of this patient already has that reference code.", 409);

        db.Entry(plan).Property(p => p.RowVersion).OriginalValue = version;
        var now = clock.UtcNow;
        plan.ReferenceCode = clean;
        Touch(plan, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.ReferenceCodeSet, null, null, null, actor, now);
        AuditService.Record(db, "TreatmentPlanReferenceCodeSet", nameof(TreatmentPlan), planId, actor, "Treatment plan reference code set.");
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }
}
