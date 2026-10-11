using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 R02 (review finding ALV-015-C01-R01-01) against real SQL Server: the procedure catalog counts the treatment-plan procedures that refer to a procedure. The usage answer lists the source with its
/// number, every persisted plan procedure counts (withdrawn ones too, and across patients), inactivating needs the use acknowledged and changes nothing until then, the plan keeps showing the procedure and the fee
/// it copied, and the inactive procedure is refused for new plan work.
/// </summary>
public class TreatmentPlanProcedureUsageTests : TreatmentPlanTestBase
{
    private Task<ProcedureUsageView> UsageOf(Guid procedureId) => Catalog(s => s.UsageAsync(procedureId, default));

    private async Task<(PlanView Plan, ProcedureDetailView Procedure)> PlanOnAsync(ProcedureDetailView procedure, Guid? patient = null, string key = "p")
    {
        var diagnosis = await DiagnosisAsync(patient);
        var plan = await CreateAsync([Item(diagnosis, procedure.Summary.Id)], patient: patient, key: $"{key}-{Guid.NewGuid():N}");
        return (plan, procedure);
    }

    [Fact]
    public async Task The_usage_answer_lists_the_treatment_plan_source_with_the_number_of_plan_procedures_and_zero_when_none_refer_to_it()
    {
        var used = await ProcedureAsync("LOCAL-100");
        var unused = await ProcedureAsync("LOCAL-200");
        var before = await UsageOf(used.Summary.Id);
        Assert.Contains(before.BySource, b => b.Source == "Treatment plans" && b.Count == 0);                       // listed even when nothing refers to it
        Assert.Equal(0, before.Count);

        await PlanOnAsync(used);

        var after = await UsageOf(used.Summary.Id);
        Assert.Equal(1, after.Count);
        Assert.Contains(after.BySource, b => b.Source == "Treatment plans" && b.Count == 1);
        Assert.Contains(after.BySource, b => b.Source == "Odontogram links");                                       // the existing source is still counted beside it
        Assert.Equal(0, (await UsageOf(unused.Summary.Id)).Count);
    }

    [Fact]
    public async Task Every_persisted_plan_procedure_counts_withdrawn_ones_and_other_patients_plans_included()
    {
        var procedure = await ProcedureAsync("LOCAL-100");
        var (first, _) = await PlanOnAsync(procedure);
        var (second, _) = await PlanOnAsync(procedure, Bo);
        Assert.Equal(2, (await UsageOf(procedure.Summary.Id)).Count);

        await Svc(s => s.WithdrawItemAsync(first.Id, first.Items[0].Id, "Entered in error", first.RowVersion, S.Actor, default));
        await Svc(s => s.WithdrawAsync(second.Id, "Entered in error", second.RowVersion, S.Actor, default));
        Assert.Equal(2, (await UsageOf(procedure.Summary.Id)).Count);                                              // a withdrawn procedure and a withdrawn plan are still references
    }

    [Fact]
    public async Task Inactivating_needs_the_use_acknowledged_and_changes_nothing_until_then()
    {
        var procedure = await ProcedureAsync("LOCAL-100");
        await PlanOnAsync(procedure);

        var refused = await Assert.ThrowsAsync<ProcedureCatalogException>(() => Catalog(s => s.InactivateAsync(procedure.Summary.Id, "No longer offered", false, procedure.Summary.RowVersion, S.Actor, default)));
        Assert.Equal(("usage_confirmation_required", 409, 1), (refused.Code, refused.StatusCode, refused.Usage!.Count));
        Assert.Contains(refused.Usage.BySource, b => b.Source == "Treatment plans" && b.Count == 1);
        Assert.True((await Catalog(s => s.GetAsync(procedure.Summary.Id, null, default))).Summary.IsActive);        // still active

        var done = await Catalog(s => s.InactivateAsync(procedure.Summary.Id, "No longer offered", true, procedure.Summary.RowVersion, S.Actor, default));
        Assert.False(done.Summary.IsActive);
    }

    [Fact]
    public async Task A_procedure_nothing_uses_inactivates_without_acknowledgement()
    {
        var procedure = await ProcedureAsync("LOCAL-100");
        var done = await Catalog(s => s.InactivateAsync(procedure.Summary.Id, "Not needed", false, procedure.Summary.RowVersion, S.Actor, default));
        Assert.False(done.Summary.IsActive);
    }

    [Fact]
    public async Task After_inactivation_the_plan_still_shows_the_procedure_and_the_fee_it_copied_and_new_plan_work_refuses_the_inactive_procedure()
    {
        var procedure = await ProcedureAsync("LOCAL-100", fee: 120m);
        var (plan, _) = await PlanOnAsync(procedure);
        await Catalog(s => s.InactivateAsync(procedure.Summary.Id, "No longer offered", true, procedure.Summary.RowVersion, S.Actor, default));

        var shown = await Svc(s => s.GetAsync(plan.Id, default));
        Assert.Equal((120m, "LOCAL-100", 1, 120m), (shown.Items[0].Fee, shown.Items[0].ProcedureCode, shown.ActiveItemCount, shown.EstimateTotal));

        var diagnosis = shown.Items[0].DiagnosisId;
        var refused = await Refused(() => Svc(s => s.AddItemAsync(plan.Id, Item(diagnosis, procedure.Summary.Id, key: "again"), shown.RowVersion, S.Actor, default)));
        Assert.Equal("validation_failed", refused.Code);
        Assert.Contains("inactive", refused.FieldErrors["procedureId"]);
        Assert.Single((await Svc(s => s.GetAsync(plan.Id, default))).Items);

        var newPlan = await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)], patient: Ann));
        Assert.Contains("inactive", newPlan.FieldErrors["items[0].procedureId"]);
    }
}
