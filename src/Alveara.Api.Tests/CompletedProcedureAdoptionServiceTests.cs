using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 against real SQL Server: adopting ONE procedure that was completed the old, state-only way. The adoption records the provider, encounter, performed time and reason, the immutable completed-procedure
/// record, the chart effect and the procedure's one charge (an existing manual charge is reused), advances the plan's version and writes one audit entry, while the legacy state row, event and audit entry stay
/// exactly as they were and no new completion state is staged. A procedure that is not a legacy completion, a wrong provider, encounter or time, and a stale plan are refused and record nothing; the same request
/// replays; and the request's identity binds the operation and the reason (review correction C1), so anything else under the same key, an ordinary completion's key included, is a conflict.
/// </summary>
public class CompletedProcedureAdoptionServiceTests : CompletionTestBase
{
    private const string Reason = "Completed before procedure records existed";
    private ProviderProfile _provider = null!;
    private DateTimeOffset? _performed;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
    }

    /// <summary>An accepted plan of <paramref name="count"/> procedures (fee 100 each) and one pending, with the listed procedures completed the old, state-only way.</summary>
    private async Task<PlanView> LegacyAsync(int count = 2, params int[] completed)
    {
        var plan = await AcceptedPlanAsync(count);
        foreach (var i in completed) await LegacyComplete(plan.Id, plan.Items[i].Id);
        return await PlanNowAsync(plan.Id);
    }

    private Task<CompletionResult> Adopt(PlanView plan, int item, string key = "adopt-1", string? reason = Reason, string? version = null, DateTimeOffset? performed = null, Guid? encounter = null, Guid? provider = null) =>
        Completion(s => s.AdoptAsync(plan.Id, plan.Items[item].Id,
            new AdoptCompletionRequest(key, version ?? plan.RowVersion, encounter ?? AnnEncounter, provider ?? _provider.Id, performed ?? (_performed ??= DateTimeOffset.UtcNow.AddSeconds(2)), reason), S.Actor, default));

    private static async Task<TreatmentPlanException> RefusedAsync(Func<Task<CompletionResult>> action) => await Assert.ThrowsAsync<TreatmentPlanException>(action);

    private async Task<(int Sets, int Procedures, int Entries, int Audit, int States, int Events)> CountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.CompletionSets.CountAsync(), await db.CompletedProcedures.CountAsync(), await db.BillingEntries.CountAsync(), await db.AuditLogEntries.CountAsync(),
            await db.TreatmentPlanItemStates.CountAsync(), await db.TreatmentPlanEvents.CountAsync());
    }

    // ---------- the adoption ----------

    [Fact]
    public async Task An_unbilled_legacy_completion_is_adopted_with_its_record_one_charge_the_reason_and_a_new_plan_version()
    {
        var plan = await LegacyAsync(1, 0);
        var result = await Adopt(plan, 0);

        Assert.False(result.Replayed);
        Assert.Equal(Reason, result.AdoptionReason);
        var p = result.Procedures.Single();
        Assert.Equal((AnnEncounter, _provider.Id, ChartOutcomes.NotApplicable), (p.EncounterId, p.ProviderProfileId, p.ChartOutcome));
        Assert.Equal(_performed!.Value.AddTicks(-(_performed.Value.Ticks % TimeSpan.TicksPerMillisecond)), p.PerformedAtUtc);
        Assert.Equal((ChargeOutcomes.Created, 1, 100m), (p.Charge!.Outcome, p.Charge.EntryNumber, p.Charge.Amount));
        Assert.NotEqual(plan.RowVersion, result.Plan.RowVersion);                                          // a plan screen opened before this is now stale

        await using var db = Fixture.CreateContext();
        var set = await db.CompletionSets.AsNoTracking().SingleAsync();
        Assert.Equal((Reason, 1, "adopt-1"), (set.AdoptionReason, set.ItemCount, set.IdempotencyKey));
        Assert.Equal(set.Id, (await db.CompletedProcedures.AsNoTracking().SingleAsync()).SetId);
        Assert.Equal(set.CorrelationId, (await db.BillingEntries.AsNoTracking().SingleAsync()).CorrelationId);
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "CompletionAdopted"));
        Assert.Equal(0, await db.AuditLogEntries.CountAsync(a => a.EventType == "CompletionSetRecorded"));   // an adoption is not an ordinary set
    }

    [Fact]
    public async Task The_legacy_state_row_event_and_audit_entry_are_not_rewritten_and_nothing_is_added_to_them()
    {
        var plan = await LegacyAsync(2, 0, 1);
        await using var before = Fixture.CreateContext();
        var states = await before.TreatmentPlanItemStates.AsNoTracking().OrderBy(s => s.ItemId).ThenBy(s => s.Sequence).Select(s => new { s.Id, s.ItemId, s.Sequence, s.Completion, s.Acceptance, s.RecordedAtUtc }).ToListAsync();
        var events = await before.TreatmentPlanEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Id).ToListAsync();
        var audits = await before.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "TreatmentPlanItemCompleted").OrderBy(a => a.Id).Select(a => new { a.Id, a.Details, a.TimestampUtc }).ToListAsync();

        await Adopt(plan, 0);
        await Adopt(plan, 1, key: "adopt-2", version: (await PlanNowAsync(plan.Id)).RowVersion);

        await using var after = Fixture.CreateContext();
        Assert.Equal(states, await after.TreatmentPlanItemStates.AsNoTracking().OrderBy(s => s.ItemId).ThenBy(s => s.Sequence).Select(s => new { s.Id, s.ItemId, s.Sequence, s.Completion, s.Acceptance, s.RecordedAtUtc }).ToListAsync());
        Assert.Equal(events, await after.TreatmentPlanEvents.AsNoTracking().OrderBy(e => e.Id).Select(e => e.Id).ToListAsync());
        Assert.Equal(audits, await after.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "TreatmentPlanItemCompleted").OrderBy(a => a.Id).Select(a => new { a.Id, a.Details, a.TimestampUtc }).ToListAsync());
    }

    [Fact]
    public async Task A_charge_that_billing_already_made_by_hand_is_reused_never_duplicated()
    {
        var plan = await LegacyAsync(1, 0);
        await WithDb(db => new BillingService(db, SchedulingTestSupport.Clock).RecordChargeAsync(Ann, new ChargeInput(plan.Items[0].Id, "c-1"), S.Actor, default));

        var p = (await Adopt(plan, 0)).Procedures.Single();

        Assert.Equal((ChargeOutcomes.Existing, 1, 100m), (p.Charge!.Outcome, p.Charge.EntryNumber, p.Charge.Amount));
        await using var db = Fixture.CreateContext();
        var entry = await db.BillingEntries.AsNoTracking().SingleAsync();
        Assert.Null(entry.CorrelationId);                                                                 // the manual charge is untouched: it does not take the adoption's correlation id
    }

    [Fact]
    public async Task A_procedure_with_no_fee_is_adopted_with_its_record_and_no_charge()
    {
        var diagnosis = await DiagnosisAsync();
        var exam = await ProcedureAsync("LOCAL-900", fee: 0m, description: "Periodic exam");
        var plan = await CreateAsync([Item(diagnosis, exam.Summary.Id)]);
        plan = await Svc(s => s.DecideItemAsync(plan.Id, plan.Items[0].Id, "Accepted", null, null, plan.RowVersion, S.Actor, default));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        plan = await PlanNowAsync(plan.Id);

        var p = (await Adopt(plan, 0)).Procedures.Single();

        Assert.Equal((ChargeOutcomes.None, (int?)null, (decimal?)null), (p.Charge!.Outcome, p.Charge.EntryNumber, p.Charge.Amount));
        var counts = await CountsAsync();
        Assert.Equal((1, 1, 0), (counts.Sets, counts.Procedures, counts.Entries));                        // the record exists, the ledger is untouched
    }

    /// <summary>A diagnosis linked to a chart finding on tooth 16 O, an accepted plan for it with one filling, completed the old way (which leaves the chart alone).</summary>
    private async Task<(PlanView Plan, Guid Finding)> LegacyToothAsync(string state)
    {
        var diagnosis = await DiagnosisAsync();
        var finding = await FindingAsync("16", "O", state);
        await LinkAsync(diagnosis, finding);
        var plan = await AcceptedToothPlanAsync(diagnosis, ("FILL-1", 140m, "16", "O"));
        await LegacyComplete(plan.Id, plan.Items[0].Id);
        return (await PlanNowAsync(plan.Id), finding);
    }

    [Theory]
    [InlineData(FindingStates.Planned)]
    [InlineData(FindingStates.Diagnosed)]
    public async Task Adopting_a_procedure_moves_its_linked_finding_to_completed_through_the_charts_own_rules(string state)
    {
        var (plan, finding) = await LegacyToothAsync(state);
        Assert.Equal(state, await FindingStateAsync(finding));                                            // the old completion did not touch the chart
        var p = (await Adopt(plan, 0)).Procedures.Single();
        Assert.Equal((ChartOutcomes.Updated, finding), (p.ChartOutcome, p.ToothFindingId));
        Assert.Equal(FindingStates.Completed, await FindingStateAsync(finding));
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.AuditLogEntries.CountAsync(a => a.EventType == "ToothFindingStateChanged" && a.TargetUserAccountId == finding));
    }

    [Fact]
    public async Task A_finding_that_is_already_completed_is_reported_as_such_and_left_alone()
    {
        var (plan, finding) = await LegacyToothAsync(FindingStates.Completed);
        await using var before = Fixture.CreateContext();
        var versions = await before.ToothFindingVersions.CountAsync(v => v.FindingId == finding);
        var p = (await Adopt(plan, 0)).Procedures.Single();
        Assert.Equal((ChartOutcomes.AlreadyCompleted, finding), (p.ChartOutcome, p.ToothFindingId));
        await using var after = Fixture.CreateContext();
        Assert.Equal(versions, await after.ToothFindingVersions.CountAsync(v => v.FindingId == finding));   // nothing was added to the finding's history
    }

    // ---------- what cannot be adopted ----------

    [Theory]
    [InlineData("notCompleted", "not_a_legacy_completion")]
    [InlineData("alreadyHasRecord", "not_a_legacy_completion")]
    [InlineData("alreadyAdopted", "not_a_legacy_completion")]
    [InlineData("withdrawn", "item_withdrawn")]
    [InlineData("unknownItem", "item_not_found")]
    public async Task A_procedure_that_is_not_a_legacy_completion_is_refused_and_nothing_is_recorded(string which, string code)
    {
        var plan = await LegacyAsync(2, 1);                                                               // item 0 accepted only, item 1 completed the old way, item 2 pending
        var target = 0;
        switch (which)
        {
            case "alreadyHasRecord":                                                                      // completed the authoritative way: it has a record, so it is not a legacy completion
                var done = await Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0], AnnEncounter, _provider.Id, "set-1", DateTimeOffset.UtcNow.AddSeconds(2)), S.Actor, default));
                plan = done.Plan;
                break;
            case "alreadyAdopted":
                target = 1;
                plan = (await Adopt(plan, 1)).Plan;
                break;
            case "withdrawn":
                target = 2;
                plan = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[2].Id, "Not needed", plan.RowVersion, S.Actor, default));
                break;
        }
        var before = await CountsAsync();
        var e = which == "unknownItem"
            ? await RefusedAsync(() => Completion(s => s.AdoptAsync(plan.Id, Guid.NewGuid(), new AdoptCompletionRequest("k", plan.RowVersion, AnnEncounter, _provider.Id, DateTimeOffset.UtcNow, Reason), S.Actor, default)))
            : await RefusedAsync(() => Adopt(plan, target, key: "adopt-9"));
        Assert.Equal((code, which == "unknownItem" ? 404 : 409), (e.Code, e.StatusCode));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task An_unknown_plan_is_not_found()
    {
        var plan = await LegacyAsync(1, 0);
        var e = await RefusedAsync(() => Completion(s => s.AdoptAsync(Guid.NewGuid(), plan.Items[0].Id, new AdoptCompletionRequest("k", plan.RowVersion, AnnEncounter, _provider.Id, DateTimeOffset.UtcNow, Reason), S.Actor, default)));
        Assert.Equal(("plan_not_found", 404), (e.Code, e.StatusCode));
    }

    [Fact]
    public async Task Another_patients_encounter_an_unknown_provider_and_a_time_before_the_acceptance_are_each_named_and_nothing_is_recorded()
    {
        var plan = await LegacyAsync(1, 0);
        var before = await CountsAsync();
        var accepted = await AcceptedAtAsync(plan.Items[0].Id);

        var encounter = await RefusedAsync(() => Adopt(plan, 0, encounter: BoEncounter));
        var provider = await RefusedAsync(() => Adopt(plan, 0, provider: Guid.NewGuid()));
        var early = await RefusedAsync(() => Adopt(plan, 0, performed: accepted.AddHours(-1)));
        Assert.Equal(["encounterId"], encounter.FieldErrors.Keys.ToArray());
        Assert.Equal(["providerId"], provider.FieldErrors.Keys.ToArray());
        Assert.Equal(["performedAtUtc"], early.FieldErrors.Keys.ToArray());
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task The_acceptance_in_force_is_compared_even_though_the_procedure_is_already_completed_with_no_exemption_for_an_old_acceptance_time()
    {
        var plan = await LegacyAsync(1, 0);
        var accepted = await AcceptedAtAsync(plan.Items[0].Id);
        var early = await RefusedAsync(() => Adopt(plan, 0, performed: accepted.AddMilliseconds(-5)));
        Assert.Contains("accepted", early.FieldErrors["performedAtUtc"]);
        Assert.Contains("not when the patient may have agreed", early.FieldErrors["performedAtUtc"]);
        var ok = await Adopt(plan, 0, performed: accepted.AddMilliseconds(5));                           // a time at or after the recorded acceptance is accepted
        Assert.Single(ok.Procedures);
    }

    [Fact]
    public async Task A_missing_row_version_a_stale_plan_and_a_missing_reason_are_refused()
    {
        var plan = await LegacyAsync(2, 0, 1);
        Assert.Equal("row_version_required", (await RefusedAsync(() => Completion(s => s.AdoptAsync(plan.Id, plan.Items[0].Id, new AdoptCompletionRequest("k", null, AnnEncounter, _provider.Id, DateTimeOffset.UtcNow, Reason), S.Actor, default)))).Code);
        var noReason = await RefusedAsync(() => Adopt(plan, 0, reason: "   "));
        Assert.Equal(["reason"], noReason.FieldErrors.Keys.ToArray());

        await Adopt(plan, 0);                                                                             // moves the plan's version
        var before = await CountsAsync();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Adopt(plan, 1, key: "adopt-2"));     // plan.RowVersion is now out of date
        Assert.Equal(before, await CountsAsync());
        var fresh = await Adopt(await PlanNowAsync(plan.Id), 1, key: "adopt-2");
        Assert.Single(fresh.Procedures);
    }

    // ---------- retries and the request's identity (review correction C1) ----------

    [Fact]
    public async Task The_same_request_again_replays_the_saved_result_with_no_writes_even_from_an_out_of_date_plan()
    {
        var plan = await LegacyAsync(1, 0);
        var first = await Adopt(plan, 0);
        var before = await CountsAsync();

        var again = await Adopt(plan, 0);                                                                 // the old row version: the double click

        Assert.True(again.Replayed);
        Assert.Equal((first.SetId, first.CorrelationId, Reason), (again.SetId, again.CorrelationId, again.AdoptionReason));
        Assert.Equal(first.Procedures.Single().Id, again.Procedures.Single().Id);
        Assert.Equal(before, await CountsAsync());
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("provider")]
    [InlineData("encounter")]
    [InlineData("time")]
    [InlineData("item")]
    public async Task The_same_key_with_anything_changed_is_a_conflict_and_writes_nothing(string changed)
    {
        var plan = await LegacyAsync(2, 0, 1);
        await Adopt(plan, 0);
        var other = await ProviderAsync("Dr. Other");                                                     // (made before the snapshot: creating them writes audit entries of their own)
        var secondEncounter = await EncounterAsync(Ann);
        var before = await CountsAsync();

        var e = await RefusedAsync(() => changed switch
        {
            "reason" => Adopt(plan, 0, reason: "A different reason"),
            "provider" => Adopt(plan, 0, provider: other.Id),
            "encounter" => Adopt(plan, 0, encounter: secondEncounter),
            "time" => Adopt(plan, 0, performed: _performed!.Value.AddSeconds(1)),
            _ => Adopt(plan, 1),
        });

        Assert.Equal(("idempotency_conflict", 409), (e.Code, e.StatusCode));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task A_key_that_an_ordinary_completion_of_the_same_plan_already_used_is_a_conflict_for_an_adoption_and_the_other_way_round()
    {
        var plan = await LegacyAsync(2, 1);                                                               // item 0 accepted only; item 1 completed the old way
        var done = await Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, [0], AnnEncounter, _provider.Id, "shared-key", DateTimeOffset.UtcNow.AddSeconds(2)), S.Actor, default));
        var before = await CountsAsync();
        var adoption = await RefusedAsync(() => Adopt(done.Plan, 1, key: "shared-key"));                  // the ordinary completion's key
        Assert.Equal(("idempotency_conflict", 409), (adoption.Code, adoption.StatusCode));
        Assert.Equal(before, await CountsAsync());

        var plan2 = await LegacyAsync(2, 1);
        var adopted = await Adopt(plan2, 1, key: "adopt-key");
        var ordinary = await RefusedAsync(() => Completion(s => s.CompleteAsync(plan2.Id, RequestFor(adopted.Plan, [0], AnnEncounter, _provider.Id, "adopt-key", DateTimeOffset.UtcNow.AddSeconds(2)), S.Actor, default)));
        Assert.Equal(("idempotency_conflict", 409), (ordinary.Code, ordinary.StatusCode));              // the adoption's key
    }
}
