using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-006 behaviour, against real SQL Server: a finding is saved with the state the clinician chose; completing work updates the chart; every update is logged with user and
/// timestamp in the same save (and a failed audit write saves nothing); and every failure path - a wrong tooth or surface, an illegal state move, a missing reason, a stale edit,
/// a withdrawn finding, and simultaneous writers.
/// </summary>
public class OdontogramServiceTests : SafetyTestBase
{
    private Task<ChartView> ChartAsyncFor(Guid? patient = null) => WithDb(db => new OdontogramService(db, Clock).ChartAsync(patient ?? Ann, default));
    private Task<ChartView> Svc(Func<OdontogramService, Task<ChartView>> action) => WithDb(db => action(new OdontogramService(db, Clock)));

    private Task<ChartView> RecordAsync(string tooth = "16", string? surface = "O", string condition = "Caries", string state = "Diagnosed", Guid? patient = null, Guid? actor = null) =>
        Svc(s => s.RecordAsync(patient ?? Ann, tooth, surface, condition, state, actor ?? S.Actor, default));

    private static async Task<OdontogramException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<OdontogramException>(action);

    private static FindingView Only(ChartView c) => Assert.Single(c.Findings);

    private async Task<List<(string Type, string Details, Guid? By)>> FindingAuditAsync() => await AuditAsync(nameof(ToothFinding));

    // ---------- acceptance 1: a recorded condition is saved with the correct lifecycle state ----------

    [Theory]
    [InlineData(FindingStates.Existing)]
    [InlineData(FindingStates.Diagnosed)]
    [InlineData(FindingStates.Planned)]
    [InlineData(FindingStates.Completed)]
    public async Task A_recorded_condition_is_saved_with_exactly_the_state_chosen_and_who_recorded_it(string state)
    {
        var f = Only(await RecordAsync("16", "O", FindingConditions.Caries, state));
        Assert.Equal(("16", "O", "Caries", state, FindingStatuses.Active), (f.ToothKey, f.Surface, f.Condition, f.State, f.Status));
        Assert.Equal("Dr. Okafor", f.RecordedByName);
        Assert.NotEqual(default, f.RecordedAtUtc);
        Assert.Null(f.UpdatedAtUtc);
        var stored = await WithDb(db => db.ToothFindings.AsNoTracking().SingleAsync());
        Assert.Equal((state, Ann), (stored.State, stored.PatientId));
    }

    [Fact]
    public async Task Each_of_the_six_conditions_can_be_recorded_surface_conditions_with_a_surface_and_whole_tooth_ones_without()
    {
        await RecordAsync("26", "D", FindingConditions.Caries);
        await RecordAsync("26", "O", FindingConditions.Restoration, FindingStates.Existing);
        await RecordAsync("11", null, FindingConditions.Crown, FindingStates.Existing);
        await RecordAsync("18", null, FindingConditions.Missing, FindingStates.Existing);
        await RecordAsync("36", null, FindingConditions.Implant, FindingStates.Existing);
        var chart = await RecordAsync("46", null, FindingConditions.RootCanal, FindingStates.Completed);
        Assert.Equal(6, chart.Findings.Count);
        Assert.Equal(new[] { "11", "18", "26", "26", "36", "46" }, chart.Findings.Select(f => f.ToothKey));       // ordered by tooth
        Assert.Equal(new[] { "D", "O" }, chart.Findings.Where(f => f.ToothKey == "26").Select(f => f.Surface));
    }

    [Fact]
    public async Task A_surface_letter_is_accepted_in_either_case_and_stored_in_capitals()
    {
        var f = Only(await Svc(s => s.RecordAsync(Ann, " 21 ", "i", " Caries ", " Diagnosed ", S.Actor, default)));
        Assert.Equal(("21", "I", "Caries", "Diagnosed"), (f.ToothKey, f.Surface, f.Condition, f.State));
    }

    [Fact]
    public async Task A_primary_tooth_can_be_recorded_the_same_way()
    {
        var f = Only(await RecordAsync("54", "O", FindingConditions.Caries));
        Assert.Equal("54", f.ToothKey);
    }

    // ---------- failure path: incorrect tooth selection and data entry errors ----------

    [Theory]
    [InlineData("19")] [InlineData("1")] [InlineData("A")] [InlineData("")] [InlineData(null)] [InlineData("UR1")] [InlineData("56")]
    public async Task A_tooth_that_is_not_on_the_chart_is_refused_and_stores_nothing(string? tooth)
    {
        var refused = await Refused(() => RecordAsync(tooth!, "O"));
        Assert.Equal(("validation_failed", 400), (refused.Code, refused.StatusCode));
        Assert.True(refused.FieldErrors.ContainsKey("toothKey"));
        await NothingStoredAsync();
    }

    [Theory]
    [InlineData("11", "O")]    // no occlusal surface on an incisor
    [InlineData("16", "I")]    // no incisal surface on a molar
    [InlineData("16", "F")]
    [InlineData("21", "B")]
    [InlineData("16", "X")]
    [InlineData("16", null)]   // caries needs a surface
    [InlineData("16", "  ")]
    public async Task A_surface_that_does_not_exist_on_that_tooth_or_is_missing_for_a_surface_condition_is_refused(string tooth, string? surface)
    {
        var refused = await Refused(() => RecordAsync(tooth, surface));
        Assert.Equal("validation_failed", refused.Code);
        Assert.True(refused.FieldErrors.ContainsKey("surface"));
        await NothingStoredAsync();
    }

    [Theory]
    [InlineData(FindingConditions.Crown)] [InlineData(FindingConditions.Missing)] [InlineData(FindingConditions.Implant)] [InlineData(FindingConditions.RootCanal)]
    public async Task A_whole_tooth_condition_with_a_surface_is_refused(string condition)
    {
        var refused = await Refused(() => RecordAsync("16", "O", condition));
        Assert.True(refused.FieldErrors.ContainsKey("surface"));
        await NothingStoredAsync();
    }

    [Theory]
    [InlineData("Cavity", "Diagnosed", "condition")]
    [InlineData("caries ", "Proposed", "state")]
    [InlineData(null, null, "condition")]
    [InlineData("Caries", "", "state")]
    public async Task An_unknown_condition_or_state_is_refused_naming_the_field(string? condition, string? state, string field)
    {
        var refused = await Refused(() => Svc(s => s.RecordAsync(Ann, "16", "O", condition, state, S.Actor, default)));
        Assert.True(refused.FieldErrors.ContainsKey(field));
        await NothingStoredAsync();
    }

    [Fact]
    public async Task Every_wrong_field_is_named_at_once()
    {
        var refused = await Refused(() => Svc(s => s.RecordAsync(Ann, "99", "O", "Cavity", "Soon", S.Actor, default)));
        Assert.Contains("toothKey", refused.FieldErrors.Keys);
        Assert.Contains("condition", refused.FieldErrors.Keys);
        Assert.Contains("state", refused.FieldErrors.Keys);
    }

    [Fact]
    public async Task An_unknown_patient_is_a_404_and_stores_nothing()
    {
        Assert.Equal(("patient_not_found", 404), ((await Refused(() => RecordAsync(patient: Guid.NewGuid()))).Code, 404));
        Assert.Equal("patient_not_found", (await Refused(() => ChartAsyncFor(Guid.NewGuid()))).Code);
        await NothingStoredAsync();
    }

    private async Task NothingStoredAsync()
    {
        Assert.Equal(0, await S.CountAsync(db => db.ToothFindings));
        Assert.Equal(0, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Empty(await FindingAuditAsync());
    }

    // ---------- the lifecycle ----------

    [Fact]
    public async Task A_finding_moves_forward_from_diagnosed_to_planned_to_completed_with_each_step_in_the_history()
    {
        var f = Only(await RecordAsync());
        f = Only(await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, f.RowVersion, Other, default)));
        Assert.Equal((FindingStates.Planned, "Hana Hygienist"), (f.State, f.UpdatedByName));
        Assert.NotNull(f.UpdatedAtUtc);
        f = Only(await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Completed, f.RowVersion, S.Actor, default)));
        Assert.Equal(FindingStates.Completed, f.State);

        var history = await WithDb(db => new OdontogramService(db, Clock).HistoryAsync(f.Id, default));
        Assert.Equal(new[] { 1, 2, 3 }, history.Versions.Select(v => v.VersionNumber));
        Assert.Equal(new[] { "Recorded", "StateChanged", "StateChanged" }, history.Versions.Select(v => v.ChangeType));
        Assert.Equal(new[] { "Diagnosed", "Planned", "Completed" }, history.Versions.Select(v => v.State));
        Assert.Equal(new[] { "Dr. Okafor", "Hana Hygienist", "Dr. Okafor" }, history.Versions.Select(v => v.ActorName));
        Assert.All(history.Versions, v => Assert.NotEqual(default, v.OccurredAtUtc));
    }

    [Fact]
    public async Task A_diagnosed_finding_can_be_completed_directly_for_work_done_at_the_visit()
    {
        var f = Only(await RecordAsync());
        Assert.Equal(FindingStates.Completed, Only(await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Completed, f.RowVersion, S.Actor, default))).State);
    }

    // ---------- acceptance 2: a completed treatment updates the odontogram ----------

    [Fact]
    public async Task Completing_a_planned_treatment_updates_what_the_chart_shows()
    {
        var planned = Only(await RecordAsync("16", "O", FindingConditions.Caries, FindingStates.Planned));
        Assert.Equal(FindingStates.Planned, Only(await ChartAsyncFor()).State);
        await Svc(s => s.ChangeStateAsync(planned.Id, FindingStates.Completed, planned.RowVersion, S.Actor, default));
        var now = Only(await ChartAsyncFor());
        Assert.Equal((planned.Id, FindingStates.Completed), (now.Id, now.State));
    }

    [Fact]
    public async Task Work_recorded_as_completed_directly_shows_on_the_chart()
    {
        await RecordAsync("46", null, FindingConditions.RootCanal, FindingStates.Completed);
        Assert.Equal(FindingStates.Completed, Only(await ChartAsyncFor()).State);
    }

    [Theory]
    [InlineData(FindingStates.Completed, FindingStates.Planned)]
    [InlineData(FindingStates.Completed, FindingStates.Diagnosed)]
    [InlineData(FindingStates.Completed, FindingStates.Existing)]
    [InlineData(FindingStates.Planned, FindingStates.Diagnosed)]
    [InlineData(FindingStates.Existing, FindingStates.Planned)]
    [InlineData(FindingStates.Existing, FindingStates.Completed)]
    [InlineData(FindingStates.Diagnosed, FindingStates.Existing)]
    public async Task An_illegal_move_is_refused_and_leaves_the_finding_and_its_history_alone(string from, string to)
    {
        var f = Only(await RecordAsync(state: from));
        var refused = await Refused(() => Svc(s => s.ChangeStateAsync(f.Id, to, f.RowVersion, S.Actor, default)));
        Assert.Equal(("invalid_transition", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(from, Only(await ChartAsyncFor()).State);
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingVersions));
    }

    [Fact]
    public async Task Moving_to_the_state_it_already_has_changes_nothing()
    {
        var f = Only(await RecordAsync(state: FindingStates.Planned));
        await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, f.RowVersion, S.Actor, default));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Single(await FindingAuditAsync());
    }

    [Fact]
    public async Task An_unknown_target_state_or_finding_is_refused()
    {
        var f = Only(await RecordAsync());
        Assert.True((await Refused(() => Svc(s => s.ChangeStateAsync(f.Id, "Soon", f.RowVersion, S.Actor, default)))).FieldErrors.ContainsKey("state"));
        Assert.Equal("finding_not_found", (await Refused(() => Svc(s => s.ChangeStateAsync(Guid.NewGuid(), FindingStates.Planned, f.RowVersion, S.Actor, default)))).Code);
    }

    // ---------- quiet repeats ----------

    [Fact]
    public async Task Recording_the_same_active_finding_again_is_a_quiet_repeat()
    {
        await RecordAsync();
        var chart = await RecordAsync();
        Assert.Single(chart.Findings);
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindings));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Single(await FindingAuditAsync());
    }

    [Fact]
    public async Task Recording_an_existing_finding_in_a_different_state_is_refused_so_the_state_is_changed_deliberately()
    {
        await RecordAsync(state: FindingStates.Diagnosed);
        var refused = await Refused(() => RecordAsync(state: FindingStates.Planned));
        Assert.Equal(("finding_exists", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(FindingStates.Diagnosed, Only(await ChartAsyncFor()).State);
    }

    [Fact]
    public async Task The_same_tooth_in_another_patients_chart_is_a_different_finding()
    {
        await RecordAsync(patient: Ann);
        await RecordAsync(patient: Bo);
        Assert.Single((await ChartAsyncFor(Ann)).Findings);
        Assert.Single((await ChartAsyncFor(Bo)).Findings);
    }

    // ---------- withdrawing ----------

    [Fact]
    public async Task A_wrong_entry_is_withdrawn_with_a_reason_and_kept_in_the_history_never_deleted()
    {
        var f = Only(await RecordAsync());
        var chart = await Svc(s => s.WithdrawAsync(f.Id, "  Wrong tooth  ", f.RowVersion, Other, default));
        Assert.Empty(chart.Findings);
        var stored = await WithDb(db => db.ToothFindings.AsNoTracking().SingleAsync());
        Assert.Equal((FindingStatuses.Withdrawn, "Wrong tooth", Other), (stored.Status, stored.WithdrawnReason, stored.WithdrawnByUserId));
        Assert.NotNull(stored.WithdrawnAtUtc);
        var history = await WithDb(db => new OdontogramService(db, Clock).HistoryAsync(f.Id, default));
        var last = history.Versions[^1];
        Assert.Equal(("Withdrawn", "Wrong tooth", "Hana Hygienist", FindingStatuses.Withdrawn), (last.ChangeType, last.Reason, last.ActorName, last.Status));
        Assert.Equal(2, history.Versions.Count);
    }

    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("   ")]
    public async Task Withdrawing_needs_a_reason(string? reason)
    {
        var f = Only(await RecordAsync());
        var refused = await Refused(() => Svc(s => s.WithdrawAsync(f.Id, reason, f.RowVersion, S.Actor, default)));
        Assert.Equal(("reason_required", 400), (refused.Code, refused.StatusCode));
        Assert.Single((await ChartAsyncFor()).Findings);
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused()
    {
        var f = Only(await RecordAsync());
        var refused = await Refused(() => Svc(s => s.WithdrawAsync(f.Id, new string('x', 501), f.RowVersion, S.Actor, default)));
        Assert.True(refused.FieldErrors.ContainsKey("reason"));
    }

    [Fact]
    public async Task Withdrawing_a_withdrawn_finding_changes_nothing_and_its_state_cannot_then_change()
    {
        var f = Only(await RecordAsync());
        await Svc(s => s.WithdrawAsync(f.Id, "Wrong tooth", f.RowVersion, S.Actor, default));
        var stored = await WithDb(db => db.ToothFindings.AsNoTracking().SingleAsync());
        var rv = Convert.ToBase64String(stored.RowVersion);
        await Svc(s => s.WithdrawAsync(f.Id, "Again", rv, S.Actor, default));
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Equal("Wrong tooth", (await WithDb(db => db.ToothFindings.AsNoTracking().SingleAsync())).WithdrawnReason);
        Assert.Equal("finding_withdrawn", (await Refused(() => Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, rv, S.Actor, default)))).Code);
    }

    [Fact]
    public async Task After_a_withdrawal_the_correct_finding_can_be_recorded_on_the_same_tooth()
    {
        var wrong = Only(await RecordAsync("16", "O"));
        await Svc(s => s.WithdrawAsync(wrong.Id, "Wrong tooth", wrong.RowVersion, S.Actor, default));
        var chart = await RecordAsync("17", "O");
        var right = Only(chart);
        Assert.Equal("17", right.ToothKey);
        var again = await RecordAsync("16", "O");                       // and the original can be entered again, as a new finding
        Assert.Equal(2, again.Findings.Count);
        Assert.Equal(3, await S.CountAsync(db => db.ToothFindings));
    }

    // ---------- concurrency and row versions ----------

    [Fact]
    public async Task A_stale_change_is_refused_not_merged()
    {
        var f = Only(await RecordAsync());
        await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, f.RowVersion, S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Completed, f.RowVersion, Other, default)));          // second editor still on the old version
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.WithdrawAsync(f.Id, "Late", f.RowVersion, Other, default)));
        Assert.Equal(FindingStates.Planned, Only(await ChartAsyncFor()).State);
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindingVersions));
    }

    [Theory]
    [InlineData(null, "row_version_required")]
    [InlineData("", "row_version_required")]
    [InlineData("not base64!", "row_version_invalid")]
    public async Task A_missing_or_malformed_row_version_is_a_400_before_anything_changes(string? rowVersion, string code)
    {
        var f = Only(await RecordAsync());
        var refused = await Refused(() => Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, rowVersion, S.Actor, default)));
        Assert.Equal((code, 400), (refused.Code, refused.StatusCode));
        Assert.Equal((code, 400), ((await Refused(() => Svc(s => s.WithdrawAsync(f.Id, "x", rowVersion, S.Actor, default)))).Code, 400));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingVersions));
    }

    [Fact]
    public async Task Six_simultaneous_records_of_the_same_finding_store_it_once()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await RecordAsync(); return "ok"; }
            catch (Exception ex) { return ex.GetType().Name; }
        })));
        Assert.All(outcomes, o => Assert.Equal("ok", o));                 // identical retries all get the one finding
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindings));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Single(await FindingAuditAsync());
    }

    [Fact]
    public async Task Simultaneous_records_of_the_same_tooth_in_different_states_leave_one_finding_and_refuse_the_rest_clearly()
    {
        var outcomes = await Task.WhenAll(new[] { FindingStates.Diagnosed, FindingStates.Planned, FindingStates.Completed }.Select(st => Task.Run(async () =>
        {
            try { await RecordAsync(state: st); return "ok"; }
            catch (OdontogramException ex) { return ex.Code; }
        })));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindings));
        Assert.Contains("ok", outcomes);
        Assert.All(outcomes, o => Assert.Contains(o, new[] { "ok", "finding_exists" }));
    }

    [Fact]
    public async Task Six_simultaneous_moves_from_one_version_apply_exactly_once()
    {
        var f = Only(await RecordAsync());
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            try { await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, f.RowVersion, S.Actor, default)); return "ok"; }
            catch (ConcurrencyConflictException) { return "conflict"; }
        })));
        Assert.Contains("ok", outcomes);
        Assert.Equal(2, await S.CountAsync(db => db.ToothFindingVersions));
        Assert.Equal(1, (await FindingAuditAsync()).Count(e => e.Type == "ToothFindingStateChanged"));
    }

    // ---------- trust: every update is logged with user and timestamp ----------

    [Fact]
    public async Task Every_update_is_logged_with_the_user_and_a_time_and_the_log_never_names_the_tooth_or_condition()
    {
        var f = Only(await RecordAsync("16", "O", FindingConditions.Caries));
        f = Only(await Svc(s => s.ChangeStateAsync(f.Id, FindingStates.Planned, f.RowVersion, Other, default)));
        await Svc(s => s.WithdrawAsync(f.Id, "Entered on the wrong patient", f.RowVersion, S.Actor, default));

        var audit = await FindingAuditAsync();
        Assert.Equal(new[] { "ToothFindingRecorded", "ToothFindingStateChanged", "ToothFindingWithdrawn" }, audit.Select(a => a.Type));
        Assert.Equal(new Guid?[] { S.Actor, Other, S.Actor }, audit.Select(a => a.By));
        Assert.All(audit, a =>
        {
            Assert.DoesNotContain("16", a.Details);
            Assert.DoesNotContain("Caries", a.Details);
            Assert.DoesNotContain("wrong patient", a.Details);
        });
        await using var db = Fixture.CreateContext();
        Assert.All(await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(ToothFinding)).ToListAsync(), a => Assert.NotEqual(default, a.TimestampUtc));
    }

    [Fact]
    public async Task The_change_its_history_and_its_log_entry_are_saved_together_or_not_at_all()
    {
        await using (var db = Fixture.CreateContext())
        {
            // an audit write that fails: nothing about the finding may be left behind
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseFindingAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'ToothFinding') THROW 59000, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        }
        await Assert.ThrowsAnyAsync<Exception>(() => RecordAsync());
        await NothingStoredAsync();
    }

    [Fact]
    public async Task The_chart_is_empty_for_a_patient_with_nothing_recorded_and_withdrawn_findings_are_not_on_it()
    {
        Assert.Empty((await ChartAsyncFor()).Findings);
        var f = Only(await RecordAsync());
        await Svc(s => s.WithdrawAsync(f.Id, "Wrong tooth", f.RowVersion, S.Actor, default));
        Assert.Empty((await ChartAsyncFor()).Findings);
    }

    [Fact]
    public async Task The_history_of_an_unknown_finding_is_a_404()
        => Assert.Equal("finding_not_found", (await Refused(() => WithDb(db => new OdontogramService(db, Clock).HistoryAsync(Guid.NewGuid(), default)))).Code);
}
