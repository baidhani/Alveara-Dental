using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Architecture.Treatment;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// ALV-008-C01: completes a selected set of one plan's procedures as one trustworthy operation (the checks are in <c>CompletedProcedureService.Context.cs</c>, the chart effect in <c>.Chart.cs</c>, the charge in
/// <c>.Charge.cs</c> and the pre-submit view in <c>.Readiness.cs</c>).
///
/// How the promises are kept:
/// - <b>One transaction.</b> The plan's version, every procedure's completion state, history event and audit entry, the completion set, every completed-procedure record, each chart finding that moves and each
///   charge are written inside one explicit transaction; if anything fails (a charge, the chart, an audit write) the transaction is rolled back and the caller is told so. Nothing is half-recorded.
/// - <b>Stale protection.</b> The request carries the plan version the person read. It is checked before anything is staged and again, atomically, by the save that advances the plan's version, so two sets that
///   overlap on a procedure end with exactly one winner and the other a conflict.
/// - <b>Retries are safe.</b> The same key with the same canonical request returns the saved result with no writes (even from an out-of-date screen); the same key with a different request is
///   <c>idempotency_conflict</c>; the same procedures under a new key after success are refused as already completed.
/// - <b>The chart and the ledger are touched only through their own services</b> (a finding is found only through the plan's diagnosis link; a charge is made only through <see cref="IProcedureCharging"/>).
/// </summary>
public partial class CompletedProcedureService(AlveraDbContext db, IPracticeClock clock, TreatmentPlanService plans, OdontogramService chart, IProcedureCharging charging, ILogger<CompletedProcedureService>? logger = null)
{
    public async Task<CompletionResult> CompleteAsync(Guid planId, CompleteProceduresRequest request, Guid actor, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct) ?? throw TreatmentPlanService.MissingPlan();
        var version = TreatmentPlanRules.ParseVersion(request.RowVersion);
        var input = CompletionRules.Clean(request, clock.UtcNow);
        var hash = CompletionRules.RequestHash(input);

        var saved = await db.CompletionSets.AsNoTracking().SingleOrDefaultAsync(s => s.PlanId == planId && s.IdempotencyKey == input.Key, ct);
        if (saved is not null)
        {
            if (saved.RequestHash != hash)
                throw new TreatmentPlanException("idempotency_conflict", "That save key was already used for a different completion. Use a new key to complete something else.", 409);
            return await ResultAsync(saved, replayed: true, ct);
        }

        TreatmentPlanService.RequireOpen(plan);
        var loaded = await LoadAsync(plan, input, ct);
        TreatmentPlanService.RequireVersion(plan, version);
        RequireCompletable(loaded);
        return await RecordAsync(plan, input, hash, version, loaded, actor, ct);
    }

    private async Task<CompletionResult> RecordAsync(TreatmentPlan snapshot, CleanCompletion input, string hash, byte[] version, Loaded loaded, Guid actor, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var set = new CompletionSet
        {
            Id = Guid.NewGuid(), PatientId = snapshot.PatientId, PlanId = snapshot.Id, IdempotencyKey = input.Key, RequestHash = hash, CorrelationId = Guid.NewGuid(),
            PlanRevisionNumber = snapshot.CurrentRevision, ItemCount = loaded.Selected.Count, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        var started = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var choices = await ResolveChartAsync(snapshot, loaded.Selected, ct);
            RequireNoAmbiguity(loaded.Selected, choices);                           // refused before anything is written

            // the plan's version is checked and advanced by the same statement, so a change made since the person read the plan cannot be overwritten
            var plan = await db.TreatmentPlans.SingleAsync(p => p.Id == snapshot.Id, ct);
            db.Entry(plan).Property(p => p.RowVersion).OriginalValue = version;
            set.PlanRevisionNumber = plan.CurrentRevision;
            await plans.StageCompletionsAsync(plan, loaded.Selected, loaded.Latest, actor, now, ct);
            TreatmentPlanService.MarkChanged(plan, actor, now);
            db.CompletionSets.Add(set);
            await plans.SaveStagedAsync(plan.Id, ct);                          // the state rows exist before a completed procedure can refer to them

            await StageChartAsync(loaded.Selected, choices, actor, now, ct);
            await plans.SaveStagedAsync(plan.Id, ct);                          // the chart's moves and their history exist before a record can name the version that shows its finding Completed
            var versions = await ChartVersionsAsync(choices, ct);
            foreach (var item in loaded.Selected)
            {
                var row = CompletedRow(set, plan, item, input, actor, now);
                (row.ChartOutcome, row.ToothFindingId) = (choices[item.Id].Outcome, choices[item.Id].FindingId);
                row.ToothFindingVersionNumber = row.ToothFindingId is Guid finding ? versions[finding] : null;
                db.CompletedProcedures.Add(row);
            }
            await plans.SaveStagedAsync(plan.Id, ct);

            var charges = await ChargeAsync(set, loaded.Selected, actor, ct);
            var moved = choices.Values.Count(c => c.Outcome == ChartOutcomes.Updated);
            AuditService.Record(db, "CompletionSetRecorded", nameof(CompletionSet), set.Id, actor,
                $"Completion set recorded: {Plural(loaded.Selected.Count, "procedure")} completed, {Plural(charges, "charge")} created, {Plural(moved, "chart finding")} completed (correlation {set.CorrelationId:N}).");
            await plans.SaveStagedAsync(plan.Id, ct);
            await transaction.CommitAsync(ct);
        }
        catch (TreatmentPlanException ex) when (ex.Code == "save_failed")
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("completion_set_not_recorded error_class={ErrorClass} correlation_id={CorrelationId} duration_ms={DurationMs}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, set.CorrelationId, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            throw new TreatmentPlanException("save_failed", "The completion could not be saved, so nothing was completed. Try again; if it keeps failing, contact support.", 503, inner: ex.InnerException);     // say it about the completion, not the plan
        }
        catch (Exception ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("completion_set_not_recorded error_class={ErrorClass} correlation_id={CorrelationId} duration_ms={DurationMs}", ex.GetType().Name, set.CorrelationId, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
            throw;
        }
        logger?.LogInformation("completion_set_recorded correlation_id={CorrelationId} procedures={Count} duration_ms={DurationMs}", set.CorrelationId, loaded.Selected.Count, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        return await ResultAsync(set, replayed: false, ct);
    }

    private static string Plural(int count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static CompletedProcedure CompletedRow(CompletionSet set, TreatmentPlan plan, TreatmentPlanItem item, CleanCompletion input, Guid actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), SetId = set.Id, PatientId = plan.PatientId, PlanId = plan.Id, PlanItemId = item.Id, PlanRevisionNumber = plan.CurrentRevision, ProcedureVersionId = item.ProcedureVersionId,
        ToothKey = item.ToothKey, Surface = item.Surface, EncounterId = input.EncounterId, ProviderProfileId = input.ProviderId, PerformedAtUtc = input.PerformedAtUtc,
        ChartOutcome = ChartOutcomes.NotApplicable, FinalizedAtUtc = now, FinalizedByUserId = actor,
    };

    /// <summary>The set as it was recorded (the original completion, not later amendments), with the plan as it stands now and each procedure's chart and charge effect.</summary>
    private async Task<CompletionResult> ResultAsync(CompletionSet set, bool replayed, CancellationToken ct)
    {
        var plan = await plans.GetAsync(set.PlanId, ct);
        var rows = await db.CompletedProcedures.AsNoTracking().Where(c => c.SetId == set.Id).ToListAsync(ct);
        var charges = await ChargeEffectsAsync(set, plan.Items.Where(i => rows.Any(r => r.PlanItemId == i.Id)).ToList(), ct);
        var views = rows.Select(r =>
        {
            var item = plan.Items.Single(i => i.Id == r.PlanItemId);
            return new CompletedProcedureView(r.Id, r.PlanItemId, item.ItemNumber, item.ProcedureCode, item.ProcedureDescription, r.ToothKey, r.Surface, r.EncounterId, r.ProviderProfileId,
                r.PerformedAtUtc, r.PlanRevisionNumber, r.ChartOutcome, r.ToothFindingId, charges[r.PlanItemId], set.AdoptionReason is not null, set.AdoptionReason);
        }).OrderBy(v => v.ItemNumber).ToList();
        return new CompletionResult(plan, set.Id, set.CorrelationId, replayed, views, set.AdoptionReason);
    }
}
