using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01 against real SQL Server: what completing a set does to the chart and to the ledger. A procedure finds its chart finding only through its plan's diagnosis link on the same tooth and surface; one
/// finding moves to Completed, a completed one is reported as such, nothing matching leaves the chart alone and more than one match refuses the whole set. Every billable procedure gets exactly one charge
/// through the billing contract and a no-charge procedure gets none. A failed charge, a failed chart write or a refused chart rule undoes the whole set, and the readiness the screen reads says what the
/// completion will then do.
/// </summary>
public class CompletedProcedureEffectsTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    private DateTimeOffset? _performed;

    /// <summary>The same performed time for every call of one test (taken after its plan was made and accepted), so a repeated call is the same request.</summary>
    private Task<CompletionResult> CompleteAsync(PlanView plan, IEnumerable<int> items, string key = "set-1", string? version = null) =>
        Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, items, AnnEncounter, _provider.Id, key, _performed ??= DateTimeOffset.UtcNow.AddSeconds(2), version), S.Actor, default));

    private Task<CompletionReadiness> ReadinessAsync(PlanView plan, IReadOnlyList<Guid>? items = null) => Completion(s => s.ReadinessAsync(plan.Id, items, default));

    /// <summary>A diagnosis linked to a chart finding on the tooth, and an accepted plan for it with one procedure on that tooth and surface.</summary>
    private async Task<(PlanView Plan, Guid Finding)> LinkedPlanAsync(string state = FindingStates.Planned, string tooth = "16", string? surface = "O", string condition = FindingConditions.Caries)
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync(tooth, surface, state, condition: condition);
        await LinkAsync(diagnosis, finding);
        return (await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, tooth, surface)), finding);
    }

    private async Task<int> VersionsOfAsync(Guid finding)
    {
        await using var db = Fixture.CreateContext();
        return await db.ToothFindingVersions.CountAsync(v => v.FindingId == finding);
    }

    // ---------- the chart ----------

    [Theory]
    [InlineData(FindingStates.Planned)]
    [InlineData(FindingStates.Diagnosed)]
    public async Task A_linked_planned_or_diagnosed_finding_on_the_procedures_tooth_and_surface_moves_to_completed_with_its_history(string state)
    {
        var (plan, finding) = await LinkedPlanAsync(state);
        var versions = await VersionsOfAsync(finding);

        var result = await CompleteAsync(plan, [0]);

        var p = result.Procedures.Single();
        Assert.Equal((ChartOutcomes.Updated, finding), (p.ChartOutcome, p.ToothFindingId));
        Assert.Equal(FindingStates.Completed, await FindingStateAsync(finding));
        Assert.Equal(versions + 1, await VersionsOfAsync(finding));
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "ToothFindingStateChanged" && a.TargetUserAccountId == finding));
        var row = await db.CompletedProcedures.AsNoTracking().SingleAsync();
        Assert.Equal((ChartOutcomes.Updated, finding), (row.ChartOutcome, row.ToothFindingId));
    }

    [Fact]
    public async Task A_finding_that_is_already_completed_is_reported_as_such_and_left_alone()
    {
        var (plan, finding) = await LinkedPlanAsync(FindingStates.Completed);
        var versions = await VersionsOfAsync(finding);
        var result = await CompleteAsync(plan, [0]);
        Assert.Equal((ChartOutcomes.AlreadyCompleted, finding), (result.Procedures.Single().ChartOutcome, result.Procedures.Single().ToothFindingId));
        Assert.Equal(versions, await VersionsOfAsync(finding));
    }

    [Theory]
    [InlineData("unlinked")]
    [InlineData("existing")]
    [InlineData("otherTooth")]
    [InlineData("otherSurface")]
    [InlineData("wholeMouth")]
    public async Task Without_a_linked_finding_on_the_same_tooth_and_surface_the_chart_is_left_alone(string which)
    {
        PlanView plan;
        Guid? finding = null;
        if (which == "wholeMouth")
        {
            plan = await AcceptedPlanAsync(1);
        }
        else
        {
            var diagnosis = await DiagnosisAsync();
            finding = which switch
            {
                "otherTooth" => await FindingAsync("17", "O"),
                "otherSurface" => await FindingAsync("16", "M"),
                "existing" => await FindingAsync("16", "O", FindingStates.Existing),
                _ => await FindingAsync("16", "O"),
            };
            if (which != "unlinked") await LinkAsync(diagnosis, finding.Value);
            plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"));
        }
        var before = finding is null ? 0 : await VersionsOfAsync(finding.Value);

        var result = await CompleteAsync(plan, [0]);

        Assert.Equal((ChartOutcomes.NotApplicable, (Guid?)null), (result.Procedures.Single().ChartOutcome, result.Procedures.Single().ToothFindingId));
        if (finding is not null) Assert.Equal(before, await VersionsOfAsync(finding.Value));
    }

    [Fact]
    public async Task More_than_one_linked_finding_that_could_be_the_procedures_refuses_the_whole_set_and_names_the_procedure()
    {
        var diagnosis = await DiagnosisAsync();
        await LinkAsync(diagnosis, await FindingAsync("16", "O", FindingStates.Planned, condition: FindingConditions.Caries));
        await LinkAsync(diagnosis, await FindingAsync("16", "O", FindingStates.Planned, condition: FindingConditions.Restoration));
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"), ("FILL-2", 90m, "17", "O"));
        var before = await SnapshotAsync(plan.Id);

        var e = await Assert.ThrowsAsync<AmbiguousChartFindingException>(() => CompleteAsync(plan, [0, 1]));

        Assert.Equal(("ambiguous_chart_finding", 409), (e.Code, e.StatusCode));
        var ambiguity = Assert.Single(e.Items);
        Assert.Equal((1, "16", "O", 2), (ambiguity.ItemNumber, ambiguity.ToothKey, ambiguity.Surface, ambiguity.Matches));
        Assert.Equal(["items.1"], e.FieldErrors.Keys.ToArray());
        Assert.Equal(before, await SnapshotAsync(plan.Id));                                              // procedure 2 was fine and was not completed either
    }

    [Fact]
    public async Task Two_procedures_of_one_set_that_share_a_finding_move_it_once_and_the_second_reports_it_as_already_completed()
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", FindingStates.Planned);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"), ("SEAL-1", 40m, "16", "O"));
        var versions = await VersionsOfAsync(finding);

        var result = await CompleteAsync(plan, [0, 1]);

        Assert.Equal([ChartOutcomes.Updated, ChartOutcomes.AlreadyCompleted], result.Procedures.Select(p => p.ChartOutcome).ToArray());
        Assert.All(result.Procedures, p => Assert.Equal(finding, p.ToothFindingId));
        Assert.Equal(versions + 1, await VersionsOfAsync(finding));
    }

    [Fact]
    public async Task The_charts_own_rules_still_apply_completing_an_extraction_is_refused_while_other_findings_stand_on_the_tooth()
    {
        var diagnosis = await DiagnosisAsync();
        var missing = await FindingAsync("16", null, FindingStates.Planned, condition: FindingConditions.Missing);
        await FindingAsync("16", "O", FindingStates.Diagnosed);                                          // a caries finding on the same tooth
        await LinkAsync(diagnosis, missing);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("EXT-1", 80m, "16", null));
        var before = await SnapshotAsync(plan.Id);

        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => CompleteAsync(plan, [0]));

        Assert.Equal(("chart_update_refused", 409), (e.Code, e.StatusCode));
        Assert.Equal(before, await SnapshotAsync(plan.Id));
        Assert.Equal(FindingStates.Planned, await FindingStateAsync(missing));
    }

    // ---------- the charge ----------

    [Fact]
    public async Task A_set_of_billable_and_no_charge_procedures_charges_the_billable_ones_once_and_the_no_charge_one_not_at_all()
    {
        var diagnosis = await DiagnosisAsync();
        var plan = await AcceptedToothPlanAsync(diagnosis, ("EXAM-1", 0m, "11", null), ("FILL-2", 100m, "14", "O"), ("FILL-3", 60m, "15", "M"));

        var result = await CompleteAsync(plan, [0, 1, 2]);

        Assert.Equal([ChargeOutcomes.None, ChargeOutcomes.Created, ChargeOutcomes.Created], result.Procedures.Select(p => p.Charge!.Outcome).ToArray());
        Assert.Equal([(int?)null, 1, 2], result.Procedures.Select(p => p.Charge!.EntryNumber).ToArray());
        Assert.Equal([(decimal?)null, 100m, 60m], result.Procedures.Select(p => p.Charge!.Amount).ToArray());
        await using var db = Fixture.CreateContext();
        var entries = await db.BillingEntries.AsNoTracking().OrderBy(e => e.EntryNumber).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal(plan.Items.Skip(1).Select(i => i.Id).ToArray(), entries.Select(e => e.PlanItemId!.Value).ToArray());           // each charge names its procedure
        Assert.All(entries, e => Assert.Equal(result.CorrelationId, e.CorrelationId));                                                // and the set that made it
    }

    [Fact]
    public async Task A_set_of_only_no_charge_procedures_opens_no_account_and_a_replay_reports_the_same_charges()
    {
        var diagnosis = await DiagnosisAsync();
        var plan = await AcceptedToothPlanAsync(diagnosis, ("EXAM-1", 0m, "11", null));
        var first = await CompleteAsync(plan, [0]);
        Assert.Equal((0, 0), ((await SnapshotAsync(plan.Id)).Charges, (await SnapshotAsync(plan.Id)).Accounts));

        var again = await CompleteAsync(plan, [0]);
        Assert.True(again.Replayed);
        Assert.Equal(first.Procedures.Select(p => p.Charge), again.Procedures.Select(p => p.Charge));
    }

    [Fact]
    public async Task A_replay_reports_the_original_charges_as_created_and_makes_no_second_one()
    {
        var plan = await AcceptedPlanAsync(2);
        var first = await CompleteAsync(plan, [0, 1]);
        var charges = (await SnapshotAsync(plan.Id)).Charges;

        var again = await CompleteAsync(plan, [1, 0]);

        Assert.Equal(first.Procedures.Select(p => p.Charge), again.Procedures.Select(p => p.Charge));
        Assert.All(again.Procedures, p => Assert.Equal(ChargeOutcomes.Created, p.Charge!.Outcome));
        Assert.Equal(charges, (await SnapshotAsync(plan.Id)).Charges);
    }

    // ---------- failure: a coupled effect fails after others succeeded ----------

    [Fact]
    public async Task A_failed_charge_for_the_second_of_three_procedures_undoes_the_first_charge_the_account_and_every_completion()
    {
        var plan = await AcceptedPlanAsync(3);                                                           // codes LOCAL-500, LOCAL-501, LOCAL-502, each fee 100
        var before = await SnapshotAsync(plan.Id);
        await ExecuteAsync(@"CREATE TRIGGER [TR_Test_RefuseSecondCharge] ON [BillingEntries] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE ProcedureCode = 'LOCAL-501') THROW 59041, 'ledger unavailable', 1;
END");

        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => CompleteAsync(plan, [0, 1, 2]));

        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("ledger", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await SnapshotAsync(plan.Id));                                              // no completion, record, chart change, account, charge or audit remains

        await ExecuteAsync("DROP TRIGGER [TR_Test_RefuseSecondCharge]");
        var again = await CompleteAsync(plan, [0, 1, 2]);                                                 // the same submission works once the cause is gone
        Assert.Equal(3, again.Procedures.Count);
        Assert.Equal(3, (await SnapshotAsync(plan.Id)).Charges);
    }

    [Fact]
    public async Task A_failed_chart_write_undoes_the_whole_set_including_the_completion_states_already_saved()
    {
        var (plan, finding) = await LinkedPlanAsync();
        var before = await SnapshotAsync(plan.Id);
        await ExecuteAsync(@"CREATE TRIGGER [TR_Test_RefuseChartHistory] ON [ToothFindingVersions] AFTER INSERT AS
BEGIN
    THROW 59042, 'chart unavailable', 1;
END");

        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => CompleteAsync(plan, [0]));

        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.Equal(before, await SnapshotAsync(plan.Id));
        Assert.Equal(FindingStates.Planned, await FindingStateAsync(finding));

        await ExecuteAsync("DROP TRIGGER [TR_Test_RefuseChartHistory]");
        Assert.Equal(ChartOutcomes.Updated, (await CompleteAsync(plan, [0])).Procedures.Single().ChartOutcome);
    }

    // ---------- readiness ----------

    [Fact]
    public async Task Readiness_says_what_a_completion_will_do_and_the_completion_then_does_exactly_that()
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", FindingStates.Planned);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"), ("EXAM-1", 0m, "11", null), ("FILL-2", 100m, "12", "M"));
        var before = await SnapshotAsync(plan.Id);

        var readiness = await ReadinessAsync(plan);

        Assert.Equal(before, await SnapshotAsync(plan.Id));                                              // reading it writes nothing
        Assert.Equal(plan.RowVersion, readiness.RowVersion);
        Assert.All(readiness.Items, r => Assert.True(r.Ready));
        Assert.Equal([ChartOutcomes.Updated, ChartOutcomes.NotApplicable, ChartOutcomes.NotApplicable], readiness.Items.Select(r => r.ChartEffect).ToArray());
        Assert.Equal([true, false, true], readiness.Items.Select(r => r.Billable).ToArray());
        Assert.Equal([140m, 0m, 100m], readiness.Items.Select(r => r.Fee).ToArray());

        var result = await CompleteAsync(plan, [0, 1, 2]);
        Assert.Equal(readiness.Items.Select(r => r.ChartEffect).ToArray(), result.Procedures.Select(p => p.ChartOutcome).ToArray());
        Assert.Equal(readiness.Items.Select(r => r.Billable).ToArray(), result.Procedures.Select(p => p.Charge!.Outcome != ChargeOutcomes.None).ToArray());
    }

    [Fact]
    public async Task Readiness_names_the_procedures_that_cannot_be_completed_and_an_ambiguous_chart_match_with_how_many_matched()
    {
        var diagnosis = await DiagnosisAsync();
        await LinkAsync(diagnosis, await FindingAsync("16", "O", FindingStates.Planned, condition: FindingConditions.Caries));
        await LinkAsync(diagnosis, await FindingAsync("16", "O", FindingStates.Planned, condition: FindingConditions.Restoration));
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"), ("FILL-2", 90m, "17", "O"), ("FILL-3", 50m, "18", "O"));
        var done = await Svc(s => s.CompleteItemAsync(plan.Id, plan.Items[1].Id, S.Actor, default));                          // completed the old way
        var withdrawn = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[2].Id, "Entered in error", done.RowVersion, S.Actor, default));

        var readiness = await ReadinessAsync(withdrawn, withdrawn.Items.Select(i => i.Id).ToList());          // the default listing leaves withdrawn procedures out; asking for them says why

        Assert.Equal([false, false, false], readiness.Items.Select(r => r.Ready).ToArray());
        Assert.Equal(["ambiguous_chart_finding", "already_completed", "withdrawn"], readiness.Items.Select(r => r.NotReadyReason!).ToArray());
        Assert.Equal((2, 0, 0), (readiness.Items[0].ChartMatches, readiness.Items[1].ChartMatches, readiness.Items[2].ChartMatches));
        Assert.Equal(withdrawn.RowVersion, readiness.RowVersion);

        var asked = await ReadinessAsync(withdrawn, [withdrawn.Items[0].Id]);
        Assert.Single(asked.Items);
        var unknown = await Assert.ThrowsAsync<TreatmentPlanException>(() => ReadinessAsync(withdrawn, [Guid.NewGuid()]));
        Assert.Equal("validation_failed", unknown.Code);
        var gone = await Assert.ThrowsAsync<TreatmentPlanException>(() => Completion(s => s.ReadinessAsync(Guid.NewGuid(), null, default)));
        Assert.Equal(("plan_not_found", 404), (gone.Code, gone.StatusCode));
    }
}
