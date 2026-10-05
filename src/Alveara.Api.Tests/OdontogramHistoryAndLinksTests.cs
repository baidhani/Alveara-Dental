using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-006-C01 behaviour, against real SQL Server: a tooth's longitudinal history (every finding ever recorded on it and every change to each, withdrawn ones included, in the order they
/// happened, with who, when and why), that nothing a later chart edit does removes or re-words an event, primary and permanent teeth on one patient (mixed dentition) with no history lost
/// either way, and the links from a finding to a diagnosis, treatment plan or procedure.
/// </summary>
public class OdontogramHistoryAndLinksTests : SafetyTestBase
{
    private Task<ChartView> Chart(Func<OdontogramService, Task<ChartView>> action) => WithDb(db => action(new OdontogramService(db, Clock)));
    private Task<ChartView> RecordAsync(string tooth, string? surface, string condition, string state = "Diagnosed", Guid? patient = null, Guid? actor = null) =>
        Chart(s => s.RecordAsync(patient ?? Ann, tooth, surface, condition, state, actor ?? S.Actor, default));
    private Task<ToothHistoryView> HistoryAsync(string tooth, Guid? patient = null) => WithDb(db => new OdontogramService(db, Clock).ToothHistoryAsync(patient ?? Ann, tooth, default));
    private static async Task<OdontogramException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<OdontogramException>(action);
    private static FindingView Of(ChartView c, string tooth, string condition) => c.Findings.Single(f => f.ToothKey == tooth && f.Condition == condition);
    private Task<ChartView> ChartAsyncFor(Guid? patient = null) => Chart(s => s.ChartAsync(patient ?? Ann, default));

    // ---------- a tooth's longitudinal history ----------

    [Fact]
    public async Task A_tooth_history_lists_every_finding_and_every_change_oldest_first_with_who_and_why_withdrawn_ones_included()
    {
        var chart = await RecordAsync("16", "O", "Caries");
        var caries = Of(chart, "16", "Caries");
        chart = await Chart(s => s.ChangeStateAsync(caries.Id, "Planned", caries.RowVersion, Other, default));
        caries = Of(chart, "16", "Caries");
        chart = await Chart(s => s.ChangeStateAsync(caries.Id, "Completed", caries.RowVersion, S.Actor, default));
        chart = await RecordAsync("16", null, "Crown", "Planned");
        var crown = Of(chart, "16", "Crown");
        await Chart(s => s.WithdrawAsync(crown.Id, "Patient declined the crown", crown.RowVersion, Other, default));

        var h = await HistoryAsync("16");
        Assert.Equal(("16", Ann), (h.ToothKey, h.PatientId));
        Assert.Equal(new[]
        {
            ("Caries", "Recorded", "Diagnosed", "Active", "Dr. Okafor"), ("Caries", "StateChanged", "Planned", "Active", "Hana Hygienist"), ("Caries", "StateChanged", "Completed", "Active", "Dr. Okafor"),
            ("Crown", "Recorded", "Planned", "Active", "Dr. Okafor"), ("Crown", "Withdrawn", "Planned", "Withdrawn", "Hana Hygienist"),
        }, h.Events.Select(e => (e.ConditionLabel, e.ChangeType, e.State, e.Status, e.ActorName)));
        Assert.Equal("Patient declined the crown", h.Events[^1].Reason);
        Assert.Equal("O", h.Events[0].Surface);
        Assert.All(h.Events, e => Assert.NotEqual(default, e.OccurredAtUtc));
        Assert.Equal(h.Events.OrderBy(e => e.OccurredAtUtc).Select(e => e.OccurredAtUtc), h.Events.Select(e => e.OccurredAtUtc));    // in the order they happened
    }

    [Fact]
    public async Task The_history_stays_complete_after_a_later_chart_edit_withdraws_and_re_records_the_same_finding()
    {
        var first = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        await Chart(s => s.WithdrawAsync(first.Id, "Wrong tooth", first.RowVersion, S.Actor, default));
        await RecordAsync("16", "O", "Caries", "Planned");                                          // the same tooth, surface and condition again, as a new finding

        var h = await HistoryAsync("16");
        Assert.Equal(new[] { "Recorded", "Withdrawn", "Recorded" }, h.Events.Select(e => e.ChangeType));
        Assert.Equal(2, h.Events.Select(e => e.FindingId).Distinct().Count());                      // two findings, both still on the tooth's timeline
        Assert.Equal(3, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindings));                                // nothing was deleted
    }

    [Fact]
    public async Task The_history_is_per_tooth_and_per_patient()
    {
        await RecordAsync("16", "O", "Caries");
        await RecordAsync("26", "O", "Caries");
        await RecordAsync("16", "O", "Caries", patient: Bo);
        Assert.Single((await HistoryAsync("16")).Events);
        Assert.Single((await HistoryAsync("26")).Events);
        Assert.Single((await HistoryAsync("16", Bo)).Events);
        Assert.Empty((await HistoryAsync("36")).Events);                                            // nothing recorded is an empty timeline, not an error
    }

    [Fact]
    public async Task A_retired_condition_keeps_its_label_in_the_history_it_made()
    {
        var created = (await WithDb(db => new ConditionTypeService(db, Clock).CreateAsync("Fracture", "Tooth fracture", "Surface", "Both", "None", S.Actor, default))).Single(t => t.Code == "Fracture");
        await RecordAsync("16", "O", "Fracture");
        await WithDb(db => new ConditionTypeService(db, Clock).RetireAsync(created.Id, "Not used any more", created.RowVersion, S.Actor, default));
        var h = await HistoryAsync("16");
        Assert.Equal("Tooth fracture", Assert.Single(h.Events).ConditionLabel);
        Assert.Equal("Tooth fracture", Assert.Single((await ChartAsyncFor()).Findings).ConditionLabel);
    }

    [Theory]
    [InlineData("19")] [InlineData("")] [InlineData("A")] [InlineData("UR1")]
    public async Task A_tooth_that_is_not_one_of_the_52_is_refused(string tooth)
    {
        var refused = await Refused(() => HistoryAsync(tooth));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey("toothKey"));
    }

    [Fact]
    public async Task The_history_of_an_unknown_patient_is_a_404()
        => Assert.Equal("patient_not_found", (await Refused(() => HistoryAsync("16", Guid.NewGuid()))).Code);

    // ---------- permanent, primary and mixed dentition ----------

    [Fact]
    public async Task A_patient_can_have_findings_on_permanent_and_primary_teeth_at_once_and_each_tooth_keeps_its_own_history()
    {
        await RecordAsync("16", "O", "Caries");                                                     // a permanent first molar coming in
        await RecordAsync("55", "O", "Caries");                                                     // a primary second molar still there
        await RecordAsync("51", null, "Crown", "Existing");                                         // a primary central incisor
        var chart = await ChartAsyncFor();
        Assert.Equal(new[] { "16", "51", "55" }, chart.Findings.Select(f => f.ToothKey));
        Assert.Single((await HistoryAsync("55")).Events);
        Assert.Single((await HistoryAsync("51")).Events);
        Assert.Single((await HistoryAsync("16")).Events);
    }

    [Theory]
    [InlineData("51", "I", true)] [InlineData("51", "F", true)] [InlineData("51", "O", false)] [InlineData("51", "B", false)]       // a primary incisor has incisal and facial surfaces
    [InlineData("54", "O", true)] [InlineData("54", "B", true)] [InlineData("54", "I", false)] [InlineData("54", "F", false)]       // a primary molar has occlusal and buccal ones
    public async Task Surfaces_on_primary_teeth_follow_the_same_rule_as_permanent_ones(string tooth, string surface, bool valid)
    {
        if (valid) Assert.Single((await RecordAsync(tooth, surface, "Caries")).Findings);
        else Assert.True((await Refused(() => RecordAsync(tooth, surface, "Caries"))).FieldErrors.ContainsKey("surface"));
    }

    [Fact]
    public async Task Showing_the_chart_as_permanent_primary_or_mixed_is_only_a_view_so_switching_loses_nothing()
    {
        await RecordAsync("16", "O", "Caries");
        await RecordAsync("55", "O", "Caries");
        var before = await ChartAsyncFor();
        var after = await ChartAsyncFor();                                                          // there is no stored dentition mode to switch: the chart is the same whichever view a screen draws
        Assert.Equal(before.Findings.Select(f => f.Id), after.Findings.Select(f => f.Id));
        Assert.Equal(0, await WithDb(db => db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME LIKE '%Dentition%' AND TABLE_NAME IN ('ToothFindings', 'Patients')").SingleAsync()));
    }

    [Fact]
    public async Task Numbering_is_not_part_of_a_teeth_stored_identity()
    {
        await RecordAsync("16", "O", "Caries");
        var columns = await WithDb(db => db.Database.SqlQuery<string>($"SELECT COLUMN_NAME AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('ToothFindings', 'ToothFindingVersions')").ToListAsync());
        Assert.DoesNotContain(columns, c => c.Contains("Universal", StringComparison.OrdinalIgnoreCase) || c.Contains("Palmer", StringComparison.OrdinalIgnoreCase) || c.Contains("Numbering", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("16", (await WithDb(db => db.ToothFindings.AsNoTracking().SingleAsync())).ToothKey);
    }

    // ---------- links to diagnoses, treatment plans and procedures ----------

    [Fact]
    public async Task A_finding_is_linked_to_a_diagnosis_a_treatment_plan_and_a_procedure_each_recorded_in_its_history_with_who_and_when()
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        await Chart(s => s.LinkAsync(finding.Id, "Diagnosis", "DX-1001", S.Actor, default));
        await Chart(s => s.LinkAsync(finding.Id, "TreatmentPlan", "TP-2002", Other, default));
        var chart = await Chart(s => s.LinkAsync(finding.Id, "Procedure", "PROC-3003", S.Actor, default));

        var links = Of(chart, "16", "Caries").Links;
        Assert.Equal(new[] { ("Diagnosis", "DX-1001", "Dr. Okafor"), ("TreatmentPlan", "TP-2002", "Hana Hygienist"), ("Procedure", "PROC-3003", "Dr. Okafor") }, links.Select(l => (l.LinkType, l.Reference, l.LinkedByName)));
        Assert.All(links, l => Assert.NotEqual(default, l.LinkedAtUtc));

        var events = (await HistoryAsync("16")).Events;
        Assert.Equal(new[] { "Recorded", "Linked", "Linked", "Linked" }, events.Select(e => e.ChangeType));
        Assert.Equal(new[] { "Diagnosis: DX-1001", "TreatmentPlan: TP-2002", "Procedure: PROC-3003" }, events.Skip(1).Select(e => e.Reason));
        Assert.Equal(3, (await AuditAsync(nameof(ToothFinding))).Count(a => a.Type == "ToothFindingLinked"));
        Assert.All((await AuditAsync(nameof(ToothFinding))).Where(a => a.Type == "ToothFindingLinked"), a => Assert.DoesNotContain("DX-1001", a.Details));
    }

    [Fact]
    public async Task Linking_does_not_change_the_finding_so_it_never_invalidates_someone_elses_edit()
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        var chart = await Chart(s => s.LinkAsync(finding.Id, "Diagnosis", "DX-1", S.Actor, default));
        Assert.Equal(finding.RowVersion, Of(chart, "16", "Caries").RowVersion);
        chart = await Chart(s => s.ChangeStateAsync(finding.Id, "Planned", finding.RowVersion, S.Actor, default));            // the version read before the link still works
        Assert.Equal("Planned", Of(chart, "16", "Caries").State);
        Assert.Single(Of(chart, "16", "Caries").Links);                                             // and the link is still there
    }

    [Fact]
    public async Task Linking_the_same_thing_again_is_quiet_and_a_different_reference_is_a_second_link()
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        await Chart(s => s.LinkAsync(finding.Id, "Diagnosis", "DX-1", S.Actor, default));
        await Chart(s => s.LinkAsync(finding.Id, "Diagnosis", "  DX-1  ", S.Actor, default));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingLinks));
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindingVersions));
        await Chart(s => s.LinkAsync(finding.Id, "Diagnosis", "DX-2", S.Actor, default));
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindingLinks));
    }

    [Fact]
    public async Task Six_simultaneous_identical_links_store_one()
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await Chart(s => s.LinkAsync(finding.Id, "Procedure", "PROC-1", S.Actor, default)); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingLinks));
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindingVersions));
    }

    [Theory]
    [InlineData("Note", "DX-1", "linkType")] [InlineData("diagnosis", "DX-1", "linkType")] [InlineData("", "DX-1", "linkType")] [InlineData(null, "DX-1", "linkType")]
    [InlineData("Diagnosis", "", "reference")] [InlineData("Diagnosis", "   ", "reference")] [InlineData("Diagnosis", null, "reference")]
    public async Task A_link_with_an_unknown_type_or_no_reference_is_refused_naming_the_field(string? type, string? reference, string field)
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        var refused = await Refused(() => Chart(s => s.LinkAsync(finding.Id, type, reference, S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey(field));
        Assert.Equal(0, await S.CountAsync(db => db.ToothFindingLinks));
    }

    [Fact]
    public async Task A_reference_over_100_characters_is_refused()
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        Assert.True((await Refused(() => Chart(s => s.LinkAsync(finding.Id, "Diagnosis", new string('x', 101), S.Actor, default)))).FieldErrors.ContainsKey("reference"));
    }

    [Fact]
    public async Task A_withdrawn_or_unknown_finding_cannot_be_linked()
    {
        var finding = Of(await RecordAsync("16", "O", "Caries"), "16", "Caries");
        await Chart(s => s.WithdrawAsync(finding.Id, "Wrong tooth", finding.RowVersion, S.Actor, default));
        Assert.Equal(("finding_withdrawn", 409), await CodeOf(() => Chart(s => s.LinkAsync(finding.Id, "Diagnosis", "DX-1", S.Actor, default))));
        Assert.Equal(("finding_not_found", 404), await CodeOf(() => Chart(s => s.LinkAsync(Guid.NewGuid(), "Diagnosis", "DX-1", S.Actor, default))));
        Assert.Equal(0, await S.CountAsync(db => db.ToothFindingLinks));
    }

    private static async Task<(string, int)> CodeOf<T>(Func<Task<T>> action) { var e = await Assert.ThrowsAsync<OdontogramException>(action); return (e.Code, e.StatusCode); }
}
