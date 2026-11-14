using Alveara.Api.Architecture.Scheduling;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (briefing 5.1, 5.2, 5.4; OD-01, OD-02, OD-10): the one visit path, as a fixed table written out by hand from the briefing, compared with the code for EVERY (from, to) pair of the
/// eight states under all four combinations of the two settings. Anything not in the table is refused, so every STORY-011 shortcut is gone and Completed is final.
/// </summary>
public class VisitPathTableTests
{
    /// <summary>The briefing's table: (from, to, needs confirmation off, needs mark-ready off).</summary>
    private static readonly (string From, string To, bool OnlyWithoutConfirmation, bool OnlyWithoutReady)[] Table =
    [
        ("Scheduled", "Confirmed", false, false),
        ("Scheduled", "CheckedIn", true, false),
        ("Confirmed", "CheckedIn", false, false),
        ("CheckedIn", "Ready", false, false),
        ("CheckedIn", "Seated", false, true),
        ("Ready", "Seated", false, false),
        ("Seated", "InTreatment", false, false),
        ("InTreatment", "CheckedOut", false, false),
        ("CheckedOut", "Completed", false, false),
    ];

    public static TheoryData<bool, bool> Settings => new() { { true, true }, { true, false }, { false, true }, { false, false } };

    [Theory]
    [MemberData(nameof(Settings))]
    public void Every_pair_of_states_matches_the_table_under_each_setting_combination(bool confirmationRequired, bool markReadyRequired)
    {
        var settings = new VisitSettings(confirmationRequired, markReadyRequired);
        var pairs = 0;
        foreach (var from in VisitStates.InOrder)
            foreach (var to in VisitStates.InOrder)
            {
                pairs++;
                var row = Table.SingleOrDefault(t => t.From == from && t.To == to);
                var allowed = row.From is not null && (!row.OnlyWithoutConfirmation || !confirmationRequired) && (!row.OnlyWithoutReady || !markReadyRequired);
                var expected = from == to ? FlowDecision.NoChange : allowed ? FlowDecision.Move : FlowDecision.Refused;
                Assert.True(expected == VisitStateMachine.Decide(from, to, settings), $"{from} -> {to} with confirmation {confirmationRequired}, ready {markReadyRequired}: expected {expected}");
            }
        Assert.Equal(64, pairs);
    }

    [Theory]
    [InlineData("CheckedIn", "InTreatment")]
    [InlineData("CheckedIn", "Completed")]
    [InlineData("InTreatment", "Completed")]
    [InlineData("Seated", "CheckedOut")]
    [InlineData("Scheduled", "Seated")]
    public void STORY_011s_shortcuts_and_other_skips_are_refused_whatever_the_settings(string from, string to)
    {
        foreach (var (c, r) in new[] { (true, true), (false, false) })
            Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(from, to, new VisitSettings(c, r)));
    }

    [Fact]
    public void Completed_is_final_and_nothing_goes_backwards()
    {
        foreach (var to in VisitStates.InOrder.Where(s => s != VisitStates.Completed))
            Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(VisitStates.Completed, to, new VisitSettings(false, false)));
        Assert.Equal(FlowDecision.Refused, VisitStateMachine.Decide(VisitStates.CheckedIn, VisitStates.Confirmed));
    }

    [Fact]
    public void The_next_states_offered_follow_the_settings()
    {
        Assert.Equal(["Confirmed"], VisitStateMachine.NextStates("Scheduled", VisitSettings.Default));
        Assert.Equal(["Confirmed", "CheckedIn"], VisitStateMachine.NextStates("Scheduled", new VisitSettings(false, true)));
        Assert.Equal(["Ready"], VisitStateMachine.NextStates("CheckedIn", VisitSettings.Default));
        Assert.Equal(["Ready", "Seated"], VisitStateMachine.NextStates("CheckedIn", new VisitSettings(true, false)));
        Assert.Empty(VisitStateMachine.NextStates("Completed", new VisitSettings(false, false)));
    }
}
