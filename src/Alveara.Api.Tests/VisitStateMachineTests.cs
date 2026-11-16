using Alveara.Api.Architecture.Scheduling;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>ALV-011-C01: the complete visit-state rule on its own (pure logic, no database), and how it relates to STORY-011's rule. Updated for ALV-N021 A2 (default settings).</summary>
public class VisitStateMachineTests
{
    private const string S = VisitStates.Scheduled, Cf = VisitStates.Confirmed, Ci = VisitStates.CheckedIn, R = VisitStates.Ready,
        Se = VisitStates.Seated, T = VisitStates.InTreatment, Co = VisitStates.CheckedOut, D = VisitStates.Completed;

    // every allowed move under the default settings, written out once; the exhaustive test below proves nothing else is allowed.
    // ALV-N021 A2: one path (OD-01); STORY-011's shortcuts CheckedIn->InTreatment, CheckedIn->Completed, InTreatment->Completed are removed (OD-02);
    // with confirmation and marking ready required by default (OD-03, OD-04) Scheduled->CheckedIn is refused too. The settings-off table is in VisitPathTableTests.
    private static readonly (string, string)[] Moves =
    [
        (S, Cf), (Cf, Ci), (Ci, R), (R, Se), (Se, T), (T, Co), (Co, D),
    ];

    [Fact]
    public void A_visit_can_traverse_the_whole_production_chain_one_step_at_a_time()
    {
        var chain = new[] { S, Cf, Ci, R, Se, T, Co, D };
        for (var i = 0; i < chain.Length - 1; i++)
            Assert.Equal(FlowDecision.Move, VisitStateMachine.Decide(chain[i], chain[i + 1]));
        Assert.Equal(chain, VisitStates.InOrder);
    }

    [Fact]
    public void Every_pair_of_states_has_exactly_the_documented_outcome()
    {
        foreach (var from in VisitStates.InOrder)
            foreach (var to in VisitStates.InOrder)
            {
                var expected = from == to ? FlowDecision.NoChange : Moves.Contains((from, to)) ? FlowDecision.Move : FlowDecision.Refused;
                Assert.True(expected == VisitStateMachine.Decide(from, to), $"{from} -> {to} should be {expected}");
            }
    }

    [Fact]
    public void For_the_four_original_states_it_agrees_with_STORY_011_except_that_every_STORY_011_move_was_a_shortcut_and_is_now_refused()
    {
        // ALV-N021 A2 (OD-02, OD-03): among Scheduled, CheckedIn, InTreatment, Completed every move STORY-011 allowed skips a step of the one path, so each is refused;
        // repeats and refusals are unchanged.
        var storyMoves = 0;
        foreach (var from in PatientFlowStates.InOrder)
            foreach (var to in PatientFlowStates.InOrder)
            {
                var story = PatientFlowRules.Decide(from, to);
                var expected = story == FlowDecision.Move ? FlowDecision.Refused : story;
                if (story == FlowDecision.Move) storyMoves++;
                Assert.True(expected == VisitStateMachine.Decide(from, to), $"{from} -> {to} should be {expected} (STORY-011 said {story})");
            }
        Assert.Equal(4, storyMoves);   // Scheduled->CheckedIn, CheckedIn->InTreatment, CheckedIn->Completed, InTreatment->Completed
    }

    [Fact]
    public void The_original_four_states_keep_their_exact_stored_values()
    {
        Assert.Equal(["Scheduled", "CheckedIn", "InTreatment", "Completed"], PatientFlowStates.InOrder);
        Assert.Equal(("Scheduled", "CheckedIn", "InTreatment", "Completed"), (VisitStates.Scheduled, VisitStates.CheckedIn, VisitStates.InTreatment, VisitStates.Completed));
    }

    [Theory]
    [InlineData(Cf, S)]
    [InlineData(Ci, Cf)]
    [InlineData(R, Ci)]
    [InlineData(Se, R)]
    [InlineData(T, Se)]
    [InlineData(Co, T)]
    [InlineData(D, Co)]
    [InlineData(D, S)]
    public void Going_backwards_is_refused(string from, string to) =>
        Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(from, to));

    [Theory]
    [InlineData(S, R)]
    [InlineData(S, Se)]
    [InlineData(S, T)]
    [InlineData(S, Co)]
    [InlineData(S, D)]
    [InlineData(Cf, R)]
    [InlineData(Cf, Se)]
    [InlineData(Cf, T)]
    [InlineData(Cf, D)]
    public void Check_in_cannot_be_skipped(string from, string to) =>
        Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(from, to));

    [Theory]
    [InlineData(Ci, Se)] // the room must be made ready before the patient is seated
    [InlineData(R, T)]   // and the patient must be seated before treatment starts (ALV-N021 A2, OD-05: no shortcut at all)
    [InlineData(Se, Co)]
    [InlineData(Se, D)]
    [InlineData(R, D)]
    [InlineData(Ci, Co)]
    public void The_chairside_steps_cannot_be_skipped_once_a_patient_is_in_them(string from, string to) =>
        Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(from, to));

    [Fact]
    public void Completed_is_final_and_repeating_any_state_changes_nothing()
    {
        Assert.Empty(VisitStateMachine.NextStates(D));
        foreach (var s in VisitStates.InOrder) Assert.Equal(FlowDecision.NoChange, VisitStateMachine.Decide(s, s));
    }

    [Theory]
    [InlineData(null, Ci)]
    [InlineData("", Ci)]
    [InlineData(S, "Arrived")]
    [InlineData("completed", D)]
    [InlineData(S, null)]
    [InlineData("Cancelled", Ci)] // cancelled and no-show are booking statuses, never visit states
    [InlineData(Ci, "NoShow")]
    public void Unknown_states_are_refused_and_cancelled_and_no_show_are_not_visit_states(string? from, string? to)
    {
        Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(from, to));
        Assert.False(VisitStates.IsKnown("Cancelled"));
        Assert.False(VisitStates.IsKnown("NoShow"));
    }

    [Fact]
    public void NextStates_lists_exactly_the_moves_the_rule_allows_in_chain_order()
    {
        foreach (var from in VisitStates.InOrder)
        {
            var expected = VisitStates.InOrder.Where(to => VisitStateMachine.Decide(from, to) == FlowDecision.Move).ToArray();
            Assert.Equal(expected, VisitStateMachine.NextStates(from));
        }
        Assert.Equal([Cf], VisitStateMachine.NextStates(S));    // ALV-N021 A2 (OD-03): confirmation required by default
        Assert.Equal([R], VisitStateMachine.NextStates(Ci));    // OD-02 (no shortcuts), OD-04 (marking ready required by default)
        Assert.Empty(VisitStateMachine.NextStates("nonsense"));
        Assert.Empty(VisitStateMachine.NextStates(null));
    }

    [Fact]
    public void Every_state_other_than_Scheduled_can_be_reached_from_Scheduled()
    {
        var reached = new HashSet<string> { S };
        var frontier = new Queue<string>([S]);
        while (frontier.Count > 0)
            foreach (var next in VisitStateMachine.NextStates(frontier.Dequeue()))
                if (reached.Add(next)) frontier.Enqueue(next);
        Assert.Equal(VisitStates.InOrder.OrderBy(x => x), reached.OrderBy(x => x));
    }

    [Theory]
    [InlineData(S, false)]
    [InlineData(Cf, false)]
    [InlineData(Ci, true)]
    [InlineData(R, true)]
    [InlineData(Se, true)]
    [InlineData(T, true)]
    [InlineData(Co, true)]
    [InlineData(D, true)]
    [InlineData("nonsense", false)]
    public void An_appointment_can_still_be_rescheduled_or_cancelled_only_until_the_patient_arrives(string state, bool arrived) =>
        Assert.Equal(arrived, VisitStates.HasArrived(state));

    [Fact]
    public void Only_Seated_and_InTreatment_occupy_an_operatory()
    {
        Assert.Equal([Se, T], VisitStates.InOrder.Where(VisitStates.OccupiesOperatory));
    }

    [Theory]
    [InlineData(Cf, TransitionDuty.FrontOffice)]
    [InlineData(Ci, TransitionDuty.FrontOffice)]
    [InlineData(D, TransitionDuty.FrontOffice)]   // ALV-N021 A2 (OD-09): the front desk completes (or the clinician who started treatment, a separate rule)
    [InlineData(R, TransitionDuty.Chairside)]
    [InlineData(Se, TransitionDuty.Chairside)]
    [InlineData(T, TransitionDuty.Chairside)]
    [InlineData(Co, TransitionDuty.Chairside)]    // ALV-N021 A2 (OD-08, OD-10): "Ready for checkout" is the booked provider's End treatment, not the front desk's
    public void Each_move_is_front_office_or_chairside_work(string target, TransitionDuty duty) =>
        Assert.Equal(duty, VisitStateMachine.DutyFor(target));
}
