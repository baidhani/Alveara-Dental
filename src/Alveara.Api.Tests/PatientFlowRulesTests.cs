using Alveara.Api.Architecture.Scheduling;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>STORY-011: the patient-flow transition rule on its own (pure logic, no database).</summary>
public class PatientFlowRulesTests
{
    private const string S = PatientFlowStates.Scheduled;
    private const string C = PatientFlowStates.CheckedIn;
    private const string T = PatientFlowStates.InTreatment;
    private const string D = PatientFlowStates.Completed;

    [Theory]
    [InlineData(S, C)] // acceptance 1: a scheduled patient checks in
    [InlineData(C, T)]
    [InlineData(T, D)]
    [InlineData(C, D)] // acceptance 2: checked in, treatment completed; InTreatment may be skipped
    public void Forward_moves_are_allowed(string from, string to) =>
        Assert.Equal(FlowDecision.Move, PatientFlowRules.Decide(from, to));

    [Theory]
    [InlineData(S)]
    [InlineData(C)]
    [InlineData(T)]
    [InlineData(D)]
    public void Repeating_the_current_state_changes_nothing(string state) =>
        Assert.Equal(FlowDecision.NoChange, PatientFlowRules.Decide(state, state));

    [Theory]
    [InlineData(C, S)]
    [InlineData(T, C)]
    [InlineData(D, T)]
    [InlineData(D, C)]
    [InlineData(D, S)]
    public void Backward_moves_are_refused(string from, string to) =>
        Assert.Equal(FlowDecision.Refused, PatientFlowRules.Decide(from, to));

    [Theory]
    [InlineData(S, T)] // cannot start treatment without checking in
    [InlineData(S, D)] // cannot complete without checking in
    public void Check_in_cannot_be_skipped(string from, string to) =>
        Assert.Equal(FlowDecision.Refused, PatientFlowRules.Decide(from, to));

    [Theory]
    [InlineData("", C)]
    [InlineData(S, "Arrived")]
    [InlineData("completed ", D)]
    [InlineData(null, C)]
    public void Unknown_states_are_refused(string? from, string to) =>
        Assert.Equal(FlowDecision.Refused, PatientFlowRules.Decide(from!, to));

    [Fact]
    public void Every_pair_of_states_has_exactly_one_documented_outcome()
    {
        var moves = new HashSet<(string, string)> { (S, C), (C, T), (T, D), (C, D) };
        foreach (var from in PatientFlowStates.InOrder)
            foreach (var to in PatientFlowStates.InOrder)
            {
                var expected = from == to ? FlowDecision.NoChange
                    : moves.Contains((from, to)) ? FlowDecision.Move
                    : FlowDecision.Refused;
                Assert.Equal(expected, PatientFlowRules.Decide(from, to));
            }
    }
}
