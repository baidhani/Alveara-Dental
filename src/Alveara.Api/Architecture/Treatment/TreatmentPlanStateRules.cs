namespace Alveara.Api.Architecture.Treatment;

/// <summary>ALV-N023 (owner decision PD-14): the plan state a screen shows. Derived on every read from the plan and its items; nothing new is stored.</summary>
public static class PlanStates
{
    public const string Proposed = "Proposed";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Closed = "Closed";
    public const string Withdrawn = "Withdrawn";
}

/// <summary>ALV-N023: why a plan is Closed, so the screen can say it truthfully.</summary>
public static class ClosedReasons
{
    public const string NoProcedures = "NoProcedures";
    public const string AllDeclined = "AllDeclined";
    public const string CompletedOrDeclined = "CompletedOrDeclined";
}

/// <summary>
/// ALV-N023 (PD-14; briefing revision 2, section 4.6, amended by the owner's decisions of 2026-11-18 recorded in addendum 01): the one definition of a plan's state. The server computes it here for
/// the view, for the refusals and for the tests; the client and the browser tests read the server's value. Rules in order (the first that matches):
/// 1. plan withdrawn -> Withdrawn; 2. no remaining (not withdrawn) item -> Closed (no procedures remain); 3. every remaining item completed -> Completed; 4. no remaining item unanswered or
/// accepted-but-not-completed -> Closed (all declined, or completed and declined); 5. at least one remaining item accepted or completed -> InProgress; 6. otherwise -> Proposed.
/// LOCKED (read-only except history, patient copy and the warned "Revise anyway"; CR-3: no withdrawal) only when the plan is Completed or Closed AND contains completed care. A Closed plan with no
/// completed care (empty, or all declined) stays correctable: add, rename, change an answer, withdraw (owner, ambiguities 1 and 2).
/// </summary>
public static class TreatmentPlanStateRules
{
    public readonly record struct ItemFacts(bool Withdrawn, string Acceptance, string Completion);

    public static string Derive(string planStatus, IEnumerable<ItemFacts> items)
    {
        if (planStatus == TreatmentPlanStatuses.Withdrawn) return PlanStates.Withdrawn;
        var remaining = items.Where(i => !i.Withdrawn).ToList();
        if (remaining.Count == 0) return PlanStates.Closed;
        var completed = remaining.Count(i => i.Completion == ItemCompletion.Completed);
        if (completed == remaining.Count) return PlanStates.Completed;
        var open = remaining.Count(i => i.Completion != ItemCompletion.Completed && i.Acceptance is ItemAcceptance.Pending or ItemAcceptance.Accepted);
        if (open == 0) return PlanStates.Closed;
        var started = remaining.Count(i => i.Acceptance == ItemAcceptance.Accepted || i.Completion == ItemCompletion.Completed);
        return started > 0 ? PlanStates.InProgress : PlanStates.Proposed;
    }

    /// <summary>Why a Closed plan is closed (null for any other state).</summary>
    public static string? ClosedReason(string state, IEnumerable<ItemFacts> items)
    {
        if (state != PlanStates.Closed) return null;
        var remaining = items.Where(i => !i.Withdrawn).ToList();
        if (remaining.Count == 0) return ClosedReasons.NoProcedures;
        return remaining.Any(i => i.Completion == ItemCompletion.Completed) ? ClosedReasons.CompletedOrDeclined : ClosedReasons.AllDeclined;
    }

    /// <summary>A finished plan (Completed or Closed) that contains completed care is read-only except its history, its patient copy and the warned "Revise anyway" (no withdrawal).</summary>
    public static bool IsLocked(string state, IEnumerable<ItemFacts> items) =>
        state is PlanStates.Completed or PlanStates.Closed && items.Any(i => !i.Withdrawn && i.Completion == ItemCompletion.Completed);

    public static string LockedMessage(string state) => state == PlanStates.Completed
        ? "Every procedure on this treatment plan is completed, so the plan is finished and can no longer be changed. To correct it, use Revise anyway."
        : "This treatment plan is closed and contains completed care, so it can no longer be changed. To correct it, use Revise anyway.";
}
