using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 BREAK and HARDEN, against real SQL Server: an adoption raced against another change to the plan, two adoptions racing for the same plan version, the same adoption submitted twice at once, and a
/// failure in each write the adoption makes (chart history, ledger, audit). Every failure leaves the plan, the chart, the ledger and the log exactly as they were and the same request works afterwards;
/// no race ever produces a second record, charge or chart change.
/// </summary>
public class CompletedProcedureAdoptionHardeningTests : CompletionTestBase
{
    private const string Reason = "Completed before procedure records existed";
    private ProviderProfile _provider = null!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();                                                                      // made before any snapshot
    }

    private AdoptCompletionRequest Request(PlanView plan, int item, string key = "adopt-1", string? version = null) =>
        new(key, version ?? plan.RowVersion, AnnEncounter, _provider.Id, DateTimeOffset.UtcNow.AddSeconds(2), Reason);

    private Task<CompletionResult> Adopt(PlanView plan, int item, string key = "adopt-1", string? version = null, Alveara.Api.Architecture.Time.IPracticeClock? clock = null) =>
        Completion(s => s.AdoptAsync(plan.Id, plan.Items[item].Id, Request(plan, item, key, version), S.Actor, default), clock);

    private async Task<PlanView> LegacyAsync(int count, params int[] completed)
    {
        var plan = await AcceptedPlanAsync(count);
        foreach (var i in completed) await LegacyComplete(plan.Id, plan.Items[i].Id);
        return await PlanNowAsync(plan.Id);
    }

    private async Task<(PlanView Plan, Guid Finding)> LegacyToothAsync()
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", FindingStates.Planned);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        return (await PlanNowAsync(plan.Id), finding);
    }

    private async Task<int> Count(Func<Alveara.Api.Data.AlveraDbContext, Task<int>> count) { await using var db = Fixture.CreateContext(); return await count(db); }

    // ---------- races ----------

    [Fact]
    public async Task A_plan_changed_after_it_was_checked_but_before_the_adoption_is_saved_is_still_a_conflict_and_records_nothing()
    {
        var plan = await LegacyAsync(2, 0);
        var before = await SnapshotAsync(plan.Id);
        // the adoption asks the clock for the time once while it cleans the request and again just before it opens its transaction: the plan is renamed at the second moment, after the version was checked
        var clock = new HookedClock(SchedulingTestSupport.Clock, onCall: 2, () => Svc(s => s.RenameAsync(plan.Id, "Renamed in the gap", plan.RowVersion, S.Actor, default)).GetAwaiter().GetResult());

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Adopt(plan, 0, "gap-1", clock: clock));

        var after = await SnapshotAsync(plan.Id);
        Assert.Equal(before with { Version = after.Version, Audit = after.Audit, Events = after.Events }, after);   // only the rename happened
        Assert.Equal((0, 0, 0), (after.Sets, after.Procedures, after.Charges));
        Assert.Equal(1, await Count(db => db.TreatmentPlanItemStates.CountAsync(s => s.Completion == ItemCompletion.Completed)));   // the legacy completion is exactly as it was

        var fresh = await PlanNowAsync(plan.Id);                                                                  // the person reloads and adopts again
        Assert.Single((await Adopt(fresh, 0, "gap-2")).Procedures);
    }

    [Fact]
    public async Task Two_adoptions_from_the_same_plan_version_leave_exactly_one_winner_and_the_loser_records_nothing()
    {
        var plan = await LegacyAsync(2, 0, 1);
        var outcomes = await Task.WhenAll(Adopt(plan, 0, "race-a").ContinueWith(t => t.Exception?.GetBaseException()), Adopt(plan, 1, "race-b").ContinueWith(t => t.Exception?.GetBaseException()));

        Assert.Single(outcomes, ex => ex is null);
        Assert.IsType<ConcurrencyConflictException>(Assert.Single(outcomes, ex => ex is not null));
        Assert.Equal((1, 1, 1, 1), (await Count(db => db.CompletionSets.CountAsync()), await Count(db => db.CompletedProcedures.CountAsync()), await Count(db => db.BillingEntries.CountAsync()),
            await Count(db => db.AuditLogEntries.CountAsync(a => a.EventType == "CompletionAdopted"))));

        var fresh = await PlanNowAsync(plan.Id);
        var other = await Adopt(fresh, outcomes[0] is null ? 1 : 0, "race-c");                                    // the loser retries from the new version and succeeds
        Assert.Single(other.Procedures);
        Assert.Equal((2, 2, 2), (await Count(db => db.CompletionSets.CountAsync()), await Count(db => db.CompletedProcedures.CountAsync()), await Count(db => db.BillingEntries.CountAsync())));
    }

    [Fact]
    public async Task The_same_adoption_submitted_twice_at_once_never_makes_a_second_record_charge_or_chart_change()
    {
        var (plan, finding) = await LegacyToothAsync();
        var request = Request(plan, 0);
        Task<Exception?> Submit() => Completion(s => s.AdoptAsync(plan.Id, plan.Items[0].Id, request, S.Actor, default)).ContinueWith(t => t.Exception?.GetBaseException());

        var outcomes = await Task.WhenAll(Submit(), Submit());

        Assert.Contains(outcomes, ex => ex is null);                                                              // at least one succeeded (the other replayed or lost the race)
        Assert.All(outcomes, ex => Assert.True(ex is null or ConcurrencyConflictException or TreatmentPlanException, ex?.ToString()));
        Assert.Equal((1, 1, 1, 1), (await Count(db => db.CompletionSets.CountAsync()), await Count(db => db.CompletedProcedures.CountAsync()), await Count(db => db.BillingEntries.CountAsync()),
            await Count(db => db.AuditLogEntries.CountAsync(a => a.EventType == "CompletionAdopted"))));
        Assert.Equal(1, await Count(db => db.ToothFindingVersions.CountAsync(v => v.FindingId == finding && v.State == FindingStates.Completed)));
        Assert.Equal(FindingStates.Completed, await FindingStateAsync(finding));
    }

    // ---------- a failed write undoes every other write ----------

    [Theory]
    [InlineData("ToothFindingVersions")]
    [InlineData("BillingEntries")]
    [InlineData("AuditLogEntries")]
    public async Task A_failure_writing_the_chart_the_ledger_or_the_log_undoes_the_set_the_record_the_chart_and_the_charge_and_the_same_request_then_works(string failing)
    {
        var (plan, finding) = await LegacyToothAsync();
        var before = await SnapshotAsync(plan.Id);
        var when = failing == "AuditLogEntries" ? "WHERE EventType = 'CompletionAdopted'" : "";
        await ExecuteAsync($@"CREATE TRIGGER [TR_Test_RefuseAdoptionWrite] ON [{failing}] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted {when}) THROW 59041, 'write unavailable', 1;
    INSERT INTO [{failing}] SELECT * FROM inserted;
END");

        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => Adopt(plan, 0));
        Assert.Contains(e.Code, new[] { "save_failed", "chart_update_refused", "charge_failed" });
        Assert.DoesNotContain("write unavailable", e.Message);
        Assert.Equal(before, await SnapshotAsync(plan.Id));                                                       // version, states, events, audit, sets, records, ledger and chart history: all as they were
        Assert.Equal(FindingStates.Planned, await FindingStateAsync(finding));

        await ExecuteAsync("DROP TRIGGER [TR_Test_RefuseAdoptionWrite]");
        var again = (await Adopt(plan, 0)).Procedures.Single();                                                   // the same request, same key, same plan version
        Assert.Equal((ChartOutcomes.Updated, ChargeOutcomes.Created), (again.ChartOutcome, again.Charge!.Outcome));
        Assert.Equal(FindingStates.Completed, await FindingStateAsync(finding));
    }

    // ---------- refusals that record nothing ----------

    [Fact]
    public async Task A_withdrawn_plan_cannot_have_a_procedure_adopted_and_nothing_is_recorded()
    {
        var plan = await LegacyAsync(1, 0);
        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Patient moved practice", plan.RowVersion, S.Actor, default));
        var before = await SnapshotAsync(plan.Id);

        var e = await Assert.ThrowsAsync<TreatmentPlanException>(() => Adopt(withdrawn, 0, version: withdrawn.RowVersion));

        Assert.Equal(("plan_withdrawn", 409), (e.Code, e.StatusCode));
        Assert.Equal(before, await SnapshotAsync(plan.Id));
    }

    [Fact]
    public async Task More_than_one_linked_finding_that_could_be_the_procedures_refuses_the_adoption_before_anything_is_written()
    {
        var diagnosis = await DiagnosisAsync();
        var first = await FindingAsync("16", "O", FindingStates.Planned, condition: FindingConditions.Caries);
        var second = await FindingAsync("16", "O", FindingStates.Planned, condition: FindingConditions.Restoration);
        await LinkAsync(diagnosis, first);
        await LinkAsync(diagnosis, second);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        plan = await PlanNowAsync(plan.Id);
        var before = await SnapshotAsync(plan.Id);

        var e = await Assert.ThrowsAsync<AmbiguousChartFindingException>(() => Adopt(plan, 0));

        Assert.Equal(("ambiguous_chart_finding", 409), (e.Code, e.StatusCode));
        Assert.Equal(before, await SnapshotAsync(plan.Id));
        Assert.Equal((FindingStates.Planned, FindingStates.Planned), (await FindingStateAsync(first), await FindingStateAsync(second)));
    }
}
