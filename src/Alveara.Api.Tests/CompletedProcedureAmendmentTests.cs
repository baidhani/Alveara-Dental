using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01 against real SQL Server: correcting a completed procedure. The original row never changes; a correction is a numbered, append-only amendment of the provider, performed time or encounter with a
/// reason, made against the amendment the caller last saw (a stale one is a conflict, two racing for one number leave one winner), recognised on retry by its own key and canonical request, judged by the same
/// time rules as a completion, and it never touches the plan's state, the chart or the charge. The read side shows the current values and every correction.
/// </summary>
public class CompletedProcedureAmendmentTests : CompletionTestBase
{
    private ProviderProfile _provider = null!;
    private ProviderProfile _other = null!;
    private DateTimeOffset? _performed;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        _provider = await ProviderAsync();
        _other = await ProviderAsync("Dr. Other");
    }

    private async Task<(PlanView Plan, CompletionResult Result)> CompletedAsync(int count = 2)
    {
        var plan = await AcceptedPlanAsync(count);
        _performed ??= DateTimeOffset.UtcNow.AddSeconds(2);
        var result = await Completion(s => s.CompleteAsync(plan.Id, RequestFor(plan, Enumerable.Range(0, count), AnnEncounter, _provider.Id, "set-1", _performed), S.Actor, default));
        return (plan, result);
    }

    private Task<AmendmentResult> AmendAsync(Guid procedure, int expected, string key = "amend-1", Guid? provider = null, DateTimeOffset? performed = null, Guid? encounter = null, string? reason = "Recorded under the wrong provider") =>
        Completion(s => s.AmendAsync(procedure, new AmendCompletedProcedureRequest(key, expected, provider, performed, encounter, reason), S.Actor, default));

    private async Task<(int Amendments, int Audit)> AmendmentCountsAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.CompletedProcedureAmendments.CountAsync(), await db.AuditLogEntries.CountAsync(a => a.EventType == "CompletedProcedureAmended"));
    }

    private static async Task<TreatmentPlanException> RefusedAsync(Func<Task<AmendmentResult>> action) => await Assert.ThrowsAsync<TreatmentPlanException>(action);

    // ---------- the correction ----------

    [Fact]
    public async Task A_correction_changes_the_current_provider_keeps_the_original_and_touches_nothing_else()
    {
        var (plan, done) = await CompletedAsync();
        var procedure = done.Procedures[0];
        var before = await SnapshotAsync(plan.Id);
        await using var originalBefore = Fixture.CreateContext();
        var rowBefore = await originalBefore.CompletedProcedures.AsNoTracking().SingleAsync(c => c.Id == procedure.Id);

        var result = await AmendAsync(procedure.Id, 0, provider: _other.Id);

        Assert.False(result.Replayed);
        Assert.Equal(1, result.AmendmentNumber);
        var detail = result.Procedure;
        Assert.Equal((_other.Id, _provider.Id), (detail.ProviderProfileId, detail.OriginalProviderProfileId));
        Assert.Equal((procedure.PerformedAtUtc, AnnEncounter), (detail.PerformedAtUtc, detail.EncounterId));                 // what was not corrected is unchanged
        var amendment = Assert.Single(detail.Amendments);
        Assert.Equal((1, "Recorded under the wrong provider", _other.Id), (amendment.Number, amendment.Reason, amendment.ProviderProfileId));
        Assert.Equal(1, detail.LatestAmendmentNumber);

        var after = await SnapshotAsync(plan.Id);
        Assert.Equal(before with { Audit = before.Audit + 1 }, after);                                                         // the plan, its states and history, the charges and the chart are exactly as they were
        await using var db = Fixture.CreateContext();
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(rowBefore), System.Text.Json.JsonSerializer.Serialize(await db.CompletedProcedures.AsNoTracking().SingleAsync(c => c.Id == procedure.Id)));            // the original record is unchanged, field for field
        Assert.Equal((1, 1), await AmendmentCountsAsync());
    }

    [Fact]
    public async Task Successive_corrections_are_numbered_and_each_field_shows_its_latest_value_with_the_originals_kept()
    {
        var (_, done) = await CompletedAsync();
        var procedure = done.Procedures[0];
        var later = _performed!.Value.AddMinutes(1);

        await AmendAsync(procedure.Id, 0, "amend-1", provider: _other.Id);
        var second = await AmendAsync(procedure.Id, 1, "amend-2", performed: later, encounter: AnnEncounter, reason: "Entered at the wrong time");

        Assert.Equal(2, second.AmendmentNumber);
        var detail = second.Procedure;
        Assert.Equal((_other.Id, CompletionRules.NormalizeTime(later)), (detail.ProviderProfileId, detail.PerformedAtUtc));       // provider from the first correction, time from the second
        Assert.Equal((_provider.Id, CompletionRules.NormalizeTime(_performed.Value)), (detail.OriginalProviderProfileId, detail.OriginalPerformedAtUtc));
        Assert.Equal([1, 2], detail.Amendments.Select(a => a.Number).ToArray());
        Assert.Equal([(Guid?)_other.Id, null], detail.Amendments.Select(a => a.ProviderProfileId).ToArray());                   // each shows only what it corrected
        Assert.False(string.IsNullOrWhiteSpace(detail.Amendments[0].AmendedByName));
    }

    // ---------- retries and stale corrections ----------

    [Fact]
    public async Task The_same_key_and_correction_replays_the_original_amendment_and_records_nothing()
    {
        var (_, done) = await CompletedAsync();
        var first = await AmendAsync(done.Procedures[0].Id, 0, provider: _other.Id);
        var counts = await AmendmentCountsAsync();

        var again = await AmendAsync(done.Procedures[0].Id, 0, provider: _other.Id);                          // the same request, still holding the amendment number it had

        Assert.True(again.Replayed);
        Assert.Equal(first.AmendmentNumber, again.AmendmentNumber);
        Assert.Equal(counts, await AmendmentCountsAsync());
    }

    [Fact]
    public async Task The_same_key_with_a_different_correction_is_a_conflict_and_records_nothing()
    {
        var (_, done) = await CompletedAsync();
        await AmendAsync(done.Procedures[0].Id, 0, provider: _other.Id);
        var counts = await AmendmentCountsAsync();

        var e = await RefusedAsync(() => AmendAsync(done.Procedures[0].Id, 0, performed: _performed!.Value.AddMinutes(1)));
        Assert.Equal(("idempotency_conflict", 409), (e.Code, e.StatusCode));
        var otherReason = await RefusedAsync(() => AmendAsync(done.Procedures[0].Id, 0, provider: _other.Id, reason: "A different reason"));
        Assert.Equal("idempotency_conflict", otherReason.Code);
        Assert.Equal(counts, await AmendmentCountsAsync());
    }

    [Fact]
    public async Task A_correction_made_against_an_older_amendment_is_a_conflict_until_the_caller_looks_again()
    {
        var (_, done) = await CompletedAsync();
        await AmendAsync(done.Procedures[0].Id, 0, "amend-1", provider: _other.Id);
        var counts = await AmendmentCountsAsync();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AmendAsync(done.Procedures[0].Id, 0, "amend-2", performed: _performed!.Value.AddMinutes(1)));
        Assert.Equal(counts, await AmendmentCountsAsync());

        var fresh = await AmendAsync(done.Procedures[0].Id, 1, "amend-2", performed: _performed!.Value.AddMinutes(1));
        Assert.Equal(2, fresh.AmendmentNumber);
    }

    [Fact]
    public async Task Two_corrections_made_at_once_against_the_same_amendment_leave_exactly_one_winner()
    {
        var (_, done) = await CompletedAsync();
        var id = done.Procedures[0].Id;
        var third = await ProviderAsync("Dr. Third");

        var outcomes = await Task.WhenAll(
            AmendAsync(id, 0, "race-a", provider: _other.Id).ContinueWith(t => t.Exception?.GetBaseException()),
            AmendAsync(id, 0, "race-b", provider: third.Id).ContinueWith(t => t.Exception?.GetBaseException()));

        Assert.Single(outcomes, ex => ex is null);
        var loser = Assert.Single(outcomes, ex => ex is not null)!;
        Assert.IsType<ConcurrencyConflictException>(loser);
        Assert.Equal((1, 1), await AmendmentCountsAsync());
    }

    // ---------- what a correction must be ----------

    [Fact]
    public async Task Every_missing_or_wrong_field_of_a_correction_is_named_and_nothing_is_recorded()
    {
        var (_, done) = await CompletedAsync();
        var id = done.Procedures[0].Id;
        var empty = await RefusedAsync(() => Completion(s => s.AmendAsync(id, new AmendCompletedProcedureRequest(null, null, null, null, null, null), S.Actor, default)));
        Assert.Equal(["correction", "expectedAmendmentNumber", "idempotencyKey", "reason"], empty.FieldErrors.Keys.Order().ToArray());

        var tooFar = await RefusedAsync(() => AmendAsync(id, 0, performed: DateTimeOffset.UtcNow.AddMinutes(30)));
        Assert.Equal(["performedAtUtc"], tooFar.FieldErrors.Keys.ToArray());
        var blank = await RefusedAsync(() => AmendAsync(id, 0, provider: _other.Id, reason: "   "));
        Assert.Equal(["reason"], blank.FieldErrors.Keys.ToArray());
        var unknownProvider = await RefusedAsync(() => AmendAsync(id, 0, provider: Guid.NewGuid()));
        Assert.Equal(["providerId"], unknownProvider.FieldErrors.Keys.ToArray());
        var otherPatient = await RefusedAsync(() => AmendAsync(id, 0, encounter: BoEncounter));
        Assert.Equal(["encounterId"], otherPatient.FieldErrors.Keys.ToArray());
        Assert.Equal((0, 0), await AmendmentCountsAsync());
    }

    [Fact]
    public async Task A_correction_that_changes_nothing_is_refused()
    {
        var (_, done) = await CompletedAsync();
        var id = done.Procedures[0].Id;
        var same = await RefusedAsync(() => AmendAsync(id, 0, provider: _provider.Id, performed: _performed, encounter: AnnEncounter));
        Assert.Equal(["correction"], same.FieldErrors.Keys.ToArray());
        await AmendAsync(id, 0, "amend-1", provider: _other.Id);
        var again = await RefusedAsync(() => AmendAsync(id, 1, "amend-2", provider: _other.Id));                               // already the current value
        Assert.Equal(["correction"], again.FieldErrors.Keys.ToArray());
        Assert.Equal((1, 1), await AmendmentCountsAsync());
    }

    [Fact]
    public async Task A_corrected_time_is_judged_by_the_same_rules_as_a_completion()
    {
        var (plan, done) = await CompletedAsync();
        var id = done.Procedures[0].Id;
        var beforePlan = await RefusedAsync(() => AmendAsync(id, 0, "a1", performed: plan.CreatedAtUtc.AddMinutes(-1)));
        Assert.Contains("before this plan was created", beforePlan.FieldErrors["performedAtUtc"]);
        var accepted = await AcceptedAtAsync(plan.Items[0].Id);
        var beforeAcceptance = await RefusedAsync(() => AmendAsync(id, 0, "a2", performed: accepted.AddMilliseconds(-1)));
        Assert.Contains("the time the acceptance was recorded in this plan, not when the patient may have agreed", beforeAcceptance.FieldErrors["performedAtUtc"]);
        Assert.Equal((0, 0), await AmendmentCountsAsync());

        var ok = await AmendAsync(id, 0, "a3", performed: accepted.AddMilliseconds(1));
        Assert.Equal(1, ok.AmendmentNumber);
    }

    [Fact]
    public async Task A_correction_can_name_a_finalized_encounter_and_does_not_change_it()
    {
        var (_, done) = await CompletedAsync();
        var second = await EncounterAsync(Ann);
        await ExecuteAsync("UPDATE [Encounters] SET [Status] = 'Finalized', [FinalizedAtUtc] = SYSDATETIMEOFFSET(), [FinalizedByUserId] = {0} WHERE [Id] = {1}", S.Actor, second);
        await using var before = Fixture.CreateContext();
        var rowBefore = await before.Encounters.AsNoTracking().SingleAsync(e => e.Id == second);

        var result = await AmendAsync(done.Procedures[0].Id, 0, encounter: second, reason: "Belongs to the later visit");

        Assert.Equal(second, result.Procedure.EncounterId);
        await using var db = Fixture.CreateContext();
        Assert.Equal(rowBefore.RowVersion, (await db.Encounters.AsNoTracking().SingleAsync(e => e.Id == second)).RowVersion);
    }

    [Fact]
    public async Task An_unknown_completed_procedure_is_not_found()
    {
        var e = await RefusedAsync(() => AmendAsync(Guid.NewGuid(), 0, provider: _other.Id));
        Assert.Equal(("completed_procedure_not_found", 404), (e.Code, e.StatusCode));
    }

    // ---------- failure: the log is unavailable ----------

    [Fact]
    public async Task A_failed_audit_write_records_no_amendment_and_says_so_and_the_same_correction_works_once_the_log_is_back()
    {
        var (plan, done) = await CompletedAsync();
        var before = await SnapshotAsync(plan.Id);
        await ExecuteAsync(@"CREATE TRIGGER [TR_Test_RefuseAmendAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'CompletedProcedureAmended') THROW 59043, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");

        var e = await RefusedAsync(() => AmendAsync(done.Procedures[0].Id, 0, provider: _other.Id));

        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await SnapshotAsync(plan.Id));
        Assert.Equal((0, 0), await AmendmentCountsAsync());

        await ExecuteAsync("DROP TRIGGER [TR_Test_RefuseAmendAudit]");
        Assert.Equal(1, (await AmendAsync(done.Procedures[0].Id, 0, provider: _other.Id)).AmendmentNumber);
    }

    // ---------- reading ----------

    [Fact]
    public async Task A_patients_completed_procedures_list_newest_performed_first_with_their_current_values_charge_and_chart()
    {
        var (_, done) = await CompletedAsync(2);
        var first = done.Procedures[0];
        await AmendAsync(first.Id, 0, performed: _performed!.Value.AddMinutes(2), reason: "Entered at the wrong time");      // procedure 1 is now the later one (and still inside the clock tolerance)

        var list = await Completion(s => s.ListAsync(Ann, default));

        Assert.Equal([1, 2], list.Select(d => d.ItemNumber).ToArray());
        Assert.Equal(CompletionRules.NormalizeTime(_performed.Value.AddMinutes(2)), list[0].PerformedAtUtc);
        Assert.All(list, d => Assert.Equal((ChargeOutcomes.Created, 100m, ChartOutcomes.NotApplicable, "Periodontal plan"), (d.Charge!.Outcome, d.Charge.Amount, d.ChartOutcome, d.PlanTitle)));
        Assert.All(list, d => Assert.False(string.IsNullOrWhiteSpace(d.ProviderName)));
        Assert.All(list, d => Assert.Equal(AnnEncounter, d.EncounterId));
        Assert.Empty(await Completion(s => s.ListAsync(Bo, default)));

        var detail = await Completion(s => s.DetailAsync(first.Id, default));
        Assert.Equal(1, detail.LatestAmendmentNumber);
        var gone = await Assert.ThrowsAsync<TreatmentPlanException>(() => Completion(s => s.ListAsync(Guid.NewGuid(), default)));
        Assert.Equal(404, gone.StatusCode);
    }
}
