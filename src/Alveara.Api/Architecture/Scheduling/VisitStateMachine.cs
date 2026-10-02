namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// ALV-011-C01: every state a visit can be in, from booking to the end of the day. Cancelled and no-show are deliberately NOT here: they are the booking
/// <see cref="AppointmentStatuses"/>, so they stay distinct from "completed" by construction (a cancelled appointment never has a visit state).
///
/// The four states STORY-011 defined keep their exact values (they are reused from <see cref="PatientFlowStates"/>, not copied), so every stored appointment
/// and every STORY-011 test still means what it did. The new states are the ones the full workflow needs around them:
/// Confirmed (before arrival), Ready (arrived and the room is ready), Seated (in the operatory) and CheckedOut (treatment done, leaving).
/// </summary>
public static class VisitStates
{
    public const string Scheduled = PatientFlowStates.Scheduled;
    public const string Confirmed = "Confirmed";
    public const string CheckedIn = PatientFlowStates.CheckedIn;
    public const string Ready = "Ready";
    public const string Seated = "Seated";
    public const string InTreatment = PatientFlowStates.InTreatment;
    public const string CheckedOut = "CheckedOut";
    public const string Completed = PatientFlowStates.Completed;

    /// <summary>The production chain, in the order a visit normally passes through it.</summary>
    public static readonly IReadOnlyList<string> InOrder = [Scheduled, Confirmed, CheckedIn, Ready, Seated, InTreatment, CheckedOut, Completed];

    public static bool IsKnown(string? state) => state is not null && InOrder.Contains(state);

    /// <summary>
    /// True once the patient is in the building. Before that (Scheduled, Confirmed) the appointment can still be rescheduled, cancelled or marked no-show;
    /// from check-in on it cannot (STORY-011's rule, extended to the new states).
    /// </summary>
    public static bool HasArrived(string state) => IsKnown(state) && state != Scheduled && state != Confirmed;

    /// <summary>True for the states that occupy an operatory: the patient is in the chair area and the room cannot be given to anyone else.</summary>
    public static bool OccupiesOperatory(string state) => state is Seated or InTreatment;
}

/// <summary>Who normally performs a move, so the permission can match the work: the front office or the chairside team.</summary>
public enum TransitionDuty
{
    FrontOffice,
    Chairside,
}

/// <summary>
/// ALV-011-C01: the complete visit-state rule, as one pure function with no I/O so it can be tested on every pair of states.
///
/// It is a SUPERSET of STORY-011's <see cref="PatientFlowRules"/>: for any two of the four original states it gives exactly STORY-011's answer (a test
/// compares every pair), so STORY-011's completion contract cannot drift. On top of that it adds the Confirmed, Ready, Seated and CheckedOut steps.
///
/// Rules: a visit moves forward only, along the edges below. It may not skip check-in, may not go back (a mistaken move is a correction for a later story,
/// not a silent undo), and Completed is final. Asking for the state the visit is already in is a no-op, so a retry is safe.
/// </summary>
public static class VisitStateMachine
{
    // The allowed forward moves. The three marked (011) are STORY-011's shortcuts and stay legal: a visit may go from check-in straight to treatment or to
    // completed, and from treatment straight to completed, so the receptionist-only flow STORY-011 shipped still works with no chairside steps recorded.
    private static readonly IReadOnlyDictionary<string, string[]> Edges = new Dictionary<string, string[]>
    {
        [VisitStates.Scheduled] = [VisitStates.Confirmed, VisitStates.CheckedIn],
        [VisitStates.Confirmed] = [VisitStates.CheckedIn],
        [VisitStates.CheckedIn] = [VisitStates.Ready, VisitStates.InTreatment /* 011 */, VisitStates.Completed /* 011 */],
        [VisitStates.Ready] = [VisitStates.Seated],
        [VisitStates.Seated] = [VisitStates.InTreatment],
        [VisitStates.InTreatment] = [VisitStates.CheckedOut, VisitStates.Completed /* 011 */],
        [VisitStates.CheckedOut] = [VisitStates.Completed],
        [VisitStates.Completed] = [],
    };

    public static FlowDecision Decide(string? current, string? requested)
    {
        if (!VisitStates.IsKnown(current) || !VisitStates.IsKnown(requested)) return FlowDecision.Refused;
        if (current == requested) return FlowDecision.NoChange;
        return Edges[current!].Contains(requested!) ? FlowDecision.Move : FlowDecision.Refused;
    }

    /// <summary>The states a visit may move to next (empty for an unknown state or a finished visit), in chain order, for the board's action buttons.</summary>
    public static IReadOnlyList<string> NextStates(string? current) =>
        VisitStates.IsKnown(current) ? [.. VisitStates.InOrder.Where(s => Edges[current!].Contains(s))] : [];

    /// <summary>Whether a move INTO this state is front-office or chairside work (decides which permission it needs).</summary>
    public static TransitionDuty DutyFor(string target) => target switch
    {
        VisitStates.Confirmed or VisitStates.CheckedIn or VisitStates.CheckedOut => TransitionDuty.FrontOffice,
        _ => TransitionDuty.Chairside,
    };
}
