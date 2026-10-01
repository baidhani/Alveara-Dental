namespace Alveara.Api.Architecture.Lifecycle;

/// <summary>
/// ALV-002-C01: the named set of record-lifecycle actions a future clinical/financial domain may
/// need (a finalized clinical note, a completed procedure, a signed form, a reversed charge). This
/// enum and the guard below are deliberately the ONLY thing this story ships for lifecycle
/// semantics - no domain record, no state machine, no rule about which actions apply to which
/// record type. Each owning domain story (clinical notes, procedure completion, billing reversal,
/// etc.) defines its own record type, its own current-state representation, and its own allowed-
/// transition set, then calls <see cref="RecordLifecycleGuard.EnsureAllowed"/> with that set - this
/// primitive only ever enforces "the requested transition must be one the caller explicitly
/// allowed from the record's current state," never inventing a rule of its own.
/// </summary>
public enum RecordLifecycleAction
{
    Finalize,
    Amend,
    Addendum,
    Void,
    Reversal,
    Inactivate,
}

/// <summary>Thrown when a domain requests a lifecycle transition outside the set it declared as
/// valid from the record's current state - the "unknown lifecycle transition requested by a
/// domain" failure path.</summary>
public sealed class UnknownLifecycleTransitionException(RecordLifecycleAction requested)
    : InvalidOperationException($"'{requested}' is not an allowed lifecycle transition for this record's current state.")
{
    public RecordLifecycleAction Requested { get; } = requested;
}

public static class RecordLifecycleGuard
{
    public static void EnsureAllowed(RecordLifecycleAction requested, IReadOnlySet<RecordLifecycleAction> allowedFromCurrentState)
    {
        if (!allowedFromCurrentState.Contains(requested))
        {
            throw new UnknownLifecycleTransitionException(requested);
        }
    }
}
