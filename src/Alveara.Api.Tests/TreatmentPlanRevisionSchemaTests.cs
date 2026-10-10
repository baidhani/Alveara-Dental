using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 against real SQL Server: the database refuses, on its own, what the service will refuse and what no service rule can see. These tests go around the service on purpose - a script, a future
/// module or a person with database access cannot edit or delete a revision, a phase, a procedure's acceptance history or a plan resolution; cannot record a state for a withdrawn procedure or plan, or
/// after Completed; cannot change a reference code once set or move a revision number back; cannot resolve a diagnosis to another patient's plan or with different text; and cannot save a revision, phase or
/// state that points at another plan or patient.
/// </summary>
public class TreatmentPlanRevisionSchemaTests : TreatmentPlanTestBase
{
    private static async Task<SqlException> Sql(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task<T> InsertAsync<T>(T row) where T : class
    {
        await using var db = Fixture.CreateContext();
        db.Add(row);
        await db.SaveChangesAsync();
        return row;
    }

    private async Task<int> RunAsync(string sql, params object[] args)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    private Task<TreatmentPlan> PlanAsync(Guid? patient = null, string key = "plan-1", Action<TreatmentPlan>? tweak = null)
    {
        var plan = new TreatmentPlan { Id = Guid.NewGuid(), PatientId = patient ?? Ann, IdempotencyKey = key, Title = "Plan", Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };
        tweak?.Invoke(plan);
        return InsertAsync(plan);
    }

    private async Task<TreatmentPlanItem> ItemAsync(TreatmentPlan plan, Guid diagnosis, ProcedureDetailView procedure, int number = 1, bool withdrawn = false)
    {
        var item = new TreatmentPlanItem
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, ItemNumber = number, IdempotencyKey = $"item-{number}", DiagnosisId = diagnosis, ProcedureId = procedure.Summary.Id,
            ProcedureVersionId = procedure.Versions[0].VersionId, Fee = procedure.Versions[0].Fee, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor,
        };
        if (withdrawn) (item.WithdrawnAtUtc, item.WithdrawnByUserId, item.WithdrawnReason) = (DateTimeOffset.UtcNow, S.Actor, "Declined");
        return await InsertAsync(item);
    }

    private static TreatmentPlanRevision Revision(TreatmentPlan plan, int number = 1, Guid? patient = null, string title = "Plan") =>
        new() { Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = patient ?? plan.PatientId, RevisionNumber = number, ChangeType = "Revised", EventNumber = number, Title = title, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = Guid.NewGuid() };

    private static TreatmentPlanItemState State(TreatmentPlanItem item, int sequence = 1, string acceptance = "Accepted", string scheduling = "Unscheduled", string completion = "NotStarted", Guid? plan = null) =>
        new() { Id = Guid.NewGuid(), PlanId = plan ?? item.PlanId, PatientId = item.PatientId, ItemId = item.Id, Sequence = sequence, Acceptance = acceptance, Scheduling = scheduling, Completion = completion, RevisionNumber = 1, RecordedByUserId = Guid.NewGuid(), RecordedAtUtc = DateTimeOffset.UtcNow };

    private async Task<Guid> DiagnosisWithReferenceAsync(Guid patient, string? reference)
    {
        var encounter = patient == Bo ? BoEncounter : AnnEncounter;
        var d = await WithDb(db => new DiagnosisService(db, Clock).RecordAsync(patient, Guid.NewGuid().ToString("N"), new DiagnosisInput(encounter, "Chronic periodontitis", null, null, reference), S.Actor, default));
        return d.Id;
    }

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item)> ArrangedAsync(bool withdrawnItem = false)
    {
        var plan = await PlanAsync();
        var item = await ItemAsync(plan, await DiagnosisAsync(), await ProcedureAsync(), withdrawn: withdrawnItem);
        return (plan, item);
    }

    // ---------- a new plan starts at revision 1 with no reference code ----------

    [Fact]
    public async Task A_new_plan_starts_at_revision_one_with_no_reference_code_and_two_plans_may_both_have_none()
    {
        var a = await PlanAsync(key: "a");
        await PlanAsync(key: "b");
        await using var db = Fixture.CreateContext();
        var saved = await db.TreatmentPlans.AsNoTracking().SingleAsync(p => p.Id == a.Id);
        Assert.Equal((1, null), (saved.CurrentRevision, saved.ReferenceCode));
    }

    [Theory]
    [InlineData("blank", "CK_TreatmentPlans_ReferenceCode")]
    [InlineData("control", "CK_TreatmentPlans_ReferenceCode")]
    public async Task The_database_refuses_a_blank_or_control_character_reference_code(string which, string constraint)
    {
        var e = await Sql(() => PlanAsync(tweak: p => p.ReferenceCode = which == "blank" ? "   " : "A\u0007B"));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_reference_code_names_at_most_one_plan_of_a_patient_but_another_patient_may_use_the_same_code()
    {
        await PlanAsync(key: "a", tweak: p => p.ReferenceCode = "PLAN-7");
        Assert.Contains((await Sql(() => PlanAsync(key: "b", tweak: p => p.ReferenceCode = "PLAN-7"))).Number, new[] { 2601, 2627 });
        await PlanAsync(patient: Bo, key: "c", tweak: p => p.ReferenceCode = "PLAN-7");
    }

    [Fact]
    public async Task A_reference_code_is_set_once_and_never_changed_or_cleared_and_the_revision_number_never_goes_down()
    {
        var plan = await PlanAsync();
        Assert.Equal(1, await RunAsync("UPDATE [TreatmentPlans] SET [ReferenceCode] = {0} WHERE [Id] = {1}", "PLAN-7", plan.Id));      // from none to a code is allowed
        Assert.Equal(51099, (await Sql(() => RunAsync("UPDATE [TreatmentPlans] SET [ReferenceCode] = {0} WHERE [Id] = {1}", "PLAN-8", plan.Id))).Number);
        Assert.Equal(51099, (await Sql(() => RunAsync("UPDATE [TreatmentPlans] SET [ReferenceCode] = {0} WHERE [Id] = {1}", "plan-7", plan.Id))).Number);   // not even a change of case
        Assert.Equal(51099, (await Sql(() => RunAsync("UPDATE [TreatmentPlans] SET [ReferenceCode] = NULL WHERE [Id] = {0}", plan.Id))).Number);
        Assert.Equal(1, await RunAsync("UPDATE [TreatmentPlans] SET [CurrentRevision] = 3 WHERE [Id] = {0}", plan.Id));
        Assert.Equal(51100, (await Sql(() => RunAsync("UPDATE [TreatmentPlans] SET [CurrentRevision] = 2 WHERE [Id] = {0}", plan.Id))).Number);
        Assert.Equal(547, (await Sql(() => RunAsync("UPDATE [TreatmentPlans] SET [CurrentRevision] = 0 WHERE [Id] = {0}", plan.Id))).Number);
    }

    // ---------- the history accepts the new kinds of entry, with the rules for each ----------

    private TreatmentPlanEvent Event(TreatmentPlan plan, string type, Guid? item = null, string? reason = null, int number = 1, int? revision = null) =>
        new() { Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, EventNumber = number, ChangeType = type, ItemId = item, Reason = reason, RevisionNumber = revision, ActorUserId = S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow };

    [Theory]
    [InlineData("Revised", false, "why", null)]
    [InlineData("PlanAccepted", false, null, null)]
    [InlineData("PlanDeclined", false, "No funds", null)]
    [InlineData("ReferenceCodeSet", false, null, null)]
    [InlineData("ItemAccepted", true, null, 2)]
    [InlineData("ItemDeclined", true, "Not now", 2)]
    [InlineData("ItemScheduled", true, null, null)]
    [InlineData("ItemUnscheduled", true, null, null)]
    [InlineData("ItemCompleted", true, null, null)]
    public async Task The_history_accepts_each_new_kind_of_entry(string type, bool needsItem, string? reason, int? revision)
    {
        var (plan, item) = await ArrangedAsync();
        await InsertAsync(Event(plan, type, needsItem ? item.Id : null, reason, revision: revision));
    }

    [Theory]
    [InlineData("Frobnicated", false, "x", "CK_TreatmentPlanEvents_ChangeType")]
    [InlineData("ItemAccepted", false, null, "CK_TreatmentPlanEvents_Item")]
    [InlineData("PlanAccepted", true, null, "CK_TreatmentPlanEvents_Item")]
    [InlineData("ItemDeclined", true, "  ", "CK_TreatmentPlanEvents_Reason")]
    [InlineData("PlanDeclined", false, null, "CK_TreatmentPlanEvents_Reason")]
    [InlineData("Revised", false, null, "CK_TreatmentPlanEvents_Reason")]
    public async Task The_history_refuses_an_unknown_kind_an_item_entry_without_its_item_and_a_missing_reason(string type, bool withItem, string? reason, string constraint)
    {
        var (plan, item) = await ArrangedAsync();
        var e = await Sql(() => InsertAsync(Event(plan, type, withItem ? item.Id : null, reason)));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_history_entry_names_a_real_revision_number_or_none()
    {
        var (plan, _) = await ArrangedAsync();
        var e = await Sql(() => InsertAsync(Event(plan, "PlanAccepted", revision: 0)));
        Assert.Equal(547, e.Number);
        Assert.Contains("CK_TreatmentPlanEvents_RevisionNumber", e.Message);
    }

    // ---------- revisions, phases and the procedures in them ----------

    [Theory]
    [InlineData("number", "CK_TreatmentPlanRevisions_Number")]
    [InlineData("changeType", "CK_TreatmentPlanRevisions_ChangeType")]
    [InlineData("blankTitle", "CK_TreatmentPlanRevisions_Title")]
    [InlineData("controlTitle", "CK_TreatmentPlanRevisions_Title")]
    [InlineData("blankRationale", "CK_TreatmentPlanRevisions_Text")]
    [InlineData("blankExplanation", "CK_TreatmentPlanRevisions_Text")]
    [InlineData("blankReason", "CK_TreatmentPlanRevisions_Text")]
    public async Task The_database_refuses_a_revision_that_breaks_a_shape_rule(string which, string constraint)
    {
        var plan = await PlanAsync();
        var r = Revision(plan);
        switch (which)
        {
            case "number": r.RevisionNumber = 0; break;
            case "changeType": r.ChangeType = "Accepted"; break;
            case "blankTitle": r.Title = "  "; break;
            case "controlTitle": r.Title = "A\u0007B"; break;
            case "blankRationale": r.ClinicalRationale = " "; break;
            case "blankExplanation": r.PatientExplanation = " "; break;
            case "blankReason": r.Reason = " "; break;
        }
        var e = await Sql(() => InsertAsync(r));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_revision_number_and_an_event_number_are_each_used_once_per_plan()
    {
        var plan = await PlanAsync();
        await InsertAsync(Revision(plan));
        var sameNumber = Revision(plan); sameNumber.EventNumber = 9;
        var sameEvent = Revision(plan, number: 2); sameEvent.EventNumber = 1;
        Assert.Contains((await Sql(() => InsertAsync(sameNumber))).Number, new[] { 2601, 2627 });
        Assert.Contains((await Sql(() => InsertAsync(sameEvent))).Number, new[] { 2601, 2627 });
        await InsertAsync(Revision(plan, number: 2));
    }

    [Fact]
    public async Task A_revision_must_carry_its_plans_patient()
    {
        var plan = await PlanAsync();
        Assert.Equal(51110, (await Sql(() => InsertAsync(Revision(plan, patient: Bo)))).Number);
    }

    [Theory]
    [InlineData("number", "CK_TreatmentPlanRevisionPhases_Number")]
    [InlineData("tooBig", "CK_TreatmentPlanRevisionPhases_Number")]
    [InlineData("blankName", "CK_TreatmentPlanRevisionPhases_Name")]
    [InlineData("blankVisit", "CK_TreatmentPlanRevisionPhases_Visit")]
    public async Task The_database_refuses_a_phase_that_breaks_a_shape_rule(string which, string constraint)
    {
        var plan = await PlanAsync();
        var revision = await InsertAsync(Revision(plan));
        var phase = new TreatmentPlanRevisionPhase { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, PhaseNumber = 1, Name = "Phase 1" };
        switch (which)
        {
            case "number": phase.PhaseNumber = 0; break;
            case "tooBig": phase.PhaseNumber = TreatmentPlanRules.PhasesMax + 1; break;
            case "blankName": phase.Name = "  "; break;
            case "blankVisit": phase.PlannedVisit = "  "; break;
        }
        var e = await Sql(() => InsertAsync(phase));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_phase_number_is_used_once_per_revision_and_a_phase_must_carry_the_plan_and_patient_of_its_revision()
    {
        var plan = await PlanAsync();
        var revision = await InsertAsync(Revision(plan));
        TreatmentPlanRevisionPhase Phase(int n = 1, Guid? planId = null) => new() { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = planId ?? plan.Id, PatientId = plan.PatientId, PhaseNumber = n, Name = $"Phase {n}", TargetDate = new DateOnly(2030, 5, 1) };
        await InsertAsync(Phase());
        Assert.Contains((await Sql(() => InsertAsync(Phase()))).Number, new[] { 2601, 2627 });
        Assert.Equal(51111, (await Sql(() => InsertAsync(Phase(2, Guid.NewGuid())))).Number);
    }

    [Fact]
    public async Task A_procedure_sits_in_a_phase_of_its_own_revision_or_in_none_and_only_once_per_revision()
    {
        var (plan, item) = await ArrangedAsync();
        var revision = await InsertAsync(Revision(plan));
        await InsertAsync(new TreatmentPlanRevisionPhase { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, PhaseNumber = 1, Name = "Phase 1" });
        TreatmentPlanRevisionItem In(int? phase, Guid? itemId = null) => new() { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, ItemId = itemId ?? item.Id, PhaseNumber = phase };

        Assert.Equal(547, (await Sql(() => InsertAsync(In(2)))).Number);                                  // the revision has no phase 2
        await InsertAsync(In(1));
        Assert.Contains((await Sql(() => InsertAsync(In(null)))).Number, new[] { 2601, 2627 });         // once per revision
        var second = await ItemAsync(plan, await DiagnosisAsync(), await ProcedureAsync("LOCAL-200"), number: 2);
        await InsertAsync(In(null, second.Id));                                                           // not yet placed is fine
    }

    [Fact]
    public async Task A_revision_never_holds_a_procedure_of_another_plan_or_patient()
    {
        var (plan, _) = await ArrangedAsync();
        var otherPlan = await PlanAsync(patient: Bo, key: "other");
        var foreign = await ItemAsync(otherPlan, await DiagnosisAsync(Bo), await ProcedureAsync("LOCAL-200"));
        var revision = await InsertAsync(Revision(plan));
        var e = await Sql(() => InsertAsync(new TreatmentPlanRevisionItem { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, ItemId = foreign.Id }));
        Assert.Equal(51104, e.Number);
    }

    // ---------- a procedure's acceptance, scheduling and completion ----------

    [Theory]
    [InlineData("badAcceptance", "CK_TreatmentPlanItemStates_Acceptance")]
    [InlineData("badScheduling", "CK_TreatmentPlanItemStates_Scheduling")]
    [InlineData("badCompletion", "CK_TreatmentPlanItemStates_Completion")]
    [InlineData("sequence", "CK_TreatmentPlanItemStates_Number")]
    [InlineData("declinedNoReason", "CK_TreatmentPlanItemStates_Declined")]
    [InlineData("declinedBlankReason", "CK_TreatmentPlanItemStates_Text")]
    [InlineData("scheduledPending", "CK_TreatmentPlanItemStates_Accepted")]
    [InlineData("completedDeclined", "CK_TreatmentPlanItemStates_Accepted")]
    [InlineData("dateWhenUnscheduled", "CK_TreatmentPlanItemStates_PlannedDate")]
    [InlineData("blankDecidedBy", "CK_TreatmentPlanItemStates_Text")]
    public async Task The_database_refuses_a_state_that_breaks_a_rule(string which, string constraint)
    {
        var (_, item) = await ArrangedAsync();
        var s = State(item);
        switch (which)
        {
            case "badAcceptance": s.Acceptance = "Maybe"; break;
            case "badScheduling": s.Scheduling = "Booked"; break;
            case "badCompletion": s.Completion = "Done"; break;
            case "sequence": s.Sequence = 0; break;
            case "declinedNoReason": s.Acceptance = "Declined"; break;
            case "declinedBlankReason": (s.Acceptance, s.Reason) = ("Declined", " "); break;
            case "scheduledPending": (s.Acceptance, s.Scheduling) = ("Pending", "Scheduled"); break;
            case "completedDeclined": (s.Acceptance, s.Reason, s.Completion) = ("Declined", "No", "Completed"); break;
            case "dateWhenUnscheduled": s.PlannedDate = new DateOnly(2030, 1, 1); break;
            case "blankDecidedBy": s.DecidedByName = " "; break;
        }
        var e = await Sql(() => InsertAsync(s));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task A_procedure_can_move_from_accepted_to_scheduled_to_completed_one_row_at_a_time_and_a_sequence_is_used_once()
    {
        var (_, item) = await ArrangedAsync();
        var first = State(item); first.DecidedByName = "Ann Alder (patient)";
        await InsertAsync(first);
        var scheduled = State(item, 2, scheduling: "Scheduled"); scheduled.PlannedDate = new DateOnly(2030, 3, 4);
        await InsertAsync(scheduled);
        await InsertAsync(State(item, 3, scheduling: "Scheduled", completion: "Completed"));
        Assert.Contains((await Sql(() => InsertAsync(State(item, 3)))).Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task Nothing_is_recorded_after_Completed_for_a_withdrawn_procedure_or_on_a_withdrawn_plan()
    {
        var (_, item) = await ArrangedAsync();
        await InsertAsync(State(item, 1, completion: "Completed"));
        Assert.Equal(51109, (await Sql(() => InsertAsync(State(item, 2)))).Number);

        var (_, withdrawn) = await ArrangedAsyncOnPlanAsync(withdrawn: true);
        Assert.Equal(51107, (await Sql(() => InsertAsync(State(withdrawn)))).Number);

        var (plan, live) = await ArrangedAsyncOnPlanAsync(patient: Bo);
        Assert.Equal(1, await RunAsync("UPDATE [TreatmentPlans] SET [Status] = 'Withdrawn', [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = {0}, [WithdrawnReason] = 'Entered in error' WHERE [Id] = {1}", S.Actor, plan.Id));
        Assert.Equal(51108, (await Sql(() => InsertAsync(State(live)))).Number);
    }

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item)> ArrangedAsyncOnPlanAsync(bool withdrawn = false, Guid? patient = null)
    {
        var p = patient ?? Ann;
        var plan = await PlanAsync(patient: p, key: $"p-{Guid.NewGuid():N}");
        var item = await ItemAsync(plan, await DiagnosisAsync(p), await ProcedureAsync($"LOCAL-{Guid.NewGuid():N}".Substring(0, 12)), withdrawn: withdrawn);
        return (plan, item);
    }

    [Fact]
    public async Task A_state_must_carry_the_plan_of_its_procedure_and_a_revision_that_exists()
    {
        var (plan, item) = await ArrangedAsync();
        Assert.Equal(51106, (await Sql(() => InsertAsync(State(item, plan: Guid.NewGuid())))).Number);
        var ahead = State(item); ahead.RevisionNumber = 2;                                               // the plan is still at revision 1
        Assert.Equal(51112, (await Sql(() => InsertAsync(ahead))).Number);
        Assert.Equal(1, await RunAsync("UPDATE [TreatmentPlans] SET [CurrentRevision] = 2 WHERE [Id] = {0}", plan.Id));
        await InsertAsync(ahead);
    }

    // ---------- resolving an old diagnosis reference ----------

    [Fact]
    public async Task A_diagnosis_resolves_to_a_plan_of_its_own_patient_with_the_text_it_holds_and_only_once()
    {
        var plan = await PlanAsync();
        var diagnosis = await DiagnosisWithReferenceAsync(Ann, "plan-2026-07");
        DiagnosisPlanResolution Resolution(Guid? planId = null, Guid? patient = null, string text = "plan-2026-07", string method = "ReferenceCode") =>
            new() { Id = Guid.NewGuid(), DiagnosisId = diagnosis, PatientId = patient ?? Ann, PlanId = planId ?? plan.Id, ReferenceText = text, Method = method, ResolvedAtUtc = DateTimeOffset.UtcNow, ResolvedByUserId = Guid.NewGuid() };

        var other = await PlanAsync(patient: Bo, key: "bos");
        Assert.Equal(51114, (await Sql(() => InsertAsync(Resolution(other.Id)))).Number);                     // another patient's plan
        Assert.Equal(51114, (await Sql(() => InsertAsync(Resolution(other.Id, Bo)))).Number);                 // another patient's plan and the diagnosis is not theirs either
        Assert.Equal(51115, (await Sql(() => InsertAsync(Resolution(text: "plan-2026-08")))).Number);        // not what the diagnosis says
        Assert.Equal(547, (await Sql(() => InsertAsync(Resolution(method: "Guessed")))).Number);
        Assert.Equal(547, (await Sql(() => InsertAsync(Resolution(text: "  ")))).Number);

        await InsertAsync(Resolution());
        Assert.Contains((await Sql(() => InsertAsync(Resolution()))).Number, new[] { 2601, 2627 });         // once per diagnosis
        var noReference = await DiagnosisWithReferenceAsync(Ann, null);
        var bad = Resolution(); bad.Id = Guid.NewGuid(); bad.DiagnosisId = noReference;
        Assert.Equal(51115, (await Sql(() => InsertAsync(bad))).Number);                                      // a diagnosis with no reference has nothing to resolve
    }

    // ---------- append-only ----------

    [Fact]
    public async Task Revisions_phases_procedures_in_a_revision_states_and_resolutions_are_never_edited_or_deleted()
    {
        var (plan, item) = await ArrangedAsync();
        var revision = await InsertAsync(Revision(plan));
        var phase = await InsertAsync(new TreatmentPlanRevisionPhase { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, PhaseNumber = 1, Name = "Phase 1" });
        await InsertAsync(new TreatmentPlanRevisionItem { Id = Guid.NewGuid(), RevisionId = revision.Id, PlanId = plan.Id, PatientId = plan.PatientId, ItemId = item.Id, PhaseNumber = 1 });
        await InsertAsync(State(item));
        var diagnosis = await DiagnosisWithReferenceAsync(Ann, "plan-1");
        await InsertAsync(new DiagnosisPlanResolution { Id = Guid.NewGuid(), DiagnosisId = diagnosis, PatientId = Ann, PlanId = plan.Id, ReferenceText = "plan-1", Method = "PlanId", ResolvedAtUtc = DateTimeOffset.UtcNow, ResolvedByUserId = Guid.NewGuid() });

        var cases = new (string Table, string Edit, int Number)[]
        {
            ("TreatmentPlanRevisions", "[Title] = 'Changed'", 51101), ("TreatmentPlanRevisionPhases", "[Name] = 'Changed'", 51102), ("TreatmentPlanRevisionItems", "[IsWithdrawn] = 1", 51103),
            ("TreatmentPlanItemStates", "[Acceptance] = 'Declined', [Reason] = 'Changed my mind'", 51105), ("DiagnosisPlanResolutions", "[Method] = 'Chosen'", 51113),
        };
        foreach (var (table, edit, number) in cases)
        {
            Assert.Equal(number, (await Sql(() => RunAsync($"UPDATE [{table}] SET {edit}"))).Number);
            Assert.Equal(number, (await Sql(() => RunAsync($"DELETE FROM [{table}]"))).Number);
        }
        Assert.NotEqual(Guid.Empty, phase.Id);
    }

    // ---------- care that has been done stays on the plan ----------

    [Fact]
    public async Task A_completed_procedure_cannot_be_withdrawn_but_one_that_is_not_completed_can()
    {
        var (_, completed) = await ArrangedAsyncOnPlanAsync();
        await InsertAsync(State(completed, 1, completion: "Completed"));
        const string withdraw = "UPDATE [TreatmentPlanItems] SET [WithdrawnAtUtc] = SYSDATETIMEOFFSET(), [WithdrawnByUserId] = {0}, [WithdrawnReason] = 'Entered in error' WHERE [Id] = {1}";
        Assert.Equal(51117, (await Sql(() => RunAsync(withdraw, S.Actor, completed.Id))).Number);

        var (_, open) = await ArrangedAsyncOnPlanAsync(patient: Bo);
        await InsertAsync(State(open, 1));                                                                                // accepted, not completed
        Assert.Equal(1, await RunAsync(withdraw, S.Actor, open.Id));
    }
}
