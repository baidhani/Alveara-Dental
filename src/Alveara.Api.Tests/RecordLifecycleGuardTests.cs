using Alveara.Api.Architecture.Lifecycle;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-002-C01: unit tests for the shared lifecycle-transition guard only - deliberately not
/// against any real clinical/financial record (none exists yet, and inventing one would be out of
/// scope for this story). These prove the generic mechanism a future domain will call with its own
/// record type and its own allowed-transition set.
/// </summary>
public class RecordLifecycleGuardTests
{
    [Fact]
    public void A_requested_transition_within_the_allowed_set_does_not_throw()
    {
        var allowed = new HashSet<RecordLifecycleAction> { RecordLifecycleAction.Finalize, RecordLifecycleAction.Amend };

        var exception = Record.Exception(() => RecordLifecycleGuard.EnsureAllowed(RecordLifecycleAction.Finalize, allowed));

        Assert.Null(exception);
    }

    [Fact]
    public void A_requested_transition_outside_the_allowed_set_throws_UnknownLifecycleTransitionException()
    {
        var allowed = new HashSet<RecordLifecycleAction> { RecordLifecycleAction.Finalize };

        var ex = Assert.Throws<UnknownLifecycleTransitionException>(
            () => RecordLifecycleGuard.EnsureAllowed(RecordLifecycleAction.Void, allowed));

        Assert.Equal(RecordLifecycleAction.Void, ex.Requested);
    }

    [Fact]
    public void An_empty_allowed_set_rejects_every_transition()
    {
        var allowed = new HashSet<RecordLifecycleAction>();

        foreach (var action in Enum.GetValues<RecordLifecycleAction>())
        {
            Assert.Throws<UnknownLifecycleTransitionException>(() => RecordLifecycleGuard.EnsureAllowed(action, allowed));
        }
    }
}
