using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-006-C01 R02, against real SQL Server: the invariant that a tooth is never BOTH absent (an active Existing or Completed finding whose condition has the Absent effect) and carrying an
/// active finding that says nothing about presence - held at every moment, for every writer, not only when a finding is first recorded. It is proved at three levels: the database trigger
/// itself with raw SQL (sequential orders, and two real transactions racing on one tooth, one of them held open); the service (the reviewer's exact sequence, the refusal that tells the person
/// the way out, quiet non-destructive failure, concurrent writers across many teeth); and what stays allowed (implants, other absent conditions, withdrawn findings, planned extractions).
/// </summary>
public class ToothPresenceInvariantTests : SafetyTestBase
{
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);
    private Task<ChartView> Chart(Func<OdontogramService, Task<ChartView>> action) => WithDb(db => action(new OdontogramService(db, Clock)));
    private Task<ChartView> RecordAsync(string tooth, string? surface, string condition, string state = "Diagnosed", Guid? patient = null) =>
        Chart(s => s.RecordAsync(patient ?? Ann, tooth, surface, condition, state, S.Actor, default));
    private Task<ChartView> MoveAsync(FindingView f, string to) => Chart(s => s.ChangeStateAsync(f.Id, to, f.RowVersion, S.Actor, default));
    private static FindingView Of(ChartView c, string tooth, string condition) => c.Findings.Single(f => f.ToothKey == tooth && f.Condition == condition);
    private static async Task<OdontogramException> Refused<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<OdontogramException>(action);
    /// <summary>A surface that exists on the tooth: Incisal on the front teeth (positions 1 to 3), Occlusal on the rest.</summary>
    private static string SurfaceFor(string tooth) => tooth[1] <= '3' ? "I" : "O";
    private Task<ChartView> ChartAsyncFor(Guid? patient = null) => Chart(s => s.ChartAsync(patient ?? Ann, default));

    /// <summary>The invariant itself, asked of the whole database: no patient and tooth holds an absence and a presence-neutral finding together.</summary>
    private Task<int> ViolationsAsync() => WithDb(db => db.Database.SqlQuery<int>($@"
        SELECT COUNT(*) AS [Value] FROM (
            SELECT DISTINCT a.PatientId, a.ToothKey FROM ToothFindings a JOIN ConditionTypes ca ON ca.Code = a.Condition
            JOIN ToothFindings o ON o.PatientId = a.PatientId AND o.ToothKey = a.ToothKey JOIN ConditionTypes co ON co.Code = o.Condition
            WHERE a.Status = 'Active' AND ca.ToothEffect = 'Absent' AND a.State IN ('Existing', 'Completed') AND o.Status = 'Active' AND o.Id <> a.Id AND co.ToothEffect = 'None') v").SingleAsync());

    // ---------- the reviewer's sequence, through the service ----------

    [Fact]
    public async Task Planned_missing_then_caries_then_missing_completed_cannot_persist_the_invalid_state_and_nothing_changes()
    {
        var missing = Of(await RecordAsync("16", null, "Missing", "Planned"), "16", "Missing");
        await RecordAsync("16", "O", "Caries", "Diagnosed");                                         // allowed: a planned extraction does not make the tooth absent yet
        var before = await WithDb(db => db.ToothFindings.AsNoTracking().OrderBy(f => f.Id).Select(f => new { f.Id, f.State, f.Status, RowVersion = Convert.ToBase64String(f.RowVersion) }).ToListAsync());
        var versions = await S.CountAsync(db => db.ToothFindingVersions);
        var audits = (await AuditAsync(nameof(ToothFinding))).Count;

        var refused = await Refused(() => MoveAsync(missing, "Completed"));
        Assert.Equal(("tooth_has_findings", 409), (refused.Code, refused.StatusCode));
        Assert.Contains("Withdraw them", refused.Message);                                           // the refusal names the way out
        Assert.Contains("Nothing was changed", refused.Message);

        var after = await WithDb(db => db.ToothFindings.AsNoTracking().OrderBy(f => f.Id).Select(f => new { f.Id, f.State, f.Status, RowVersion = Convert.ToBase64String(f.RowVersion) }).ToListAsync());
        Assert.Equal(before, after);                                                                 // not one row changed
        Assert.Equal(versions, await S.CountAsync(db => db.ToothFindingVersions));                  // no history written for a change that did not happen
        Assert.Equal(audits, (await AuditAsync(nameof(ToothFinding))).Count);                       // no audit entry either
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task The_way_out_works_withdraw_the_other_findings_with_a_reason_then_the_tooth_can_become_missing_and_takes_nothing_further()
    {
        var chart = await RecordAsync("16", null, "Missing", "Planned");
        chart = await RecordAsync("16", "O", "Caries");
        chart = await RecordAsync("16", null, "Crown", "Existing");
        var missing = Of(chart, "16", "Missing");
        Assert.Equal("tooth_has_findings", (await Refused(() => MoveAsync(missing, "Completed"))).Code);

        foreach (var f in chart.Findings.Where(f => f.Condition != "Missing"))
            chart = await Chart(s => s.WithdrawAsync(f.Id, "Tooth extracted", f.RowVersion, S.Actor, default));
        chart = await MoveAsync(Of(chart, "16", "Missing"), "Completed");
        Assert.Equal("Completed", Of(chart, "16", "Missing").State);
        Assert.Equal("tooth_absent", (await Refused(() => RecordAsync("16", "O", "Caries"))).Code);          // and now it is absent
        Assert.Equal(0, await ViolationsAsync());
        Assert.Equal(2, (await WithDb(db => new OdontogramService(db, Clock).ToothHistoryAsync(Ann, "16", default))).Events.Count(e => e.ChangeType == "Withdrawn"));   // the history kept what was withdrawn
    }

    [Theory]
    [InlineData("Existing")]
    [InlineData("Completed")]
    public async Task Recording_a_tooth_as_missing_directly_is_refused_while_other_findings_stand_on_it(string state)
    {
        await RecordAsync("26", "D", "Caries", "Existing");
        var refused = await Refused(() => RecordAsync("26", null, "Missing", state));
        Assert.Equal(("tooth_has_findings", 409), (refused.Code, refused.StatusCode));
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindings));
        Assert.Equal(0, await ViolationsAsync());
    }

    [Theory]
    [InlineData("Planned")]
    [InlineData("Diagnosed")]
    public async Task A_planned_or_diagnosed_missing_tooth_may_be_recorded_beside_other_findings(string state)
    {
        await RecordAsync("26", "D", "Caries", "Existing");
        Assert.Equal(2, (await RecordAsync("26", null, "Missing", state)).Findings.Count(f => f.ToothKey == "26"));
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task A_condition_a_practice_added_with_the_absent_effect_follows_the_same_rule_and_two_absences_may_coexist()
    {
        await WithDb(db => new ConditionTypeService(db, Clock).CreateAsync("Extracted", "Extracted", "WholeTooth", "Both", "Absent", S.Actor, default));
        await RecordAsync("36", "O", "Caries", "Existing");
        Assert.Equal("tooth_has_findings", (await Refused(() => RecordAsync("36", null, "Extracted", "Completed"))).Code);
        await RecordAsync("46", null, "Extracted", "Completed");
        await RecordAsync("46", null, "Missing", "Existing");                                         // both say the tooth is not there: no conflict
        Assert.Equal("tooth_absent", (await Refused(() => RecordAsync("46", "O", "Caries"))).Code);
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task A_replacement_such_as_an_implant_stays_possible_in_every_order_and_withdrawn_findings_do_not_count()
    {
        var chart = await RecordAsync("16", "O", "Caries", "Existing");
        await RecordAsync("16", null, "Implant", "Planned");                                          // an implant beside an ordinary finding
        chart = await ChartAsyncFor();
        await Chart(s => s.WithdrawAsync(Of(chart, "16", "Caries").Id, "Tooth extracted", Of(chart, "16", "Caries").RowVersion, S.Actor, default));
        await RecordAsync("16", null, "Missing", "Completed");                                        // the withdrawn caries no longer blocks the extraction
        chart = await ChartAsyncFor();
        await MoveAsync(Of(chart, "16", "Implant"), "Completed");                                    // and the implant can still be completed on the now-absent tooth
        await RecordAsync("26", null, "Missing", "Completed");                                        // the other order: absent first, then an implant
        await RecordAsync("26", null, "Implant", "Planned");
        Assert.Equal(0, await ViolationsAsync());
        Assert.Equal(new[] { "Implant", "Missing" }, (await ChartAsyncFor()).Findings.Where(f => f.ToothKey == "26").Select(f => f.Condition).Order());
    }

    [Fact]
    public async Task The_same_tooth_on_another_patient_is_unaffected()
    {
        await RecordAsync("16", null, "Missing", "Completed");                                        // Ann's tooth is absent
        await RecordAsync("16", "O", "Caries", "Existing", patient: Bo);                              // Bo's is not, and is not held up by Ann's
        Assert.Equal("tooth_has_findings", (await Refused(() => RecordAsync("16", null, "Missing", "Existing", patient: Bo))).Code);   // the rule is per patient and tooth
        Assert.Equal(0, await ViolationsAsync());
    }

    // ---------- concurrent writers ----------

    [Fact]
    public async Task Sixteen_pairs_of_simultaneous_missing_and_caries_writes_never_both_succeed()
    {
        var teeth = new[] { "11", "12", "13", "14", "15", "17", "18", "21", "22", "23", "24", "25", "27", "28", "31", "32" };
        var allowedCodes = new[] { "tooth_absent", "tooth_has_findings", "tooth_busy" };
        var results = await Task.WhenAll(teeth.Select(async tooth =>
        {
            var gate = new TaskCompletionSource();
            async Task<string> Try(Func<Task<ChartView>> write)
            {
                await gate.Task;
                try { await write(); return "ok"; }
                catch (OdontogramException ex) { return ex.Code; }
            }
            var a = Task.Run(() => Try(() => RecordAsync(tooth, null, "Missing", "Existing")));
            var b = Task.Run(() => Try(() => RecordAsync(tooth, SurfaceFor(tooth), "Caries", "Existing")));
            gate.SetResult();
            return (tooth, missing: await a, caries: await b);
        }));
        foreach (var (tooth, missing, caries) in results)
        {
            Assert.False(missing == "ok" && caries == "ok", $"tooth {tooth}: both writes succeeded");
            Assert.True(missing == "ok" || caries == "ok", $"tooth {tooth}: neither write succeeded ({missing}, {caries})");
            foreach (var outcome in new[] { missing, caries }.Where(o => o != "ok")) Assert.Contains(outcome, allowedCodes);
        }
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task A_simultaneous_move_to_completed_and_a_new_finding_on_the_same_tooth_cannot_both_win()
    {
        var teeth = new[] { "41", "42", "43", "44", "45", "46", "47", "48" };
        var missingByTooth = new Dictionary<string, FindingView>();
        foreach (var t in teeth) missingByTooth[t] = Of(await RecordAsync(t, null, "Missing", "Planned"), t, "Missing");
        var results = await Task.WhenAll(teeth.Select(async tooth =>
        {
            var gate = new TaskCompletionSource();
            async Task<string> Try(Func<Task<ChartView>> write)
            {
                await gate.Task;
                try { await write(); return "ok"; }
                catch (OdontogramException ex) { return ex.Code; }
            }
            var move = Task.Run(() => Try(() => MoveAsync(missingByTooth[tooth], "Completed")));
            var record = Task.Run(() => Try(() => RecordAsync(tooth, SurfaceFor(tooth), "Caries", "Existing")));
            gate.SetResult();
            return (tooth, move: await move, record: await record);
        }));
        foreach (var (tooth, move, record) in results) Assert.False(move == "ok" && record == "ok", $"tooth {tooth}: both the move and the new finding succeeded");
        Assert.Equal(0, await ViolationsAsync());
    }

    // ---------- the database trigger, with raw SQL ----------

    private async Task InsertAsync(SqlConnection c, SqlTransaction? tx, string tooth, string condition, string state, Guid? patient = null, string status = "Active")
    {
        var whole = condition != "Caries" && condition != "Restoration";
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandTimeout = 60;
        cmd.CommandText = "INSERT INTO ToothFindings (Id, PatientId, ToothKey, Surface, Condition, ConditionScope, State, Status, CreatedAtUtc, WithdrawnAtUtc, WithdrawnByUserId, WithdrawnReason) " +
                          "VALUES (@id, @p, @t, @s, @c, @sc, @st, @status, @at, @wat, @wby, @wr)";
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@p", patient ?? Ann);
        cmd.Parameters.AddWithValue("@t", tooth);
        cmd.Parameters.AddWithValue("@s", whole ? DBNull.Value : "O");
        cmd.Parameters.AddWithValue("@c", condition);
        cmd.Parameters.AddWithValue("@sc", whole ? "WholeTooth" : "Surface");
        cmd.Parameters.AddWithValue("@st", state);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@at", At);
        cmd.Parameters.AddWithValue("@wat", status == "Withdrawn" ? At : DBNull.Value);
        cmd.Parameters.AddWithValue("@wby", status == "Withdrawn" ? Guid.NewGuid() : DBNull.Value);
        cmd.Parameters.AddWithValue("@wr", status == "Withdrawn" ? "Test" : DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(Fixture.ConnectionString);
        await c.OpenAsync();
        return c;
    }

    [Fact]
    public async Task The_trigger_refuses_the_reviewers_sequence_in_raw_sql_without_any_application_code()
    {
        await using var c = await OpenAsync();
        await InsertAsync(c, null, "16", "Missing", "Planned");
        await InsertAsync(c, null, "16", "Caries", "Diagnosed");
        await using var update = c.CreateCommand();
        update.CommandText = "UPDATE ToothFindings SET State = 'Completed' WHERE ToothKey = '16' AND Condition = 'Missing'";
        var ex = await Assert.ThrowsAsync<SqlException>(() => update.ExecuteNonQueryAsync());
        Assert.Equal(ToothPresenceErrors.HasFindings, ex.Number);
        Assert.Equal(0, await ViolationsAsync());
    }

    [Theory]
    [InlineData("Missing", "Existing", "Caries", ToothPresenceErrors.Absent)]            // an absent tooth first, then an ordinary finding
    [InlineData("Missing", "Completed", "Crown", ToothPresenceErrors.Absent)]
    [InlineData("Caries", "Existing", "Missing", ToothPresenceErrors.HasFindings)]       // an ordinary finding first, then the tooth becomes absent
    [InlineData("RootCanal", "Completed", "Missing", ToothPresenceErrors.HasFindings)]
    public async Task The_trigger_refuses_each_order_that_would_leave_a_tooth_absent_beside_an_ordinary_finding(string first, string firstState, string second, int refusal)
    {
        await using var c = await OpenAsync();
        await InsertAsync(c, null, "16", first, firstState);
        var ex = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(c, null, "16", second, second == "Missing" ? "Existing" : "Diagnosed"));
        Assert.Equal(refusal, ex.Number);
        Assert.Equal(1, await S.CountAsync(db => db.ToothFindings));
    }

    [Fact]
    public async Task The_trigger_allows_what_the_rule_allows_a_replacement_a_planned_extraction_and_findings_that_are_withdrawn()
    {
        await using var c = await OpenAsync();
        await InsertAsync(c, null, "16", "Missing", "Completed");
        await InsertAsync(c, null, "16", "Implant", "Existing");                      // a replacement beside an absence
        await InsertAsync(c, null, "26", "Caries", "Existing");
        await InsertAsync(c, null, "26", "Missing", "Planned");                       // a planned extraction beside an ordinary finding
        await InsertAsync(c, null, "36", "Caries", "Existing", status: "Withdrawn");
        await InsertAsync(c, null, "36", "Missing", "Completed");                     // a withdrawn finding does not count
        await InsertAsync(c, null, "46", "Missing", "Completed", patient: Bo);        // another patient
        Assert.Equal(7, await S.CountAsync(db => db.ToothFindings));
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task The_trigger_judges_only_what_a_statement_changes_so_findings_written_before_it_existed_are_not_re_judged_and_a_withdrawal_is_never_refused()
    {
        await using var c = await OpenAsync();
        await using (var off = c.CreateCommand()) { off.CommandText = "DISABLE TRIGGER TR_ToothFindings_ToothPresence ON ToothFindings"; await off.ExecuteNonQueryAsync(); }
        await InsertAsync(c, null, "16", "Missing", "Completed");                    // a state an earlier version could have produced
        await InsertAsync(c, null, "16", "Caries", "Existing");
        await using (var on = c.CreateCommand()) { on.CommandText = "ENABLE TRIGGER TR_ToothFindings_ToothPresence ON ToothFindings"; await on.ExecuteNonQueryAsync(); }
        Assert.Equal(1, await ViolationsAsync());

        var chart = await ChartAsyncFor();
        var caries = Of(chart, "16", "Caries");
        await Chart(s => s.WithdrawAsync(caries.Id, "Tooth extracted", caries.RowVersion, S.Actor, default));          // putting it right is never refused
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task Two_real_transactions_on_one_tooth_queue_the_second_waits_for_the_first_and_then_sees_its_row()
    {
        await using var first = await OpenAsync();
        await using var second = await OpenAsync();
        await using var tx1 = first.BeginTransaction();
        await InsertAsync(first, tx1, "16", "Missing", "Existing");                  // not committed yet, and it holds the tooth

        await using var tx2 = second.BeginTransaction();
        var waiting = Task.Run(() => InsertAsync(second, tx2, "16", "Caries", "Existing"));
        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(2000)));       // the second writer is held, not racing ahead of an uncommitted row

        tx1.Commit();                                                                  // the first writer finishes...
        var ex = await Assert.ThrowsAsync<SqlException>(() => waiting);                // ...and the second now sees its committed row and is refused
        Assert.Equal(ToothPresenceErrors.Absent, ex.Number);
        tx2.Rollback();
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task The_same_queueing_holds_the_other_way_round_and_a_different_tooth_is_not_held_up()
    {
        await using var first = await OpenAsync();
        await using var second = await OpenAsync();
        await using var third = await OpenAsync();
        await using var tx1 = first.BeginTransaction();
        await InsertAsync(first, tx1, "16", "Caries", "Existing");

        await using var tx3 = third.BeginTransaction();
        await InsertAsync(third, tx3, "26", "Missing", "Existing");                   // another tooth: proceeds at once
        tx3.Commit();

        await using var tx2 = second.BeginTransaction();
        var waiting = Task.Run(() => InsertAsync(second, tx2, "16", "Missing", "Existing"));
        Assert.NotSame(waiting, await Task.WhenAny(waiting, Task.Delay(2000)));
        tx1.Commit();
        var ex = await Assert.ThrowsAsync<SqlException>(() => waiting);
        Assert.Equal(ToothPresenceErrors.HasFindings, ex.Number);
        tx2.Rollback();
        Assert.Equal(0, await ViolationsAsync());
    }

    [Fact]
    public async Task A_second_writer_that_waits_too_long_for_a_tooth_is_refused_as_busy_rather_than_hanging()
    {
        await using var first = await OpenAsync();
        await using var second = await OpenAsync();
        await using var tx1 = first.BeginTransaction();
        await InsertAsync(first, tx1, "16", "Caries", "Existing");                    // held open past the trigger's ten-second wait
        await using var tx2 = second.BeginTransaction();
        var ex = await Assert.ThrowsAsync<SqlException>(() => InsertAsync(second, tx2, "16", "Crown", "Existing"));
        Assert.Equal(ToothPresenceErrors.Busy, ex.Number);
        tx2.Rollback();
        tx1.Rollback();
    }
}
