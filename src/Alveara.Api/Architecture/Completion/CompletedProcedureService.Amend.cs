using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>
/// Corrections to a completed procedure. The original record is finalized and never changes; a correction is a new, numbered, append-only amendment that may change only the provider, the performed time or the
/// encounter, with a reason. It never touches the plan's state, the chart or the charge. Stale protection does not use a row version (the original row never changes): the caller names the amendment it last
/// saw, and a correction made against an older one is a conflict. A retry is recognised by its own key and canonical request, and two corrections racing for the same number leave one winner.
/// </summary>
public partial class CompletedProcedureService
{
    public async Task<AmendmentResult> AmendAsync(Guid completedProcedureId, AmendCompletedProcedureRequest request, Guid actor, CancellationToken ct)
    {
        var row = await db.CompletedProcedures.AsNoTracking().SingleOrDefaultAsync(c => c.Id == completedProcedureId, ct) ?? throw NotFound();
        var input = CompletionRules.CleanForAmendment(request, clock.UtcNow);
        var hash = CompletionRules.AmendmentHash(input);
        var amendments = await db.CompletedProcedureAmendments.AsNoTracking().Where(a => a.CompletedProcedureId == row.Id).OrderBy(a => a.AmendmentNumber).ToListAsync(ct);

        var saved = amendments.FirstOrDefault(a => a.IdempotencyKey == input.Key);
        if (saved is not null)
        {
            if (saved.RequestHash != hash)
                throw new TreatmentPlanException("idempotency_conflict", "That save key was already used for a different correction. Use a new key to correct something else.", 409);
            return new AmendmentResult(await DetailAsync(row.Id, ct), saved.AmendmentNumber, Replayed: true);
        }

        var latest = amendments.Count == 0 ? 0 : amendments[^1].AmendmentNumber;
        if (input.ExpectedAmendmentNumber != latest) throw new ConcurrencyConflictException(nameof(CompletedProcedure), row.Id);
        var changed = await CheckAmendmentAsync(row, amendments, input, ct);

        var amendment = new CompletedProcedureAmendment
        {
            Id = Guid.NewGuid(), CompletedProcedureId = row.Id, PatientId = row.PatientId, AmendmentNumber = latest + 1, IdempotencyKey = input.Key, RequestHash = hash, Reason = input.Reason,
            ProviderProfileId = input.ProviderId, PerformedAtUtc = input.PerformedAtUtc, EncounterId = input.EncounterId, AmendedAtUtc = clock.UtcNow, AmendedByUserId = actor,
        };
        db.CompletedProcedureAmendments.Add(amendment);
        AuditService.Record(db, "CompletedProcedureAmended", nameof(CompletedProcedure), row.Id, actor, $"Completed procedure amended: {string.Join(", ", changed)}.", input.Reason);
        await SaveAmendmentAsync(row.Id, ct);
        return new AmendmentResult(await DetailAsync(row.Id, ct), amendment.AmendmentNumber, Replayed: false);
    }

    /// <summary>Checks what the correction refers to and that it changes something. Returns the names of the fields it changes (for the audit entry).</summary>
    private async Task<IReadOnlyList<string>> CheckAmendmentAsync(CompletedProcedure row, IReadOnlyList<CompletedProcedureAmendment> amendments, CleanAmendment input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        if (input.ProviderId is { } providerId)
        {
            var provider = await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == providerId, ct);
            if (provider is null || !provider.IsActive) errors["providerId"] = "Choose an active provider.";
        }
        if (input.EncounterId is { } encounterId)
        {
            var encounter = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == encounterId, ct);
            if (encounter is null || encounter.PatientId != row.PatientId) errors["encounterId"] = "Choose one of this patient's encounters.";
        }
        if (input.PerformedAtUtc is { } performed)
        {
            var plan = await db.TreatmentPlans.AsNoTracking().SingleAsync(p => p.Id == row.PlanId, ct);
            var item = await db.TreatmentPlanItems.AsNoTracking().SingleAsync(i => i.Id == row.PlanItemId, ct);
            var history = (await plans.StateHistoryAsync(row.PlanId, ct))[row.PlanItemId];
            var problem = PerformedTimeProblem(performed, plan.CreatedAtUtc, item.ItemNumber, CompletionRules.AcceptedAt(history));
            if (problem is not null) errors["performedAtUtc"] = problem;
        }
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);

        var currentProvider = amendments.LastOrDefault(a => a.ProviderProfileId is not null)?.ProviderProfileId ?? row.ProviderProfileId;
        var currentPerformed = amendments.LastOrDefault(a => a.PerformedAtUtc is not null)?.PerformedAtUtc ?? row.PerformedAtUtc;
        var currentEncounter = amendments.LastOrDefault(a => a.EncounterId is not null)?.EncounterId ?? row.EncounterId;
        var changed = new List<string>();
        if (input.ProviderId is { } p && p != currentProvider) changed.Add("provider");
        if (input.PerformedAtUtc is { } t && t != CompletionRules.NormalizeTime(currentPerformed)) changed.Add("performed time");
        if (input.EncounterId is { } e && e != currentEncounter) changed.Add("encounter");
        if (changed.Count == 0) throw TreatmentPlanRules.Invalid(new Dictionary<string, string> { ["correction"] = "That correction changes nothing: the procedure already has those values." });
        return changed;
    }

    /// <summary>Saves the staged amendment. A correction racing another for the same number (the unique index) is a conflict; any other failure stores nothing and says so (<c>save_failed</c>, 503).</summary>
    private async Task SaveAmendmentAsync(Guid completedProcedureId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(CompletedProcedure), completedProcedureId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(nameof(CompletedProcedure), completedProcedureId);     // another correction took the next number first
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogError("completed_procedure_amendment_save_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
            throw new TreatmentPlanException("save_failed", "The correction could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503, inner: ex);
        }
    }

    private static TreatmentPlanException NotFound() => new("completed_procedure_not_found", "That completed procedure was not found.", 404);
}
