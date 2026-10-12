using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Architecture.Completion;

/// <summary>The reads and checks before a set is recorded: that every selected procedure, the encounter and the provider are real and belong together, that the performed time is sensible, and that each procedure can still be completed.</summary>
public partial class CompletedProcedureService
{
    private sealed record Loaded(IReadOnlyList<TreatmentPlanItem> Selected, IReadOnlyDictionary<Guid, TreatmentPlanItemState> Latest);

    /// <summary>Reads what the request refers to and checks it. Every wrong field is named in one answer (400); nothing is written.</summary>
    private async Task<Loaded> LoadAsync(TreatmentPlan plan, CleanCompletion input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => i.PlanId == plan.Id).OrderBy(i => i.ItemNumber).ToListAsync(ct);
        var selected = items.Where(i => input.ItemIds.Contains(i.Id)).ToList();
        if (selected.Count != input.ItemIds.Count) errors["itemIds"] = "Some of the selected procedures are not on this plan.";

        var encounter = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(e => e.Id == input.EncounterId, ct);
        if (encounter is null || encounter.PatientId != plan.PatientId) errors["encounterId"] = "Choose one of this patient's encounters.";
        var provider = await db.ProviderProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.Id == input.ProviderId, ct);
        if (provider is null || !provider.IsActive) errors["providerId"] = "Choose an active provider.";

        var history = await plans.StateHistoryAsync(plan.Id, ct);
        var latest = history.Where(g => selected.Any(i => i.Id == g.Key)).ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.Sequence).First());
        if (!errors.ContainsKey("performedAtUtc") && selected.Count == input.ItemIds.Count) CheckPerformedTime(plan, input, selected, history, errors);

        // an item with no state row has not been answered, so it has no latest state; it is refused as not accepted below
        if (errors.Count > 0) throw TreatmentPlanRules.Invalid(errors);
        return new Loaded(selected, latest);
    }

    /// <summary>Why a performed time is not allowed for a procedure, or null when it is: before its plan was created, or before the acceptance in force was recorded. One rule for a completion and for an amendment.</summary>
    private string? PerformedTimeProblem(DateTimeOffset performed, DateTimeOffset planCreatedAt, int itemNumber, DateTimeOffset? acceptedAt)
    {
        if (performed < planCreatedAt) return $"The performed time cannot be before this plan was created ({Local(planCreatedAt)}).";
        if (acceptedAt is { } at && performed < at)
            return $"Procedure {itemNumber} was recorded as accepted at {Local(at)}, and a procedure cannot be performed before that. " +
                "The comparison uses the time the acceptance was recorded in this plan, not when the patient may have agreed.";
        return null;
    }

    /// <summary>The performed time may not precede the plan or the acceptance in force. The acceptance is compared by the time it was recorded in the plan, which is when staff entered the answer.</summary>
    private void CheckPerformedTime(TreatmentPlan plan, CleanCompletion input, IReadOnlyList<TreatmentPlanItem> selected, ILookup<Guid, TreatmentPlanItemState> history, Dictionary<string, string> errors)
    {
        foreach (var item in selected)
        {
            var rows = history[item.Id].ToList();
            var open = rows.Count == 0 || TreatmentPlanService.CompletionOfState(rows.OrderByDescending(s => s.Sequence).First()) != ItemCompletion.Completed;
            var problem = PerformedTimeProblem(input.PerformedAtUtc, plan.CreatedAtUtc, item.ItemNumber, open ? CompletionRules.AcceptedAt(rows) : null);
            if (problem is null) continue;
            errors["performedAtUtc"] = problem;
            return;
        }
    }

    /// <summary>Why a procedure cannot be completed now, or null when it can: withdrawn, already completed, or not accepted. One rule for the completion and for the readiness view.</summary>
    internal static string? RefusalReason(TreatmentPlanItem item, TreatmentPlanItemState? latest)
    {
        if (item.WithdrawnAtUtc is not null) return "withdrawn";
        if (TreatmentPlanService.CompletionOfState(latest) == ItemCompletion.Completed) return "already_completed";
        if (TreatmentPlanService.AcceptanceOfState(latest) != ItemAcceptance.Accepted) return "not_accepted";
        return null;
    }

    /// <summary>Every selected procedure must be on the plan, not withdrawn, accepted and not yet completed; otherwise none is completed and each reason is listed.</summary>
    private static void RequireCompletable(Loaded loaded)
    {
        var refusals = loaded.Selected.Select(i => (Item: i, Reason: RefusalReason(i, loaded.Latest.GetValueOrDefault(i.Id)))).Where(x => x.Reason is not null)
            .Select(x => new ItemRefusal(x.Item.Id, x.Item.ItemNumber, x.Reason!)).ToList();
        if (refusals.Count > 0) throw new ItemsNotCompletableException(refusals);
    }

    private string Local(DateTimeOffset utc) => clock.ToPracticeLocal(utc).ToString("g", System.Globalization.CultureInfo.InvariantCulture);
}
