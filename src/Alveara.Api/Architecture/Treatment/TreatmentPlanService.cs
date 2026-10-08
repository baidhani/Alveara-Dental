using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// STORY-015: treatment plans (writes here; the reads for screens are in <c>TreatmentPlanService.Queries.cs</c> and the item rules are in <c>TreatmentPlanService.Items.cs</c>).
///
/// How the promises are kept:
/// - <b>A plan links to a diagnosis, procedures and fee estimates.</b> Every item names one of the patient's current diagnoses and one active catalog procedure, and copies the fee of the exact
///   catalog version in effect that day (the snapshot contract ALV-N005 built), so a later catalog fee change cannot alter what a plan says.
/// - <b>A wrong entry is refused with a message per field</b> (a missing or foreign diagnosis, an unknown or inactive procedure, a tooth that does not fit the procedure) before anything is stored.
/// - <b>Nothing is deleted or edited in place.</b> A wrong item is withdrawn with a reason and the right one added; a plan that should not stand is withdrawn with a reason.
/// - <b>Every change is logged in the same save</b> - the row, its history event (who, when, why) and a PHI-free audit entry - so a change cannot exist without its log entry; if anything in the
///   save fails (including the log) nothing is stored and the caller is told so (<c>save_failed</c>, 503) without internals.
/// - <b>Repeats are quiet and stale edits are refused</b>: the same save key returns what the first save made, withdrawing something already withdrawn changes nothing, and a change made from an
///   out-of-date copy is a conflict.
/// </summary>
public partial class TreatmentPlanService(AlveraDbContext db, IPracticeClock clock, ProcedureCatalogService catalog, ILogger<TreatmentPlanService>? logger = null)
{
    private DateOnly Today() => DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime);

    /// <summary>Creates a plan with its first proposed procedures in one save. The save key makes a retry return the plan the first save made.</summary>
    public async Task<PlanView> CreateAsync(Guid patientId, PlanInput input, Guid actor, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw PatientNotFound();
        var errors = new Dictionary<string, string>();
        var key = TreatmentPlanRules.CleanKey(errors, "idempotencyKey", input.IdempotencyKey);
        if (key is not null)
        {
            var existing = await db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == patientId && p.IdempotencyKey == key).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (existing is not null) return await GetAsync(existing.Value, ct);
        }

        var title = TreatmentPlanRules.CleanTitle(errors, "title", input.Title);
        var inputs = input.Items ?? [];
        if (inputs.Count == 0) errors["items"] = "Add at least one proposed procedure.";
        else if (inputs.Count > TreatmentPlanRules.ItemsMax) errors["items"] = $"A plan can start with at most {TreatmentPlanRules.ItemsMax} procedures; add the rest afterwards.";

        var built = new List<BuiltItem>();
        for (var i = 0; i < Math.Min(inputs.Count, TreatmentPlanRules.ItemsMax); i++)
        {
            var item = await BuildItemAsync(patientId, inputs[i], $"items[{i}].", errors, ct);
            if (item is null) continue;
            var twin = built.FindIndex(b => b.SameAs(item));
            if (twin >= 0) errors[$"items[{i}]"] = $"This is the same procedure for the same diagnosis and place as item {twin + 1}.";
            else built.Add(item);
        }
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);

        var now = clock.UtcNow;
        var plan = new TreatmentPlan { Id = Guid.NewGuid(), PatientId = patientId, IdempotencyKey = key!, Title = title!, Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = now, CreatedByUserId = actor };
        db.TreatmentPlans.Add(plan);
        StageEvent(plan, 1, TreatmentPlanChanges.Created, null, title, null, actor, now);
        AuditService.Record(db, "TreatmentPlanCreated", nameof(TreatmentPlan), plan.Id, actor, $"Treatment plan created with {built.Count} proposed procedure{(built.Count == 1 ? "" : "s")} (practice fee estimate {built.Sum(b => b.Fee):F2} USD).");
        var number = 0;
        foreach (var b in built)
        {
            number++;
            var item = StageItem(plan, number, $"{key}:{number}", b, actor, now);
            StageEvent(plan, number + 1, TreatmentPlanChanges.ItemAdded, item.Id, null, null, actor, now);
            AuditService.Record(db, "TreatmentPlanItemAdded", nameof(TreatmentPlan), plan.Id, actor, $"Procedure {b.ProcedureCode} proposed in a treatment plan (practice fee estimate {b.Fee:F2} USD).");
        }
        try
        {
            await SaveAsync(plan.Id, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // the same save arrived twice at the same moment (the unique key): the first one stands
            db.ChangeTracker.Clear();
            var winner = await db.TreatmentPlans.AsNoTracking().Where(p => p.PatientId == patientId && p.IdempotencyKey == key).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (winner is not null) return await GetAsync(winner.Value, ct);
            throw;
        }
        return await GetAsync(plan.Id, ct);
    }

    /// <summary>Changes the title. Saving the same title changes nothing; a withdrawn plan cannot be renamed.</summary>
    public async Task<PlanView> RenameAsync(Guid planId, string? title, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        RequireProposed(plan);
        var errors = new Dictionary<string, string>();
        var t = TreatmentPlanRules.CleanTitle(errors, "title", title);
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        var version = TreatmentPlanRules.ParseVersion(rowVersion);
        if (t == plan.Title) return await GetAsync(planId, ct);

        db.Entry(plan).Property(p => p.RowVersion).OriginalValue = version;
        var now = clock.UtcNow;
        plan.Title = t!;
        Touch(plan, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.Renamed, null, t, null, actor, now);
        AuditService.Record(db, "TreatmentPlanRenamed", nameof(TreatmentPlan), planId, actor, "Treatment plan title changed.");
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    /// <summary>Withdraws a plan with a reason. Withdrawn is final; withdrawing a withdrawn plan changes nothing. Its items and history stay readable.</summary>
    public async Task<PlanView> WithdrawAsync(Guid planId, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw PlanNotFound();
        if (plan.Status == TreatmentPlanStatuses.Withdrawn) return await GetAsync(planId, ct);
        var why = TreatmentPlanRules.RequireReason(reason, "Say why this treatment plan is being withdrawn.");
        db.Entry(plan).Property(p => p.RowVersion).OriginalValue = TreatmentPlanRules.ParseVersion(rowVersion);

        var now = clock.UtcNow;
        plan.Status = TreatmentPlanStatuses.Withdrawn;
        plan.WithdrawnAtUtc = now;
        plan.WithdrawnByUserId = actor;
        plan.WithdrawnReason = why;
        Touch(plan, actor, now);
        StageEvent(plan, await NextEventAsync(planId, ct), TreatmentPlanChanges.Withdrawn, null, null, why, actor, now);
        AuditService.Record(db, "TreatmentPlanWithdrawn", nameof(TreatmentPlan), planId, actor, "Treatment plan withdrawn.", why);
        await SaveAsync(planId, ct);
        return await GetAsync(planId, ct);
    }

    // ---------- shared ----------

    private static TreatmentPlanException PatientNotFound() => new("patient_not_found", "That patient was not found.", 404);
    private static TreatmentPlanException PlanNotFound() => new("plan_not_found", "That treatment plan was not found.", 404);

    private static void RequireProposed(TreatmentPlan plan)
    {
        if (plan.Status == TreatmentPlanStatuses.Withdrawn) throw new TreatmentPlanException("plan_withdrawn", "That treatment plan has been withdrawn, so it cannot be changed.", 409);
    }

    private static void Touch(TreatmentPlan plan, Guid actor, DateTimeOffset now)
    {
        plan.UpdatedAtUtc = now;
        plan.UpdatedByUserId = actor;
    }

    private async Task<int> NextEventAsync(Guid planId, CancellationToken ct) =>
        (await db.TreatmentPlanEvents.AsNoTracking().Where(e => e.PlanId == planId).Select(e => (int?)e.EventNumber).MaxAsync(ct) ?? 0) + 1;

    private void StageEvent(TreatmentPlan plan, int number, string changeType, Guid? itemId, string? title, string? reason, Guid actor, DateTimeOffset now) =>
        db.TreatmentPlanEvents.Add(new TreatmentPlanEvent { Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, EventNumber = number, ChangeType = changeType, ItemId = itemId, Title = title, Reason = reason, ActorUserId = actor, OccurredAtUtc = now });

    private async Task SaveAsync(Guid planId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(TreatmentPlan), planId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException(nameof(TreatmentPlan), planId); // two writers claimed the same next item number, event number or save key
        }
        catch (DbUpdateException ex)
        {
            // the whole save was one transaction, so nothing was stored; say so without leaking internals. Error class only in the log: never the plan, the patient or the SQL text
            db.ChangeTracker.Clear();
            logger?.LogError("treatment_plan_save_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
            throw new TreatmentPlanException("save_failed", "The treatment plan could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503, inner: ex);
        }
    }
}
