using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-015 against real SQL Server: how a plan changes after it is created. A procedure is added with its own save key, a wrong one is withdrawn with a reason and stays on the plan, a plan is
/// renamed or withdrawn; every change is logged with the user and time in the same save (and a failed log or a failed insert stores nothing and says so); repeats are quiet; a stale edit is a conflict.
/// </summary>
public class TreatmentPlanLifecycleTests : TreatmentPlanTestBase
{
    private Task<PlanView> AddAsync(PlanView plan, Guid diagnosis, Guid procedure, string? tooth = null, string? surface = null, string? key = null, string? version = null) =>
        Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, procedure, tooth, surface, key ?? Guid.NewGuid().ToString("N")), version ?? plan.RowVersion, S.Actor, default));

    // ---------- adding a procedure ----------

    [Fact]
    public async Task A_procedure_is_added_with_the_next_number_a_fee_estimate_and_a_new_plan_version()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync(50m);
        var filling = await ProcedureAsync("FILL-1", ProcedureScopes.ToothSurface, ProcedureDentitions.Both, 140m, "Resin filling");

        var more = await AddAsync(plan, diagnosis, filling.Summary.Id, "16", "O");

        Assert.Equal((2, 2, 190m), (more.Items[1].ItemNumber, more.ActiveItemCount, more.EstimateTotal));
        Assert.Equal(("FILL-1", "16", "O", 140m), (more.Items[1].ProcedureCode, more.Items[1].ToothKey, more.Items[1].Surface, more.Items[1].Fee));
        Assert.NotEqual(plan.RowVersion, more.RowVersion);
        Assert.Equal((1, 2, 3, 3), (1, more.Items.Count, (await Svc(s => s.HistoryAsync(plan.Id, default))).Count, (await CountsAsync()).Audit));      // Created, ItemAdded, ItemAdded; and 3 audit entries
    }

    [Fact]
    public async Task Adding_needs_the_plans_current_version_a_stale_one_is_a_conflict_and_changes_nothing()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        var other = await ProcedureAsync("PERIO-2");
        var missing = await Refused(() => Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, other.Summary.Id, key: "k1"), null, S.Actor, default)));
        Assert.Equal(("row_version_required", 400), (missing.Code, missing.StatusCode));

        await Svc(s => s.RenameAsync(plan.Id, "A different title", plan.RowVersion, S.Actor, default));                  // someone else saves first
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => AddAsync(plan, diagnosis, other.Summary.Id));      // plan.RowVersion is now stale
        Assert.Single((await Svc(s => s.GetAsync(plan.Id, default))).Items);
    }

    [Fact]
    public async Task Adding_twice_with_the_same_key_adds_one_item_even_when_the_retry_holds_an_old_version()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        var other = await ProcedureAsync("PERIO-2");
        var first = await AddAsync(plan, diagnosis, other.Summary.Id, key: "add-1");
        var retry = await AddAsync(plan, diagnosis, other.Summary.Id, key: "add-1");                                    // plan.RowVersion is stale by now: the retry is not judged
        Assert.Equal(first.RowVersion, retry.RowVersion);
        Assert.Equal(2, retry.Items.Count);
        Assert.Equal(2, (await CountsAsync()).Items);
    }

    [Fact]
    public async Task A_wrong_added_procedure_is_refused_on_the_named_field_and_a_duplicate_is_refused_until_the_first_is_withdrawn()
    {
        var (plan, diagnosis, procedure) = await SimplePlanAsync();
        var bosDiagnosis = await DiagnosisAsync(Bo);

        var wrong = await Refused(() => AddAsync(plan, bosDiagnosis, Guid.NewGuid()));
        Assert.Contains("not found for this patient", wrong.FieldErrors["diagnosisId"]);
        Assert.Contains("not found in the catalog", wrong.FieldErrors["procedureId"]);

        var duplicate = await Refused(() => AddAsync(plan, diagnosis, procedure.Summary.Id));
        Assert.Contains("already in the plan", duplicate.FieldErrors["procedureId"]);

        var afterWithdraw = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Entered with the wrong tooth", plan.RowVersion, S.Actor, default));
        var again = await AddAsync(afterWithdraw, diagnosis, procedure.Summary.Id);
        Assert.Equal(1, again.ActiveItemCount);
        Assert.Equal(2, again.Items.Count);
    }

    [Fact]
    public async Task A_withdrawn_plan_accepts_no_more_procedures()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        var other = await ProcedureAsync("PERIO-2");
        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Patient moved practice", plan.RowVersion, S.Actor, default));
        var e = await Refused(() => AddAsync(withdrawn, diagnosis, other.Summary.Id));
        Assert.Equal(("plan_withdrawn", 409), (e.Code, e.StatusCode));
    }

    // ---------- withdrawing a procedure ----------

    [Fact]
    public async Task A_procedure_is_withdrawn_with_a_reason_stays_on_the_plan_and_leaves_the_estimate()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync(50m);
        var other = await ProcedureAsync("PERIO-2", fee: 70m);
        var two = await AddAsync(plan, diagnosis, other.Summary.Id);

        var noReason = await Refused(() => Svc(s => s.WithdrawItemAsync(two.Id, two.Items[0].Id, "  ", two.RowVersion, S.Actor, default)));
        Assert.Equal(("reason_required", 400), (noReason.Code, noReason.StatusCode));
        Assert.True(noReason.FieldErrors.ContainsKey("reason"));

        var after = await Svc(s => s.WithdrawItemAsync(two.Id, two.Items[0].Id, "Declined by the clinician", two.RowVersion, Other, default));
        var gone = after.Items[0];
        Assert.Equal((true, "Declined by the clinician", "Hana Hygienist"), (gone.IsWithdrawn, gone.WithdrawnReason, gone.WithdrawnByName));
        Assert.NotNull(gone.WithdrawnAtUtc);
        Assert.Equal((1, 70m, 2), (after.ActiveItemCount, after.EstimateTotal, after.Items.Count));                      // still listed, no longer counted
    }

    [Fact]
    public async Task Withdrawing_something_already_withdrawn_changes_nothing_and_an_unknown_item_is_a_404()
    {
        var (plan, _, _) = await SimplePlanAsync();
        var once = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Wrong tooth", plan.RowVersion, S.Actor, default));
        var before = await CountsAsync();
        var twice = await Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Different words", plan.RowVersion, S.Actor, default));   // plan.RowVersion is stale; the repeat is quiet
        Assert.Equal((once.RowVersion, "Wrong tooth"), (twice.RowVersion, twice.Items[0].WithdrawnReason));
        Assert.Equal(before, await CountsAsync());

        var (other, _, _) = await SimplePlanAsync();
        var unknown = await Refused(() => Svc(s => s.WithdrawItemAsync(plan.Id, Guid.NewGuid(), "x", plan.RowVersion, S.Actor, default)));
        var foreign = await Refused(() => Svc(s => s.WithdrawItemAsync(plan.Id, other.Items[0].Id, "x", plan.RowVersion, S.Actor, default)));   // another plan's item
        Assert.Equal(("item_not_found", 404), (unknown.Code, unknown.StatusCode));
        Assert.Equal("item_not_found", foreign.Code);
    }

    [Fact]
    public async Task Withdrawing_from_a_stale_copy_is_a_conflict_and_changes_nothing()
    {
        var (plan, _, _) = await SimplePlanAsync();
        await Svc(s => s.RenameAsync(plan.Id, "Renamed first", plan.RowVersion, S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Wrong tooth", plan.RowVersion, S.Actor, default)));
        Assert.False((await Svc(s => s.GetAsync(plan.Id, default))).Items[0].IsWithdrawn);
    }

    // ---------- renaming and withdrawing a plan ----------

    [Fact]
    public async Task A_plan_is_renamed_and_saving_the_same_title_changes_nothing()
    {
        var (plan, _, _) = await SimplePlanAsync();
        var renamed = await Svc(s => s.RenameAsync(plan.Id, "  Full mouth plan  ", plan.RowVersion, S.Actor, default));
        Assert.Equal("Full mouth plan", renamed.Title);
        var before = await CountsAsync();
        var same = await Svc(s => s.RenameAsync(plan.Id, "Full mouth plan", renamed.RowVersion, S.Actor, default));
        Assert.Equal((renamed.RowVersion, before), (same.RowVersion, await CountsAsync()));
        Assert.Contains("title is required", (await Refused(() => Svc(s => s.RenameAsync(plan.Id, "  ", renamed.RowVersion, S.Actor, default)))).FieldErrors["title"]);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.RenameAsync(plan.Id, "Another", plan.RowVersion, S.Actor, default)));
    }

    [Fact]
    public async Task A_withdrawn_plan_is_final_stays_readable_and_is_hidden_from_the_list_unless_asked_for()
    {
        var (plan, _, _) = await SimplePlanAsync();
        Assert.Equal("reason_required", (await Refused(() => Svc(s => s.WithdrawAsync(plan.Id, null, plan.RowVersion, S.Actor, default)))).Code);

        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Treatment not going ahead", plan.RowVersion, S.Actor, default));
        Assert.Equal((TreatmentPlanStatuses.Withdrawn, "Treatment not going ahead", "Dr. Okafor"), (withdrawn.Status, withdrawn.WithdrawnReason, withdrawn.WithdrawnByName));
        Assert.Single(withdrawn.Items);                                                                                  // the items and their fees stay readable

        var before = await CountsAsync();
        var again = await Svc(s => s.WithdrawAsync(plan.Id, "Other words", plan.RowVersion, S.Actor, default));         // quiet, even from a stale copy
        Assert.Equal(("Treatment not going ahead", before), (again.WithdrawnReason, await CountsAsync()));
        Assert.Equal("plan_withdrawn", (await Refused(() => Svc(s => s.RenameAsync(plan.Id, "New name", withdrawn.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("plan_withdrawn", (await Refused(() => Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "x", withdrawn.RowVersion, S.Actor, default)))).Code);

        Assert.Empty(await Svc(s => s.ListAsync(Ann, false, default)));
        Assert.Single(await Svc(s => s.ListAsync(Ann, true, default)));
    }

    // ---------- acceptance 3: every entry is logged with the user and the time ----------

    [Fact]
    public async Task Every_change_is_audited_with_the_user_and_time_and_recorded_in_the_history_without_clinical_wording()
    {
        var (plan, diagnosis, procedure) = await SimplePlanAsync(50m);
        var other = await ProcedureAsync("PERIO-2", fee: 70m);
        var two = await AddAsync(plan, diagnosis, other.Summary.Id);
        var renamed = await Svc(s => s.RenameAsync(plan.Id, "Revised plan", two.RowVersion, S.Actor, default));
        var item = await Svc(s => s.WithdrawItemAsync(plan.Id, two.Items[0].Id, "Wrong tooth", renamed.RowVersion, Other, default));
        await Svc(s => s.WithdrawAsync(plan.Id, "Treatment not going ahead", item.RowVersion, S.Actor, default));

        var history = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, history.Select(h => h.EventNumber));
        Assert.Equal(new[] { "Created", "ItemAdded", "ItemAdded", "Renamed", "ItemWithdrawn", "Withdrawn" }, history.Select(h => h.ChangeType));
        Assert.Equal(new[] { "Dr. Okafor", "Dr. Okafor", "Dr. Okafor", "Dr. Okafor", "Hana Hygienist", "Dr. Okafor" }, history.Select(h => h.ActorName));
        Assert.Equal("Wrong tooth", history[4].Reason);
        Assert.Equal(("Periodontal plan", "Revised plan"), (history[0].Title, history[3].Title));
        Assert.All(history, h => Assert.NotEqual(default, h.OccurredAtUtc));

        var audit = await AuditAsync(nameof(TreatmentPlan));
        Assert.Equal(new[] { "TreatmentPlanCreated", "TreatmentPlanItemAdded", "TreatmentPlanItemAdded", "TreatmentPlanRenamed", "TreatmentPlanItemWithdrawn", "TreatmentPlanWithdrawn" }, audit.Select(a => a.Type));
        Assert.Equal(new Guid?[] { S.Actor, S.Actor, S.Actor, S.Actor, Other, S.Actor }, audit.Select(a => a.By));
        Assert.All(audit, a => Assert.DoesNotContain("Chronic periodontitis", a.Details));                                // never the diagnosis
        Assert.All(audit, a => Assert.DoesNotContain("Periodontal plan", a.Details));                                     // nor the title
        await using var db = Fixture.CreateContext();
        Assert.All(await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == nameof(TreatmentPlan)).ToListAsync(), a => Assert.NotEqual(default, a.TimestampUtc));
    }

    // ---------- failure path: "system fails to log treatment plan entries" ----------

    [Fact]
    public async Task A_failed_audit_write_saves_nothing_and_says_so_and_the_same_save_works_once_the_log_is_back()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefusePlanAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EntityType = 'TreatmentPlan') THROW 59010, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var e = await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "visit-1"));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message);                                                                       // no internals leak to the person
        Assert.Equal((0, 0, 0, 0), await CountsAsync());

        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefusePlanAudit]");
        await CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "visit-1");
        Assert.Equal((1, 1, 2, 2), await CountsAsync());
    }

    [Fact]
    public async Task A_failed_audit_write_on_any_later_change_changes_nothing()
    {
        var (plan, diagnosis, _) = await SimplePlanAsync();
        var other = await ProcedureAsync("PERIO-2");
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseChangeAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType IN ('TreatmentPlanItemAdded', 'TreatmentPlanItemWithdrawn', 'TreatmentPlanRenamed', 'TreatmentPlanWithdrawn')) THROW 59011, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var before = await CountsAsync();
        Assert.Equal("save_failed", (await Refused(() => AddAsync(plan, diagnosis, other.Summary.Id))).Code);
        Assert.Equal("save_failed", (await Refused(() => Svc(s => s.WithdrawItemAsync(plan.Id, plan.Items[0].Id, "Wrong tooth", plan.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("save_failed", (await Refused(() => Svc(s => s.RenameAsync(plan.Id, "Another title", plan.RowVersion, S.Actor, default)))).Code);
        Assert.Equal("save_failed", (await Refused(() => Svc(s => s.WithdrawAsync(plan.Id, "Not going ahead", plan.RowVersion, S.Actor, default)))).Code);

        var after = await Svc(s => s.GetAsync(plan.Id, default));
        Assert.Equal((plan.Title, TreatmentPlanStatuses.Proposed, plan.RowVersion, 1, false), (after.Title, after.Status, after.RowVersion, after.Items.Count, after.Items[0].IsWithdrawn));
        Assert.Equal(before, await CountsAsync());
    }

    [Fact]
    public async Task A_failed_insert_halfway_through_leaves_no_half_saved_plan()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseEvent] ON [TreatmentPlanEvents] AFTER INSERT AS
BEGIN
    THROW 59012, 'storage unavailable', 1;
END");
        Assert.Equal("save_failed", (await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)]))).Code);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());                                                                  // the plan and item inserted before the event were rolled back
    }

    // ---------- reading ----------

    [Fact]
    public async Task A_patients_plans_are_listed_newest_first_and_never_include_another_patients()
    {
        var (first, _, _) = await SimplePlanAsync();
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync("PERIO-9");
        var second = await CreateAsync([Item(diagnosis, procedure.Summary.Id)], "Second plan");
        var boDiagnosis = await DiagnosisAsync(Bo);
        await CreateAsync([Item(boDiagnosis, procedure.Summary.Id)], "Bo's plan", patient: Bo);

        var list = await Svc(s => s.ListAsync(Ann, false, default));
        Assert.Equal(new[] { second.Id, first.Id }, list.Select(p => p.Id));
        Assert.DoesNotContain(list, p => p.Title == "Bo's plan");
        Assert.Equal(("Bo's plan", Bo), ((await Svc(s => s.ListAsync(Bo, false, default))).Single().Title, Bo));
    }

    [Fact]
    public async Task An_unknown_patient_or_plan_is_a_404()
    {
        Assert.Equal("patient_not_found", (await Refused(() => Svc(s => s.ListAsync(Guid.NewGuid(), false, default)))).Code);
        Assert.Equal("plan_not_found", (await Refused(() => Svc(s => s.GetAsync(Guid.NewGuid(), default)))).Code);
        Assert.Equal("plan_not_found", (await Refused(() => Svc(s => s.HistoryAsync(Guid.NewGuid(), default)))).Code);
        Assert.Equal("plan_not_found", (await Refused(() => Svc(s => s.RenameAsync(Guid.NewGuid(), "x", "AAAA", S.Actor, default)))).Code);
    }

}
