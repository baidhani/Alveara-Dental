namespace Alveara.Api.Architecture.Scheduling;

/// <summary>
/// STORY-011: where a patient is in the visit - the "patient flow" of one appointment.
///
/// This is deliberately separate from <see cref="AppointmentStatuses"/>. Status says whether the appointment still holds time
/// (Scheduled / Cancelled / NoShow) and drives every conflict rule; flow says how far the visit has got. Flow only applies to an
/// appointment whose Status is Scheduled, so the conflict rules and the calendar are unchanged: a checked-in, in-treatment or
/// completed appointment still holds its time.
///
/// Flow only moves forward: Scheduled -> CheckedIn -> InTreatment -> Completed. InTreatment may be skipped (a visit can go straight
/// from check-in to completed), but nothing may be skipped before CheckedIn, and nothing moves backwards.
/// </summary>
public static class PatientFlowStates
{
    public const string Scheduled = "Scheduled";
    public const string CheckedIn = "CheckedIn";
    public const string InTreatment = "InTreatment";
    public const string Completed = "Completed";

    /// <summary>The states in the order a visit passes through them.</summary>
    public static readonly IReadOnlyList<string> InOrder = [Scheduled, CheckedIn, InTreatment, Completed];

    public static bool IsKnown(string state) => InOrder.Contains(state);
}

/// <summary>What the transition rule decided about a requested move.</summary>
public enum FlowDecision
{
    /// <summary>The move is allowed and changes the state.</summary>
    Move,
    /// <summary>The appointment is already in the requested state: nothing changes (a retry is safe).</summary>
    NoChange,
    /// <summary>The move is not allowed (backwards, skipping check-in, or an unknown state).</summary>
    Refused,
}

/// <summary>The transition rules for patient flow, as one pure function with no I/O so they can be tested exhaustively.</summary>
public static class PatientFlowRules
{
    public static FlowDecision Decide(string current, string requested)
    {
        if (!PatientFlowStates.IsKnown(current) || !PatientFlowStates.IsKnown(requested))
            return FlowDecision.Refused;
        if (current == requested)
            return FlowDecision.NoChange;

        var from = IndexOf(current);
        var to = IndexOf(requested);

        // Never backwards, and never back to Scheduled: a check-in that was a mistake is a correction for a later story, not a silent undo.
        if (to < from) return FlowDecision.Refused;

        // The only allowed skip is InTreatment (CheckedIn -> Completed). Scheduled must pass through CheckedIn.
        if (to - from == 1) return FlowDecision.Move;
        if (current == PatientFlowStates.CheckedIn && requested == PatientFlowStates.Completed) return FlowDecision.Move;
        return FlowDecision.Refused;
    }

    private static int IndexOf(string state) => PatientFlowStates.InOrder.ToList().IndexOf(state);
}
