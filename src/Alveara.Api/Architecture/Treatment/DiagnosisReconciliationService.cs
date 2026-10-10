using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>What happened to one diagnosis's old free-text treatment-plan reference. <c>Eligible</c> is what a preview says about a reference that would be resolved.</summary>
public static class ReconciliationOutcomes
{
    public const string Resolved = "Resolved";
    public const string Eligible = "Eligible";
    public const string AlreadyResolved = "AlreadyResolved";
    public const string NoMatch = "NoMatch";
    public const string Ambiguous = "Ambiguous";
    public const string Skipped = "Skipped";
}

public sealed record ReconciliationItem(Guid DiagnosisId, string DiagnosisLabel, string? Reference, string Outcome, Guid? PlanId, string? PlanTitle, string? Method, string? Reason);

/// <summary><c>Applied</c> is false for a preview (nothing was saved). <c>Matched</c> counts the references resolved now (or, in a preview, eligible); <c>Unresolved</c> counts those that stay as they were.</summary>
public sealed record ReconciliationResult(Guid PatientId, bool Applied, int Matched, int AlreadyResolved, int Unresolved, IReadOnlyList<ReconciliationItem> Items);

/// <summary>
/// ALV-015-C01: ties the free-text treatment-plan references diagnoses recorded before plans existed to the real plans of the same patient. Safety first:
/// - <b>Only the same patient's plans are ever considered.</b> Another patient's plan is never matched or accepted, and a chosen plan of another patient is refused as "not found", exactly as an unknown one is.
/// - <b>The diagnosis is never changed.</b> The resolution is a row beside it that keeps the original text; the diagnosis's text and history are untouched, and the database refuses to change that text afterwards.
/// - <b>A reference with no safe target stays visibly unresolved</b> with the reason (no plan has that code or id, or more than one plan matches); nothing is guessed, deleted or rewritten.
/// - <b>Repeating it changes nothing</b>: one resolution per diagnosis; a diagnosis already resolved is reported as such. A preview saves nothing.
/// Every resolution is one save with its audit entry, so a failed log stores nothing.
/// </summary>
public class DiagnosisReconciliationService(AlveraDbContext db, IPracticeClock clock, ILogger<DiagnosisReconciliationService>? logger = null)
{
    private sealed record PlanKey(Guid Id, string Title, string? ReferenceCode);

    /// <summary>Resolves (or, when <paramref name="apply"/> is false, only reports) every eligible reference of a patient's diagnoses by matching it to a plan's reference code or id.</summary>
    public async Task<ReconciliationResult> ReconcileAsync(Guid patientId, bool apply, Guid actor, CancellationToken ct) => await ReconcileAsync(patientId, apply, actor, true, ct);

    private async Task<ReconciliationResult> ReconcileAsync(Guid patientId, bool apply, Guid actor, bool mayRetry, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new TreatmentPlanException("patient_not_found", "That patient was not found.", 404);
        var diagnoses = await db.Diagnoses.AsNoTracking().Where(d => d.PatientId == patientId && d.TreatmentPlanReference != null).OrderBy(d => d.CreatedAtUtc).ThenBy(d => d.Id).ToListAsync(ct);
        var ids = diagnoses.Select(d => d.Id).ToList();
        var resolved = await db.DiagnosisPlanResolutions.AsNoTracking().Where(r => ids.Contains(r.DiagnosisId)).ToDictionaryAsync(r => r.DiagnosisId, ct);
        var plans = (await db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == patientId).Select(p => new PlanKey(p.Id, p.Title, p.ReferenceCode)).ToListAsync(ct)).ToList();
        var titles = plans.ToDictionary(p => p.Id, p => p.Title);

        var items = new List<ReconciliationItem>();
        var now = clock.UtcNow;
        foreach (var d in diagnoses)
        {
            var reference = d.TreatmentPlanReference!;
            ReconciliationItem Item(string outcome, Guid? plan = null, string? method = null, string? reason = null) =>
                new(d.Id, d.Label, reference, outcome, plan, plan is { } p ? titles.GetValueOrDefault(p) : null, method, reason);

            if (resolved.TryGetValue(d.Id, out var existing)) { items.Add(Item(ReconciliationOutcomes.AlreadyResolved, existing.PlanId, existing.Method)); continue; }
            if (d.Status == DiagnosisStatuses.Withdrawn) { items.Add(Item(ReconciliationOutcomes.Skipped, reason: "The diagnosis was withdrawn, so its reference is left as it is.")); continue; }

            var byCode = plans.Where(p => p.ReferenceCode is not null && string.Equals(p.ReferenceCode, reference, StringComparison.OrdinalIgnoreCase)).Select(p => p.Id).ToList();
            var byId = plans.Where(p => string.Equals(p.Id.ToString("D"), reference, StringComparison.OrdinalIgnoreCase)).Select(p => p.Id).ToList();
            var matches = byCode.Concat(byId).Distinct().ToList();
            if (matches.Count == 0) { items.Add(Item(ReconciliationOutcomes.NoMatch, reason: "No plan of this patient has this text as its reference code or id. It stays unresolved.")); continue; }
            if (matches.Count > 1) { items.Add(Item(ReconciliationOutcomes.Ambiguous, reason: "More than one plan of this patient matches this text. It stays unresolved; choose the plan yourself.")); continue; }

            var method = byCode.Count == 1 ? ResolutionMethods.ReferenceCode : ResolutionMethods.PlanId;
            items.Add(Item(apply ? ReconciliationOutcomes.Resolved : ReconciliationOutcomes.Eligible, matches[0], method));
            if (apply) Stage(d, matches[0], method, actor, now);
        }

        if (apply && items.Any(i => i.Outcome == ReconciliationOutcomes.Resolved))
        {
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } && mayRetry)
            {
                // someone resolved the same diagnosis at the same moment: look again; what they saved stands and is reported as already resolved
                db.ChangeTracker.Clear();
                return await ReconcileAsync(patientId, apply, actor, false, ct);
            }
            catch (DbUpdateException ex)
            {
                db.ChangeTracker.Clear();
                logger?.LogError("diagnosis_reconciliation_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
                throw SaveFailed(ex);
            }
        }
        var matched = items.Count(i => i.Outcome is ReconciliationOutcomes.Resolved or ReconciliationOutcomes.Eligible);
        var already = items.Count(i => i.Outcome == ReconciliationOutcomes.AlreadyResolved);
        return new ReconciliationResult(patientId, apply, matched, already, items.Count - matched - already, items);
    }

    /// <summary>Resolves one diagnosis's reference to a plan the person chose. The plan must be the same patient's; another patient's plan is refused exactly as an unknown plan is.</summary>
    public async Task<ReconciliationItem> ResolveAsync(Guid diagnosisId, Guid? planId, Guid actor, CancellationToken ct)
    {
        var d = await db.Diagnoses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == diagnosisId, ct) ?? throw new TreatmentPlanException("diagnosis_not_found", "That diagnosis was not found.", 404);
        if (d.TreatmentPlanReference is null)
            throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["diagnosisId"] = "This diagnosis has no treatment-plan reference to resolve." });
        if (d.Status == DiagnosisStatuses.Withdrawn) throw new TreatmentPlanException("diagnosis_withdrawn", "This diagnosis was withdrawn, so its reference is left as it is.", 409);
        if (planId is null) throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["planId"] = "Choose the treatment plan this reference belongs to." });
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId && p.PatientId == d.PatientId, ct)
            ?? throw new TreatmentPlanException("plan_not_found", "That treatment plan was not found.", 404);

        var existing = await db.DiagnosisPlanResolutions.AsNoTracking().SingleOrDefaultAsync(r => r.DiagnosisId == d.Id, ct);
        if (existing is not null)
        {
            if (existing.PlanId == plan.Id) return new(d.Id, d.Label, d.TreatmentPlanReference, ReconciliationOutcomes.AlreadyResolved, plan.Id, plan.Title, existing.Method, null);
            throw new TreatmentPlanException("already_resolved", "This diagnosis's reference has already been tied to a different treatment plan, and that is final.", 409);
        }

        Stage(d, plan.Id, ResolutionMethods.Chosen, actor, clock.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            var winner = await db.DiagnosisPlanResolutions.AsNoTracking().SingleAsync(r => r.DiagnosisId == d.Id, ct);
            if (winner.PlanId == plan.Id) return new(d.Id, d.Label, d.TreatmentPlanReference, ReconciliationOutcomes.AlreadyResolved, plan.Id, plan.Title, winner.Method, null);
            throw new TreatmentPlanException("already_resolved", "This diagnosis's reference has already been tied to a different treatment plan, and that is final.", 409);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogError("diagnosis_resolution_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
            throw SaveFailed(ex);
        }
        return new(d.Id, d.Label, d.TreatmentPlanReference, ReconciliationOutcomes.Resolved, plan.Id, plan.Title, ResolutionMethods.Chosen, null);
    }

    private void Stage(Diagnosis d, Guid planId, string method, Guid actor, DateTimeOffset now)
    {
        db.DiagnosisPlanResolutions.Add(new DiagnosisPlanResolution { Id = Guid.NewGuid(), DiagnosisId = d.Id, PatientId = d.PatientId, PlanId = planId, ReferenceText = d.TreatmentPlanReference!, Method = method, ResolvedAtUtc = now, ResolvedByUserId = actor });
        AuditService.Record(db, "DiagnosisPlanReferenceResolved", nameof(Diagnosis), d.Id, actor, "A diagnosis's treatment-plan reference was tied to a treatment plan.");
    }

    private static TreatmentPlanException SaveFailed(Exception ex) =>
        new("save_failed", "The resolution could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503, inner: ex);
}
