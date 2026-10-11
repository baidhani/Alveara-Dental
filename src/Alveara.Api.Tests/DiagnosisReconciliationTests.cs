using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 against real SQL Server: tying the free-text treatment-plan references that diagnoses recorded before plans existed to real plans. A reference is resolved only to a plan of the same patient,
/// by the plan's reference code or id; the diagnosis's text and history are never touched and the database refuses to change that text afterwards; a reference with no safe target stays visibly unresolved
/// with the reason; repeating changes nothing; another patient's plan is never matched or accepted; a failed log stores nothing.
/// </summary>
public class DiagnosisReconciliationTests : TreatmentPlanTestBase
{
    private ProcedureDetailView? _procedure;
    private readonly Dictionary<Guid, Guid> _diagnosisFor = new();

    private Task<T> Rec<T>(Func<DiagnosisReconciliationService, Task<T>> action) => WithDb(db => action(new DiagnosisReconciliationService(db, Clock)));
    private Task<ReconciliationResult> Reconcile(Guid patient, bool apply = true) => Rec(s => s.ReconcileAsync(patient, apply, S.Actor, default));
    private Task<ReconciliationItem> Resolve(Guid diagnosis, Guid? plan) => Rec(s => s.ResolveAsync(diagnosis, plan, S.Actor, default));

    private async Task<PlanView> PlanFor(Guid patient, string? code = null, string title = "Plan")
    {
        _procedure ??= await ProcedureAsync();
        if (!_diagnosisFor.TryGetValue(patient, out var diagnosis)) _diagnosisFor[patient] = diagnosis = await DiagnosisAsync(patient);
        var plan = await CreateAsync([Item(diagnosis, _procedure.Summary.Id)], title, patient: patient);
        return code is null ? plan : await Svc(s => s.SetReferenceCodeAsync(plan.Id, code, plan.RowVersion, S.Actor, default));
    }

    private async Task<Guid> DiagnosisWithReference(Guid patient, string? reference, string label = "Chronic periodontitis")
    {
        var encounter = patient == Bo ? BoEncounter : AnnEncounter;
        return (await WithDb(db => new DiagnosisService(db, Clock).RecordAsync(patient, Guid.NewGuid().ToString("N"), new DiagnosisInput(encounter, label, null, null, reference), S.Actor, default))).Id;
    }

    private Task<DiagnosisView> Diagnosis(Guid id) => WithDb(db => new DiagnosisService(db, Clock).GetAsync(id, default));
    private Task<int> ResolutionRowsAsync() => WithDb(db => db.DiagnosisPlanResolutions.CountAsync());

    // ---------- a plan's reference code ----------

    [Fact]
    public async Task A_reference_code_is_trimmed_set_once_unique_per_patient_and_logged()
    {
        var plan = await PlanFor(Ann);
        var coded = await Svc(s => s.SetReferenceCodeAsync(plan.Id, "  PLAN   2026-07 ", plan.RowVersion, S.Actor, default));
        Assert.Equal("PLAN 2026-07", coded.ReferenceCode);
        Assert.NotEqual(plan.RowVersion, coded.RowVersion);
        Assert.Equal(coded.CurrentRevision, plan.CurrentRevision);                                               // not a change to the plan's content

        var again = await Svc(s => s.SetReferenceCodeAsync(plan.Id, "PLAN 2026-07", plan.RowVersion, S.Actor, default));    // stale copy, same code: quiet
        Assert.Equal(coded.RowVersion, again.RowVersion);
        Assert.Equal("reference_code_set", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(plan.Id, "OTHER", coded.RowVersion, S.Actor, default)))).Code);

        var second = await PlanFor(Ann, title: "Second");
        Assert.Equal("reference_code_in_use", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(second.Id, "plan 2026-07", second.RowVersion, S.Actor, default)))).Code);   // not even in another case
        var bos = await PlanFor(Bo, "PLAN 2026-07");                                                                  // another patient may use it
        Assert.Equal("PLAN 2026-07", bos.ReferenceCode);

        var history = await Svc(s => s.HistoryAsync(plan.Id, default));
        Assert.Equal("ReferenceCodeSet", history[^1].ChangeType);
        Assert.Equal(1, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == "TreatmentPlanReferenceCodeSet" && a.TargetUserAccountId == plan.Id)));
    }

    [Fact]
    public async Task A_bad_reference_code_or_a_stale_copy_is_refused_and_a_withdrawn_plan_may_still_be_coded()
    {
        var plan = await PlanFor(Ann);
        Assert.Equal("referenceCode", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(plan.Id, "   ", plan.RowVersion, S.Actor, default)))).FieldErrors.Keys.Single());
        Assert.Equal("referenceCode", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(plan.Id, new string('x', 101), plan.RowVersion, S.Actor, default)))).FieldErrors.Keys.Single());
        Assert.Equal("referenceCode", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(plan.Id, "bad\u0007", plan.RowVersion, S.Actor, default)))).FieldErrors.Keys.Single());
        Assert.Equal("row_version_required", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(plan.Id, "CODE", "", S.Actor, default)))).Code);
        var renamed = await Svc(s => s.RenameAsync(plan.Id, "Renamed", plan.RowVersion, S.Actor, default));
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => Svc(s => s.SetReferenceCodeAsync(plan.Id, "CODE", plan.RowVersion, S.Actor, default)));
        var withdrawn = await Svc(s => s.WithdrawAsync(plan.Id, "Entered in error", renamed.RowVersion, S.Actor, default));
        Assert.Equal("CODE", (await Svc(s => s.SetReferenceCodeAsync(plan.Id, "CODE", withdrawn.RowVersion, S.Actor, default))).ReferenceCode);
        Assert.Equal("plan_not_found", (await Refused(() => Svc(s => s.SetReferenceCodeAsync(Guid.NewGuid(), "CODE", "AAAAAAAAAAA=", S.Actor, default)))).Code);
    }

    // ---------- automatic reconciliation ----------

    [Fact]
    public async Task A_preview_saves_nothing_and_resolving_ties_each_reference_to_the_patients_plan_without_touching_the_diagnosis_or_its_history()
    {
        var plan = await PlanFor(Ann, "PLAN-7", "Gum health plan");
        var idPlan = await PlanFor(Ann, title: "Another plan");
        var byCode = await DiagnosisWithReference(Ann, "plan-7");                                                       // other case: still the same code
        var byId = await DiagnosisWithReference(Ann, idPlan.Id.ToString("D").ToUpperInvariant());
        var before = await WithDb(db => db.DiagnosisVersions.CountAsync());

        var preview = await Reconcile(Ann, apply: false);
        Assert.Equal((false, 2, 0, 0), (preview.Applied, preview.Matched, preview.AlreadyResolved, preview.Unresolved));
        Assert.All(preview.Items, i => Assert.Equal("Eligible", i.Outcome));
        Assert.Equal(0, await ResolutionRowsAsync());
        Assert.Equal("Unresolved", (await Diagnosis(byCode)).TreatmentPlanReferenceState);

        var result = await Reconcile(Ann);
        Assert.Equal((true, 2, 0, 0), (result.Applied, result.Matched, result.AlreadyResolved, result.Unresolved));
        Assert.Equal(("Resolved", plan.Id, "ReferenceCode"), (result.Items.Single(i => i.DiagnosisId == byCode).Outcome, result.Items.Single(i => i.DiagnosisId == byCode).PlanId, result.Items.Single(i => i.DiagnosisId == byCode).Method));
        Assert.Equal(("PlanId", idPlan.Id), (result.Items.Single(i => i.DiagnosisId == byId).Method, result.Items.Single(i => i.DiagnosisId == byId).PlanId));

        var view = await Diagnosis(byCode);
        Assert.Equal(("Resolved", "plan-7", plan.Id, "Gum health plan", "ReferenceCode"), (view.TreatmentPlanReferenceState, view.TreatmentPlanReference, view.ResolvedPlanId, view.ResolvedPlanTitle, view.ResolvedMethod));
        Assert.NotNull(view.ResolvedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(view.ResolvedByName));
        Assert.Equal(before, await WithDb(db => db.DiagnosisVersions.CountAsync()));                                // the diagnosis's history did not change
        var versions = (await WithDb(db => new DiagnosisService(db, Clock).HistoryAsync(byCode, default))).Versions;
        Assert.All(versions, v => Assert.Equal("Unresolved", v.TreatmentPlanReferenceState));                       // and says what it said then
        Assert.Equal(2, await WithDb(db => db.AuditLogEntries.CountAsync(a => a.EventType == "DiagnosisPlanReferenceResolved")));
    }

    [Fact]
    public async Task Running_it_again_changes_nothing_and_reports_what_is_already_resolved()
    {
        await PlanFor(Ann, "PLAN-7");
        var first = await DiagnosisWithReference(Ann, "PLAN-7");
        await Reconcile(Ann);
        var audit = await WithDb(db => db.AuditLogEntries.CountAsync());

        var again = await Reconcile(Ann);
        Assert.Equal((0, 1, 0), (again.Matched, again.AlreadyResolved, again.Unresolved));
        Assert.Equal("AlreadyResolved", again.Items.Single().Outcome);
        Assert.Equal((1, audit), (await ResolutionRowsAsync(), await WithDb(db => db.AuditLogEntries.CountAsync())));
        Assert.Equal("Resolved", (await Diagnosis(first)).TreatmentPlanReferenceState);

        var results = await Task.WhenAll(Reconcile(Ann), Reconcile(Ann));                                          // and two at once end with one resolution
        Assert.All(results, r => Assert.Equal(1, r.AlreadyResolved));
        Assert.Equal(1, await ResolutionRowsAsync());
    }

    [Fact]
    public async Task A_reference_with_no_safe_target_stays_visibly_unresolved_with_the_reason_and_is_never_changed()
    {
        var plan = await PlanFor(Ann, "PLAN-7");
        var other = await PlanFor(Ann, title: "Other");
        await Svc(s => s.SetReferenceCodeAsync(other.Id, plan.Id.ToString("D"), other.RowVersion, S.Actor, default));     // this plan's code is the other plan's id
        var none = await DiagnosisWithReference(Ann, "plan-99");
        var ambiguous = await DiagnosisWithReference(Ann, plan.Id.ToString("D"));
        var withdrawn = await DiagnosisWithReference(Ann, "PLAN-7");
        await WithdrawDiagnosisAsync(withdrawn);
        await DiagnosisWithReference(Ann, null);                                                                         // no reference: nothing to do

        var result = await Reconcile(Ann);
        Assert.Equal((0, 0, 3), (result.Matched, result.AlreadyResolved, result.Unresolved));
        Assert.Equal(3, result.Items.Count);
        var byDiagnosis = result.Items.ToDictionary(i => i.DiagnosisId);
        Assert.Equal(("NoMatch", "plan-99"), (byDiagnosis[none].Outcome, byDiagnosis[none].Reference));
        Assert.Equal("Ambiguous", byDiagnosis[ambiguous].Outcome);
        Assert.Equal("Skipped", byDiagnosis[withdrawn].Outcome);
        Assert.All(result.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Reason)));
        Assert.Equal(0, await ResolutionRowsAsync());
        var view = await Diagnosis(none);
        Assert.Equal(("plan-99", "Unresolved", null), (view.TreatmentPlanReference, view.TreatmentPlanReferenceState, view.ResolvedPlanId));
    }

    [Fact]
    public async Task Another_patients_plan_is_never_matched_even_when_its_code_or_id_is_the_reference()
    {
        var bosPlan = await PlanFor(Bo, "SHARED");
        var anns = await DiagnosisWithReference(Ann, "SHARED");
        var annsById = await DiagnosisWithReference(Ann, bosPlan.Id.ToString("D"));
        var bos = await DiagnosisWithReference(Bo, "SHARED");

        var forAnn = await Reconcile(Ann);
        Assert.Equal((0, 2), (forAnn.Matched, forAnn.Unresolved));
        Assert.All(forAnn.Items, i => Assert.Equal("NoMatch", i.Outcome));
        Assert.Equal(0, await ResolutionRowsAsync());

        var forBo = await Reconcile(Bo);
        Assert.Equal((1, bosPlan.Id), (forBo.Matched, forBo.Items.Single().PlanId));
        Assert.Equal("Resolved", (await Diagnosis(bos)).TreatmentPlanReferenceState);
        Assert.Equal<IEnumerable<string?>>(["Unresolved", "Unresolved"], new[] { (await Diagnosis(anns)).TreatmentPlanReferenceState, (await Diagnosis(annsById)).TreatmentPlanReferenceState });
        Assert.Equal("patient_not_found", (await Refused(() => Reconcile(Guid.NewGuid()))).Code);
    }

    // ---------- choosing the plan ----------

    [Fact]
    public async Task A_person_can_choose_the_plan_for_a_reference_and_it_is_final_and_quiet_to_repeat()
    {
        var plan = await PlanFor(Ann, title: "Chosen plan");
        var other = await PlanFor(Ann, title: "Not this one");
        var diagnosis = await DiagnosisWithReference(Ann, "some old note");

        var item = await Resolve(diagnosis, plan.Id);
        Assert.Equal(("Resolved", "Chosen", "Chosen plan"), (item.Outcome, item.Method, item.PlanTitle));
        Assert.Equal(("Resolved", "some old note", plan.Id), ((await Diagnosis(diagnosis)).TreatmentPlanReferenceState, (await Diagnosis(diagnosis)).TreatmentPlanReference, (await Diagnosis(diagnosis)).ResolvedPlanId));
        Assert.Equal("AlreadyResolved", (await Resolve(diagnosis, plan.Id)).Outcome);
        Assert.Equal(("already_resolved", 409), ((await Refused(() => Resolve(diagnosis, other.Id))).Code, (await Refused(() => Resolve(diagnosis, other.Id))).StatusCode));
        Assert.Equal(1, await ResolutionRowsAsync());
        Assert.Equal("AlreadyResolved", (await Reconcile(Ann)).Items.Single().Outcome);
    }

    [Fact]
    public async Task Choosing_another_patients_plan_or_an_unknown_one_is_refused_in_the_same_words_and_a_missing_reference_or_withdrawn_diagnosis_is_refused()
    {
        var bosPlan = await PlanFor(Bo);
        var diagnosis = await DiagnosisWithReference(Ann, "old note");
        var other = await Refused(() => Resolve(diagnosis, bosPlan.Id));
        var unknown = await Refused(() => Resolve(diagnosis, Guid.NewGuid()));
        Assert.Equal(("plan_not_found", 404, other.Message), (unknown.Code, unknown.StatusCode, unknown.Message));
        Assert.Equal(("plan_not_found", 404), (other.Code, other.StatusCode));                                          // it does not reveal that the plan exists for someone else
        Assert.Equal(0, await ResolutionRowsAsync());

        var annsPlan = await PlanFor(Ann);
        Assert.Equal("planId", (await Refused(() => Resolve(diagnosis, null))).FieldErrors.Keys.Single());
        var bare = await DiagnosisWithReference(Ann, null);
        Assert.Equal("diagnosisId", (await Refused(() => Resolve(bare, annsPlan.Id))).FieldErrors.Keys.Single());
        var gone = await DiagnosisWithReference(Ann, "withdrawn note");
        await WithdrawDiagnosisAsync(gone);
        Assert.Equal(("diagnosis_withdrawn", 409), ((await Refused(() => Resolve(gone, annsPlan.Id))).Code, (await Refused(() => Resolve(gone, annsPlan.Id))).StatusCode));
        Assert.Equal("diagnosis_not_found", (await Refused(() => Resolve(Guid.NewGuid(), annsPlan.Id))).Code);
        Assert.Equal(0, await ResolutionRowsAsync());
    }

    // ---------- the reference is kept ----------

    [Fact]
    public async Task Once_resolved_the_diagnosis_keeps_its_reference_in_the_service_and_in_the_database()
    {
        await PlanFor(Ann, "PLAN-7");
        var diagnosis = await DiagnosisWithReference(Ann, "PLAN-7");
        await Reconcile(Ann);
        var view = await Diagnosis(diagnosis);

        var replace = await Assert.ThrowsAsync<DiagnosisException>(() => WithDb(db => new DiagnosisService(db, Clock).CorrectAsync(diagnosis, view.RowVersion,
            new DiagnosisCorrection(view.Label, null, null, "PLAN-8", false), "Wrong plan", S.Actor, default)));
        Assert.Equal(("treatment_plan_reference_resolved", 409), (replace.Code, replace.StatusCode));
        var clear = await Assert.ThrowsAsync<DiagnosisException>(() => WithDb(db => new DiagnosisService(db, Clock).CorrectAsync(diagnosis, view.RowVersion,
            new DiagnosisCorrection(view.Label, null, null, null, true), "No plan", S.Actor, default)));
        Assert.Equal("treatment_plan_reference_resolved", clear.Code);

        var label = await WithDb(db => new DiagnosisService(db, Clock).CorrectAsync(diagnosis, view.RowVersion, new DiagnosisCorrection("Gingivitis", null, null, null, false), "Better wording", S.Actor, default));
        Assert.Equal(("Gingivitis", "PLAN-7", "Resolved"), (label.Label, label.TreatmentPlanReference, label.TreatmentPlanReferenceState));       // the rest of the diagnosis can still be corrected

        await using var db2 = Fixture.CreateContext();
        var e = await Assert.ThrowsAnyAsync<Exception>(() => db2.Database.ExecuteSqlRawAsync("UPDATE [Diagnoses] SET [TreatmentPlanReference] = 'PLAN-8' WHERE [Id] = {0}", diagnosis));
        Assert.Equal(51116, Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e).Number);
        Assert.Equal(1, await db2.Database.ExecuteSqlRawAsync("UPDATE [Diagnoses] SET [Notes] = 'Unrelated edit' WHERE [Id] = {0}", diagnosis));      // only the reference is locked
    }

    // ---------- failure: the log is unavailable ----------

    [Fact]
    public async Task A_failed_audit_write_resolves_nothing_and_says_so_and_the_same_run_works_once_the_log_is_back()
    {
        await PlanFor(Ann, "PLAN-7");
        var diagnosis = await DiagnosisWithReference(Ann, "PLAN-7");
        await using (var db = Fixture.CreateContext())
            await db.Database.ExecuteSqlRawAsync(@"CREATE TRIGGER [TR_Test_RefuseResolutionAudit] ON [AuditLogEntries] INSTEAD OF INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE EventType = 'DiagnosisPlanReferenceResolved') THROW 59030, 'audit unavailable', 1;
    INSERT INTO [AuditLogEntries] SELECT * FROM inserted;
END");
        var e = await Refused(() => Reconcile(Ann));
        Assert.Equal(("save_failed", 503), (e.Code, e.StatusCode));
        Assert.DoesNotContain("audit", e.Message);
        var anyPlan = await WithDb(db => db.TreatmentPlans.Select(p => p.Id).FirstAsync());
        Assert.Equal("save_failed", (await Refused(() => Resolve(diagnosis, anyPlan))).Code);
        Assert.Equal(0, await ResolutionRowsAsync());

        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_Test_RefuseResolutionAudit]");
        Assert.Equal(1, (await Reconcile(Ann)).Matched);
        Assert.Equal(1, await ResolutionRowsAsync());
    }
}
