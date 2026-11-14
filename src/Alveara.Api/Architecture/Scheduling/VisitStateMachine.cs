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
/// ALV-011-C01, rewritten by ALV-N021 A2 (briefing 5.1, 5.2, OD-01, OD-02): the complete visit-state rule, as one pure function with no I/O so it is tested on every pair of states and
/// every combination of the two settings.
///
/// One path, no shortcuts: Scheduled -> Confirmed -> Checked in -> Ready -> Seated -> In treatment -> Ready for checkout (CheckedOut) -> Completed. STORY-011's shortcuts (check-in straight
/// to treatment or to completed, treatment straight to completed) are REMOVED. Two steps are settings: with confirmation not required a visit may also be checked in from Scheduled; with
/// mark-ready not required a patient may also be seated straight from Checked in. Completed is final. Asking for the state the visit is already in is a no-op, so a retry is safe.
/// Who may make a move is <see cref="VisitMovePolicy"/>; whether the visit may be completed is also the checkout gate.
/// </summary>
public static class VisitStateMachine
{
    private static IReadOnlyList<string> EdgesFrom(string current, VisitSettings settings) => current switch
    {
        VisitStates.Scheduled => settings.ConfirmationRequired ? [VisitStates.Confirmed] : [VisitStates.Confirmed, VisitStates.CheckedIn],
        VisitStates.Confirmed => [VisitStates.CheckedIn],
        VisitStates.CheckedIn => settings.MarkReadyRequired ? [VisitStates.Ready] : [VisitStates.Ready, VisitStates.Seated],
        VisitStates.Ready => [VisitStates.Seated],
        VisitStates.Seated => [VisitStates.InTreatment],
        VisitStates.InTreatment => [VisitStates.CheckedOut],
        VisitStates.CheckedOut => [VisitStates.Completed],
        _ => [],
    };

    /// <summary>With both settings at their default (required).</summary>
    public static FlowDecision Decide(string? current, string? requested) => Decide(current, requested, VisitSettings.Default);

    public static FlowDecision Decide(string? current, string? requested, VisitSettings settings)
    {
        if (!VisitStates.IsKnown(current) || !VisitStates.IsKnown(requested)) return FlowDecision.Refused;
        if (current == requested) return FlowDecision.NoChange;
        return EdgesFrom(current!, settings).Contains(requested!) ? FlowDecision.Move : FlowDecision.Refused;
    }

    /// <summary>The states a visit may move to next (empty for an unknown state or a finished visit), in chain order.</summary>
    public static IReadOnlyList<string> NextStates(string? current) => NextStates(current, VisitSettings.Default);

    public static IReadOnlyList<string> NextStates(string? current, VisitSettings settings) =>
        VisitStates.IsKnown(current) ? [.. VisitStates.InOrder.Where(s => EdgesFrom(current!, settings).Contains(s))] : [];

    /// <summary>
    /// Whether a move INTO this state is front-office or chairside work. ALV-N021 A2: ending treatment (CheckedOut) is now chairside work (the booked provider), and completing is front-office
    /// work (a clinician may complete only through <see cref="Identity.Permission.CompleteOwnStartedVisit"/>, decided by <see cref="VisitMovePolicy"/>).
    /// </summary>
    public static TransitionDuty DutyFor(string target) => target switch
    {
        VisitStates.Confirmed or VisitStates.CheckedIn or VisitStates.Completed => TransitionDuty.FrontOffice,
        _ => TransitionDuty.Chairside,
    };
}
