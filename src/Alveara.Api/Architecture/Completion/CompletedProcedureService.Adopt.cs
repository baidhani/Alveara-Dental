using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// ALV-008-C02: adopting ONE procedure that was completed the old, state-only way. The old completion recorded only that the procedure was completed: no provider, encounter, performed time, chart effect or
/// charge. Adoption supplies what is missing, explicitly, with a reason, and links it to the existing history without changing it.
///
/// What it does, in one transaction: records an adoption completion set (carrying the reason), the immutable completed-procedure record, the chart effect (an eligible finding moves to Completed through the chart's
/// own rules), and the procedure's one charge through the billing contract (an existing manual charge is reused, otherwise exactly one is created; a zero fee has none). It advances the plan's version so a stale
/// screen is refused, and writes one PHI-free <c>CompletionAdopted</c> audit entry.
///
/// What it deliberately does not do: it stages no new completion state and no new <c>ItemCompleted</c> event (the procedure's original Completed state row, event and audit entry stay exactly as they were), and it
/// never writes a ledger entry itself. It enforces the same provider, encounter, performed-time (no exemption for an old acceptance time), chart and billing rules as <see cref="CompleteAsync"/>.
///
/// The request's identity (<see cref="CompletionRules.AdoptionHash"/>) binds the operation, the item, the encounter, the provider, the performed time and the reason, so a replay must be identical and anything else
/// under the same key is <c>idempotency_conflict</c>, including a key that an ordinary completion of the same plan already used.
/// </summary>
public partial class CompletedProcedureService
{
    public async Task<CompletionResult> AdoptAsync(Guid planId, Guid itemId, AdoptCompletionRequest request, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw TreatmentPlanService.MissingPlan();
        var version = TreatmentPlanRules.ParseVersion(request.RowVersion);
        var input = CompletionRules.CleanForAdoption(request, itemId, clock.UtcNow);
        var hash = CompletionRules.AdoptionHash(input);

        var saved = await db.CompletionSets.AsNoTracking().SingleOrDefaultAsync(s => s.PlanId == planId && s.IdempotencyKey == input.Key, ct);
        if (saved is not null)
        {
            if (saved.RequestHash != hash)
                throw new TreatmentPlanException("idempotency_conflict", "That save key was already used for a different request. Use a new key to adopt something else.", 409);
            return await ResultAsync(saved, replayed: true, ct);
        }

        TreatmentPlanService.RequireOpen(plan);
        var item = await LoadForAdoptionAsync(plan, input, ct);
        TreatmentPlanService.RequireVersion(plan, version);
        return await RecordAdoptionAsync(plan, input, hash, version, item, actor, ct);
    }

    /// <summary>
    /// Reads what the request refers to and checks it: the procedure is on the plan, not withdrawn, currently Completed and without a completion record (so it really is a legacy completion), the encounter and
    /// provider are real, and the performed time is not before the plan or the recorded acceptance. Nothing is written.
    /// </summary>
    private async Task<TreatmentPlanItem> LoadForAdoptionAsync(TreatmentPlan plan, CleanAdoption input, CancellationToken ct)
    {
        var item = await db.TreatmentPlanItems.AsNoTracking().SingleOrDefaultAsync(i => i.Id == input.ItemId && i.PlanId == plan.Id, ct)
            ?? throw new TreatmentPlanException("item_not_found", "That item was not found in this treatment plan.", 404);
        if (item.WithdrawnAtUtc is not null) throw new TreatmentPlanException("item_withdrawn", $"Procedure {item.ItemNumber} was withdrawn from the plan, so it cannot be adopted.", 409);

        var rows = (await plans.StateHistoryAsync(plan.Id, ct))[item.Id].OrderBy(s => s.Sequence).ToList();
        if (rows.Count == 0 || TreatmentPlanService.CompletionOfState(rows[^1]) != ItemCompletion.Completed)
            throw new TreatmentPlanException("not_a_legacy_completion", $"Procedure {item.ItemNumber} has not been completed, so there is nothing to adopt. Complete it with Review and complete.", 409);
        if (await db.CompletedProcedures.AsNoTracking().AnyAsync(c => c.PlanItemId == item.Id, ct))
            throw new TreatmentPlanException("not_a_legacy_completion", $"Procedure {item.ItemNumber} already has a completion record, so it is not a legacy completion.", 409);

        var errors = new Dictionary<string, string>();
        var encounter = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == input.EncounterId, ct);
        if (encounter is null || encounter.PatientId != plan.PatientId) errors["encounterId"] = "Choose one of this patient's encounters.";
        var provider = await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == input.ProviderId, ct);
        if (provider is null || !provider.IsActive) errors["providerId"] = "Choose an active provider.";
        var problem = PerformedTimeProblem(input.PerformedAtUtc, plan.CreatedAtUtc, item.ItemNumber, CompletionRules.AcceptedAt(rows));   // the acceptance in force counts even though the procedure is already Completed
        if (problem is not null) errors["performedAtUtc"] = problem;
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        return item;
    }

    private async Task<CompletionResult> RecordAdoptionAsync(TreatmentPlan snapshot, CleanAdoption input, string hash, byte[] version, TreatmentPlanItem item, Guid actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var set = new CompletionSet
        {
            Id = Guid.NewGuid(), PatientId = snapshot.PatientId, PlanId = snapshot.Id, IdempotencyKey = input.Key, RequestHash = hash, CorrelationId = Guid.NewGuid(),
            PlanRevisionNumber = snapshot.CurrentRevision, ItemCount = 1, AdoptionReason = input.Reason, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        var started = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            IReadOnlyList<TreatmentPlanItem> items = [item];
            var choices = await ResolveChartAsync(snapshot, items, ct);
            RequireNoAmbiguity(items, choices);                                      // refused before anything is written

            // the plan's version is checked and advanced by the same statement, so a change made since the person read the plan cannot be overwritten; no state row or plan event is staged (the legacy ones stand)
            var plan = await db.TreatmentPlans.SingleAsync(p => p.Id == snapshot.Id, ct);
            db.Entry(plan).Property(p => p.RowVersion).OriginalValue = version;
            set.PlanRevisionNumber = plan.CurrentRevision;
            TreatmentPlanService.MarkChanged(plan, actor, now);
            db.CompletionSets.Add(set);
            await plans.SaveStagedAsync(plan.Id, ct);

            var row = CompletedRow(set, plan, item, new CleanCompletion(input.Key, input.EncounterId, input.ProviderId, input.PerformedAtUtc, [item.Id]), actor, now);
            (row.ChartOutcome, row.ToothFindingId) = (choices[item.Id].Outcome, choices[item.Id].FindingId);
            db.CompletedProcedures.Add(row);
            await StageChartAsync(items, choices, actor, now, ct);
            await plans.SaveStagedAsync(plan.Id, ct);

            var charges = await ChargeAsync(set, items, actor, ct);
            var moved = choices.Values.Count(c => c.Outcome == ChartOutcomes.Updated);
            AuditService.Record(db, "CompletionAdopted", nameof(CompletionSet), set.Id, actor,
                $"Legacy completion adopted: 1 procedure, {Plural(charges, "charge")} created, {Plural(moved, "chart finding")} completed (correlation {set.CorrelationId:N}).");
            await plans.SaveStagedAsync(plan.Id, ct);
            await transaction.CommitAsync(ct);
        }
        catch (TreatmentPlanException ex) when (ex.Code == "save_failed")
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("completion_adoption_not_recorded error_class={ErrorClass} correlation_id={CorrelationId} duration_ms={DurationMs}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, set.CorrelationId, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            throw new TreatmentPlanException("save_failed", "The adoption could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503, inner: ex.InnerException);
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("completion_adoption_not_recorded error_class={ErrorClass} correlation_id={CorrelationId} duration_ms={DurationMs}", ex.GetType().Name, set.CorrelationId, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            throw;
        }
        logger?.LogInformation("completion_adopted correlation_id={CorrelationId} duration_ms={DurationMs}", set.CorrelationId, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        return await ResultAsync(set, replayed: false, ct);
    }
}
