using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01 against real SQL Server: completing a selected set of one plan's procedures. The set is one transaction (the plan's version, every procedure's state, history and audit entry, the set and every
/// completed-procedure record), a retry is recognised by its key and canonical request, a stale plan or an overlapping set is a conflict that records nothing, each wrong field and each procedure that cannot be
/// completed is named, a failed log undoes everything, and the encounter is only ever read.
/// </summary>
/// <summary>The real practice clock that runs one action the moment its time is asked for the Nth time, so a test can change something between two steps of an operation without any timing luck.</summary>
internal sealed class HookedClock(Alveara.Api.Architecture.Time.IPracticeClock inner, int onCall, Action action) : Alveara.Api.Architecture.Time.IPracticeClock
{
    private int _calls;
    public TimeZoneInfo PracticeTimeZone => inner.PracticeTimeZone;
    public DateTimeOffset UtcNow { get { if (++_calls == onCall) action(); return inner.UtcNow; } }
    public DateTimeOffset ToPracticeLocal(DateTimeOffset utcInstant) => inner.ToPracticeLocal(utcInstant);
    public DateTimeOffset FromPracticeLocal(DateTime localTime, Alveara.Api.Architecture.Time.LocalTimeAmbiguityPolicy ambiguityPolicy = Alveara.Api.Architecture.Time.LocalTimeAmbiguityPolicy.Reject) => inner.FromPracticeLocal(localTime, ambiguityPolicy);
}

public class CompletedProcedureServiceTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;
    private Task<ProviderProfile> Provider() => Task.FromResult(_provider);

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();                      // made before any snapshot, so counting audit entries is only about the completion
    }

    private async Task<CompletionResult> CompleteAsync(PlanView plan, IEnumerable<int> items, string key = "set-1", DateTimeOffset? performed = null, string? version = null, Guid? encounter = null)
    {
        var request = RequestFor(plan, items, encounter ?? AnnEncounter, (await Provider()).Id, key, performed, version);
        return await Completion(s => s.CompleteAsync(plan.Id, request, S.Actor, default));
    }

    private static async Task<TreatmentPlanException> RefusedAsync(Func<Task<CompletionResult>> action) => await Assert.ThrowsAsync<TreatmentPlanException>(action);

    // ---------- the happy path ----------

    [Fact]
    public async Task A_selected_subset_is_completed_in_one_set_with_a_record_a_history_event_and_an_audit_entry_for_each_and_the_plans_version_moves()
    {
        var plan = await AcceptedPlanAsync(3);
        var before = await SnapshotAsync(plan.Id);
        var performed = DateTimeOffset.UtcNow.AddSeconds(2);                                    // after the plan was made and accepted, within the clock tolerance

        var result = await CompleteAsync(plan, [0, 2], performed: performed);

        Assert.False(result.Replayed);
        Assert.Equal([1, 3], result.Procedures.Select(p => p.ItemNumber).ToArray());
        Assert.Equal(["Completed", "NotStarted", "Completed", "NotStarted"], result.Plan.Items.Select(i => i.Completion).ToArray());     // the second accepted one and the pending one are untouched
        Assert.All(result.Procedures, p =>
        {
            Assert.Equal((AnnEncounter, _provider.Id, CompletionRules.NormalizeTime(performed), plan.CurrentRevision), (p.EncounterId, p.ProviderProfileId, p.PerformedAtUtc, p.PlanRevisionNumber));
            Assert.Equal(ChartOutcomes.NotApplicable, p.ChartOutcome);
        });

        var after = await SnapshotAsync(plan.Id);
        Assert.Equal((before.States + 2, before.Events + 2, before.Sets + 1, before.Procedures + 2), (after.States, after.Events, after.Sets, after.Procedures));
        Assert.Equal((before.Charges + 2, before.Accounts + 1), (after.Charges, after.Accounts));                                      // one charge per procedure (fee 100 each), on one new account
        Assert.All(result.Procedures, p => Assert.Equal((ChargeOutcomes.Created, 100m), (p.Charge!.Outcome, p.Charge.Amount)));
        Assert.NotEqual(before.Version, after.Version);
        Assert.NotNull(result.Plan.UpdatedAtUtc);

        await using var db = Fixture.CreateContext();
        var set = await db.CompletionSets.AsNoTracking().SingleAsync();
        var clean = CompletionRules.Clean(RequestFor(plan, [0, 2], AnnEncounter, _provider.Id, "set-1", performed), DateTimeOffset.UtcNow);
        Assert.Equal((CompletionRules.RequestHash(clean), 2, plan.CurrentRevision, result.CorrelationId), (set.RequestHash, set.ItemCount, set.PlanRevisionNumber, set.CorrelationId));
        var rows = await db.CompletedProcedures.AsNoTracking().OrderBy(c => c.PlanItemId).ToListAsync();
        Assert.All(rows, r => Assert.Equal((set.Id, plan.PatientId, plan.Id, S.Actor), (r.SetId, r.PatientId, r.PlanId, r.FinalizedByUserId)));
        Assert.Equal(plan.Items.Where((_, i) => i is 0 or 2).Select(i => i.ProcedureVersionId).Order().ToArray(), rows.Select(r => r.ProcedureVersionId).Order().ToArray());     // linked to the exact catalog version

        var events = (await Svc(s => s.HistoryAsync(plan.Id, default))).Where(e => e.ChangeType == "ItemCompleted").ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(events.Count, events.Select(e => e.EventNumber).Distinct().Count());
        Assert.All(events, e => Assert.Equal(plan.CurrentRevision, e.RevisionNumber));
        var audits = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "TreatmentPlanItemCompleted" || a.EventType == "CompletionSetRecorded").Select(a => a.EventType).ToListAsync();
        Assert.Equal((2, 1), (audits.Count(a => a == "TreatmentPlanItemCompleted"), audits.Count(a => a == "CompletionSetRecorded")));
    }

    [Fact]
    public async Task One_procedure_can_be_completed_alone_and_the_rest_complete_later_in_another_set()
    {
        var plan = await AcceptedPlanAsync(2);
        var first = await CompleteAsync(plan, [1]);
        Assert.Single(first.Procedures);
        var second = await CompleteAsync(first.Plan, [0], key: "set-2", version: first.Plan.RowVersion);
        Assert.Equal(["Completed", "Completed", "NotStarted"], second.Plan.Items.Select(i => i.Completion).ToArray());
        Assert.Equal((2, 2), ((await SnapshotAsync(plan.Id)).Sets, (await SnapshotAsync(plan.Id)).Procedures));
    }

    // ---------- retries ----------

    [Fact]
    public async Task The_same_key_and_request_replays_the_saved_result_with_no_writes_even_from_an_out_of_date_screen()
    {
        var plan = await AcceptedPlanAsync(3);
        var performed = DateTimeOffset.UtcNow.AddSeconds(2);
        var first = await CompleteAsync(plan, [0, 1], performed: performed);
        var afterFirst = await SnapshotAsync(plan.Id);

        var again = await CompleteAsync(plan, [1, 0], performed: performed);                            // the old row version, and the procedures picked in another order

        Assert.True(again.Replayed);
        Assert.Equal((first.SetId, first.CorrelationId), (again.SetId, again.CorrelationId));
        Assert.Equal(first.Procedures.Select(p => p.Id).ToArray(), again.Procedures.Select(p => p.Id).ToArray());
        Assert.Equal(afterFirst, await SnapshotAsync(plan.Id));
    }

    [Fact]
    public async Task The_same_key_with_a_different_request_is_a_conflict_and_records_nothing()
    {
        var plan = await AcceptedPlanAsync(3);
        var first = await CompleteAsync(plan, [0]);
        var after = await SnapshotAsync(plan.Id);

        var e = await RefusedAsync(() => CompleteAsync(first.Plan, [1], version: first.Plan.RowVersion));       // set-1 again, now asking for something else
        Assert.Equal(("idempotency_conflict", 409), (e.Code, e.StatusCode));
        Assert.Equal(after, await SnapshotAsync(plan.Id));
    }

    [Fact]
    public async Task The_same_procedures_under_a_new_key_after_success_are_refused_as_already_completed_and_never_charged_or_recorded_twice()
    {
        var plan = await AcceptedPlanAsync(2);
        var first = await CompleteAsync(plan, [0, 1]);
        var after = await SnapshotAsync(plan.Id);

        var e = await Assert.ThrowsAsync<ItemsNotCompletableException>(() => CompleteAsync(first.Plan, [0, 1], key: "set-2", version: first.Plan.RowVersion));
        Assert.Equal(["already_completed", "already_completed"], e.Items.Select(i => i.Reason).ToArray());
        Assert.Equal(after, await SnapshotAsync(plan.Id));
    }

    // ---------- stale and racing ----------

    [Fact]
    public async Task A_completion_advances_the_plans_version_so_a_second_screen_holding_the_old_one_is_refused_until_it_reloads()
    {
        var plan = await AcceptedPlanAsync(3);
        var first = await CompleteAsync(plan, [0]);
        var after = await SnapshotAsync(plan.Id);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => CompleteAsync(plan, [1], key: "set-2"));       // plan.RowVersion is the version before the first set
        Assert.Equal(after, await SnapshotAsync(plan.Id));

        var second = await CompleteAsync(first.Plan, [1], key: "set-2", version: first.Plan.RowVersion);
        Assert.Equal(["Completed", "Completed", "NotStarted", "NotStarted"], second.Plan.Items.Select(i => i.Completion).ToArray());
    }

    [Fact]
    public async Task A_plan_changed_since_it_was_read_is_a_conflict_and_nothing_is_recorded()
    {
        var plan = await AcceptedPlanAsync(2);
        await Svc(s => s.RenameAsync(plan.Id, "Renamed by someone else", plan.RowVersion, S.Actor, default));
        var before = await SnapshotAsync(plan.Id);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => CompleteAsync(plan, [0, 1]));
        Assert.Equal(before, await SnapshotAsync(plan.Id));
        var missing = await RefusedAsync(() => Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0], AnnEncounter, _provider.Id, "set-9") with { RowVersion = null }, S.Actor, default)));
        Assert.Equal(("row_version_required", 400), (missing.Code, missing.StatusCode));
    }

    [Fact]
    public async Task Two_overlapping_sets_submitted_at_once_leave_exactly_one_winner_and_each_procedure_completed_once()
    {
        var plan = await AcceptedPlanAsync(3);
        var provider = await Provider();
        Task<CompletionResult> Submit(string key, int[] items) => Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, items, AnnEncounter, provider.Id, key), S.Actor, default));

        var outcomes = await Task.WhenAll(Submit("race-a", [0, 1]).ContinueWith(t => t.Exception?.GetBaseException()), Submit("race-b", [1, 2]).ContinueWith(t => t.Exception?.GetBaseException()));

        Assert.Single(outcomes, ex => ex is null);                                                       // exactly one won
        var loser = Assert.Single(outcomes, ex => ex is not null)!;
        Assert.True(loser is ConcurrencyConflictException or ItemsNotCompletableException, loser.ToString());
        await using var db = Fixture.CreateContext();
        var completed = await db.TreatmentPlanItemStates.Where(s => s.Completion == ItemCompletion.Completed).Select(s => s.ItemId).ToListAsync();
        Assert.Equal(completed.Distinct().Count(), completed.Count);                                     // no procedure was completed twice
        Assert.Equal(completed.Count, await db.CompletedProcedures.CountAsync());
        Assert.Equal(1, await db.CompletionSets.CountAsync());
    }

    [Fact]
    public async Task A_plan_changed_after_it_was_checked_but_before_the_set_is_saved_is_still_a_conflict_and_records_nothing()
    {
        var plan = await AcceptedPlanAsync(2);
        var before = await SnapshotAsync(plan.Id);
        var provider = await Provider();
        // The completion asks the clock for the time once while it checks the request and a second time just before it opens its transaction: the plan is renamed at that second moment,
        // after the version was checked and before anything is saved. Only the version check inside the save itself can catch that.
        var clock = new HookedClock(SchedulingTestSupport.Clock, onCall: 2, () => Svc(s => s.RenameAsync(plan.Id, "Renamed in the gap", plan.RowVersion, S.Actor, default)).GetAwaiter().GetResult());

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0, 1], AnnEncounter, provider.Id, "gap-1"), S.Actor, default), clock));

        var after = await SnapshotAsync(plan.Id);
        Assert.Equal(before with { Version = after.Version, Audit = after.Audit, Events = after.Events }, after);                  // only the rename (its history event, its audit entry and the new version) happened
        Assert.Equal(before.Events + 1, after.Events);
        Assert.Equal((0, 0, 0), (after.Sets, after.Procedures, after.Charges));
        var now = await PlanNowAsync(plan.Id);
        Assert.Equal("Renamed in the gap", now.Title);
        Assert.Equal(["NotStarted", "NotStarted", "NotStarted"], now.Items.Select(i => i.Completion).ToArray());
    }

    // ---------- what cannot be completed ----------

    [Fact]
    public async Task Every_procedure_that_cannot_be_completed_is_listed_with_its_reason_and_none_of_the_others_is_completed_either()
    {
        var plan = await AcceptedPlanAsync(3);
        var done = await LegacyComplete(plan.Id, plan.Items[0].Id);                          // completed the old way
        var withdrawn = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[1].Id, "Entered in error", done.RowVersion, S.Actor, default));
        var before = await SnapshotAsync(plan.Id);

        var e = await Assert.ThrowsAsync<ItemsNotCompletableException>(() => CompleteAsync(withdrawn, [0, 1, 2, 3], version: withdrawn.RowVersion));
        Assert.Equal([("already_completed", 1), ("withdrawn", 2), ("not_accepted", 4)], e.Items.Select(i => (i.Reason, i.ItemNumber)).ToArray());
        Assert.Equal(["items.1", "items.2", "items.4"], e.FieldErrors.Keys.ToArray());
        Assert.Equal(409, e.StatusCode);
        Assert.Equal(before, await SnapshotAsync(plan.Id));                                              // procedure 3 was fine, and still was not completed
    }

    [Fact]
    public async Task A_withdrawn_plan_and_an_unknown_plan_complete_nothing()
    {
        var plan = await AcceptedPlanAsync(2);
        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Patient moved practice", plan.RowVersion, S.Actor, default));
        var gone = await RefusedAsync(() => CompleteAsync(withdrawn, [0], version: withdrawn.RowVersion));
        Assert.Equal(("plan_withdrawn", 409), (gone.Code, gone.StatusCode));

        var unknown = await RefusedAsync(() => Completion(s => s.CompleteAsync(Guid.NewGuid(), RequestFor(plan, [0], AnnEncounter, _provider.Id), S.Actor, default)));
        Assert.Equal(("plan_not_found", 404), (unknown.Code, unknown.StatusCode));
    }

    // ---------- wrong references ----------

    [Fact]
    public async Task A_procedure_of_another_plan_another_patients_encounter_and_an_unknown_or_inactive_provider_are_each_named_and_nothing_is_recorded()
    {
        var plan = await AcceptedPlanAsync(2);
        var other = await AcceptedPlanAsync(1, firstCode: 700);
        var provider = await Provider();
        var before = await SnapshotAsync(plan.Id);

        var foreignItem = await RefusedAsync(() => Completion(s => s.CompleteAsync(plan.Id, new CompleteProceduresRequest("k", plan.RowVersion, AnnEncounter, provider.Id, DateTimeOffset.UtcNow, [plan.Items[0].Id, other.Items[0].Id]), S.Actor, default)));
        Assert.Equal(["itemIds"], foreignItem.FieldErrors.Keys.ToArray());

        var wrongEncounter = await RefusedAsync(() => CompleteAsync(plan, [0], encounter: BoEncounter));
        Assert.Equal(["encounterId"], wrongEncounter.FieldErrors.Keys.ToArray());

        var unknownProvider = await RefusedAsync(() => Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0], AnnEncounter, Guid.NewGuid()), S.Actor, default)));
        Assert.Equal(["providerId"], unknownProvider.FieldErrors.Keys.ToArray());

        await ExecuteAsync("UPDATE [ProviderProfiles] SET [IsActive] = 0 WHERE [Id] = {0}", provider.Id);
        var inactive = await RefusedAsync(() => CompleteAsync(plan, [0]));
        Assert.Equal(["providerId"], inactive.FieldErrors.Keys.ToArray());
        Assert.Equal(before, await SnapshotAsync(plan.Id));
    }

    [Theory]
    [InlineData("Draft")]
    [InlineData("Finalized")]
    public async Task A_draft_or_a_finalized_encounter_can_be_named_and_the_encounter_itself_is_never_changed(string status)
    {
        var plan = await AcceptedPlanAsync(1);
        if (status == "Finalized")
            await ExecuteAsync("UPDATE [Encounters] SET [Status] = 'Finalized', [FinalizedAtUtc] = SYSDATETIMEOFFSET(), [FinalizedByUserId] = {0} WHERE [Id] = {1}", S.Actor, AnnEncounter);
        await using (var check = Fixture.CreateContext()) Assert.Equal(status, (await check.Encounters.AsNoTracking().SingleAsync(e => e.Id == AnnEncounter)).Status);
        var rowBefore = await EncounterRowAsync();

        var result = await CompleteAsync(plan, [0]);

        Assert.Equal(AnnEncounter, result.Procedures.Single().EncounterId);
        Assert.Equal(rowBefore, await EncounterRowAsync());                                              // not touched: same status, same update stamp, same row version
    }

    private async Task<string> EncounterRowAsync()
    {
        await using var db = Fixture.CreateContext();
        var e = await db.Encounters.AsNoTracking().SingleAsync(x => x.Id == AnnEncounter);
        return $"{e.Status}|{e.UpdatedAtUtc:O}|{e.UpdatedByUserId}|{e.FinalizedAtUtc:O}|{e.SignedAtUtc:O}|{Convert.ToBase64String(e.RowVersion)}";
    }

    // ---------- the performed time ----------

    [Fact]
    public async Task The_performed_time_may_be_five_minutes_ahead_of_the_practice_clock_and_not_one_millisecond_more()
    {
        var plan = await AcceptedPlanAsync(3);
        var now = CompletionRules.NormalizeTime(DateTimeOffset.UtcNow.AddMinutes(1));
        var clock = new TestClock(now);
        var provider = await Provider();
        Task<CompletionResult> At(DateTimeOffset when, int item, string key, string version) =>
            Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [item], AnnEncounter, provider.Id, key, when, version), S.Actor, default), clock);

        var tooLate = await RefusedAsync(() => At(now.AddMinutes(5).AddMilliseconds(1), 0, "late", plan.RowVersion));
        Assert.Equal(["performedAtUtc"], tooLate.FieldErrors.Keys.ToArray());
        Assert.Equal(0, (await SnapshotAsync(plan.Id)).Sets);

        var edge = await At(now.AddMinutes(5), 0, "edge", plan.RowVersion);
        Assert.Equal(now.AddMinutes(5), edge.Procedures.Single().PerformedAtUtc);
    }

    [Fact]
    public async Task A_performed_time_before_the_plan_existed_or_before_the_procedure_was_accepted_is_refused_and_says_what_it_is_compared_with()
    {
        var plan = await AcceptedPlanAsync(2);
        var beforePlan = await RefusedAsync(() => CompleteAsync(plan, [0], performed: plan.CreatedAtUtc.AddMinutes(-1)));
        Assert.Contains("before this plan was created", beforePlan.FieldErrors["performedAtUtc"]);

        var accepted = await AcceptedAtAsync(plan.Items[0].Id);
        var beforeAcceptance = await RefusedAsync(() => CompleteAsync(plan, [0], performed: accepted.AddMilliseconds(-1)));
        Assert.Contains("recorded as accepted", beforeAcceptance.FieldErrors["performedAtUtc"]);
        Assert.Contains("the time the acceptance was recorded in this plan, not when the patient may have agreed", beforeAcceptance.FieldErrors["performedAtUtc"]);
        Assert.Equal(0, (await SnapshotAsync(plan.Id)).Sets);

        var atAcceptance = await CompleteAsync(plan, [0], performed: accepted.AddMilliseconds(1));       // a moment after it was recorded is allowed
        Assert.Single(atAcceptance.Procedures);
    }

    [Fact]
    public async Task Scheduling_a_procedure_does_not_move_the_time_it_must_not_be_performed_before()
    {
        var plan = await AcceptedPlanAsync(1);
        var accepted = await AcceptedAtAsync(plan.Items[0].Id);
        await Svc(s => s.ScheduleItemAsync(plan.Id, plan.Items[0].Id, true, new DateOnly(2031, 1, 5), S.Actor, default));        // a later row that is also Accepted
        var result = await CompleteAsync(plan, [0], performed: accepted.AddMilliseconds(1), version: (await PlanNowAsync(plan.Id)).RowVersion);
        Assert.Single(result.Procedures);
    }

    // ---------- failure: the log is unavailable ----------

    [Theory]
    [InlineData("TreatmentPlanItemCompleted")]
    [InlineData("CompletionSetRecorded")]
    public async Task A_failed_audit_write_in_either_save_of_the_transaction_undoes_every_record_and_says_so(string failing)
    {
        var plan = await AcceptedPlanAsync(3);
        var before = await SnapshotAsync(plan.Id);
        await ExecuteAsync($@"CREATE TRIGGER [TR_Test_RefuseCompletionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = '{failing}') THROW 59040, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");

        var e = await RefusedAsync(() => CompleteAsync(plan, [0, 1, 2]));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await SnapshotAsync(plan.Id));                                              // the plan version, states, events, audit, set and records: all as they were

        await ExecuteAsync("DROP TRIGGER [TR_Test_RefuseCompletionAudit]");
        var again = await CompleteAsync(plan, [0, 1, 2]);                                                 // the same click works once the log is back
        Assert.Equal(3, again.Procedures.Count);
    }
}
