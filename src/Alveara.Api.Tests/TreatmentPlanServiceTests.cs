using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-015 against real SQL Server: creating a treatment plan. Given a patient's diagnosis, a plan links it to proposed catalog procedures and fee estimates (the fee copied from the exact catalog
/// version, labelled as a practice estimate); an error in the entry is refused with a message per field and nothing is stored; a repeated or simultaneous save makes one plan; and a later catalog fee
/// change never alters what a plan says.
/// </summary>
public class TreatmentPlanServiceTests : TreatmentPlanTestBase
{
    // ---------- acceptance 1: a plan links a diagnosis to proposed procedures and fee estimates ----------

    [Fact]
    public async Task A_plan_links_the_patients_diagnosis_to_proposed_procedures_with_fee_estimates_copied_from_the_catalog()
    {
        var diagnosis = await DiagnosisAsync(label: "Chronic periodontitis");
        var cleaning = await ProcedureAsync("PERIO-1", fee: 95.5m, description: "Periodontal maintenance");
        var scaling = await ProcedureAsync("TOOTH-1", ProcedureScopes.Tooth, ProcedureDentitions.Permanent, 120m, "Scaling and root planing, one tooth");

        var plan = await CreateAsync([Item(diagnosis, cleaning.Summary.Id), Item(diagnosis, scaling.Summary.Id, "16")], "Periodontal plan");

        Assert.Equal((Ann, "Periodontal plan", TreatmentPlanStatuses.Proposed, 2), (plan.PatientId, plan.Title, plan.Status, plan.ActiveItemCount));
        Assert.Equal(215.5m, plan.EstimateTotal);
        Assert.Equal("USD", plan.Currency);
        Assert.Contains("not an insurance estimate", plan.EstimateLabel);
        Assert.Equal("Dr. Okafor", plan.CreatedByName);
        var first = plan.Items[0];
        Assert.Equal((1, diagnosis, "Chronic periodontitis", cleaning.Summary.Id, "PERIO-1", "Periodontal maintenance", 95.5m, 1), (first.ItemNumber, first.DiagnosisId, first.DiagnosisLabel, first.ProcedureId, first.ProcedureCode, first.ProcedureDescription, first.Fee, first.ProcedureVersionNumber));
        var second = plan.Items[1];
        Assert.Equal((2, "16", null, 120m), (second.ItemNumber, second.ToothKey, second.Surface, second.Fee));

        await using var db = Fixture.CreateContext();
        var stored = await db.TreatmentPlanItems.AsNoTracking().OrderBy(i => i.ItemNumber).ToListAsync();
        Assert.Equal(new[] { cleaning.Versions[0].VersionId, scaling.Versions[0].VersionId }, stored.Select(i => i.ProcedureVersionId));      // the exact catalog versions are remembered
        Assert.Equal((2, 3, 1 + 2), (stored.Count, await db.TreatmentPlanEvents.CountAsync(), 1 + 2));
    }

    [Fact]
    public async Task One_plan_can_cover_several_diagnoses()
    {
        var perio = await DiagnosisAsync(label: "Chronic periodontitis");
        var caries = await DiagnosisAsync(label: "Caries");
        var maintenance = await ProcedureAsync("PERIO-1");
        var filling = await ProcedureAsync("FILL-1", ProcedureScopes.ToothSurface, ProcedureDentitions.Both, 140m, "Resin filling");

        var plan = await CreateAsync([Item(perio, maintenance.Summary.Id), Item(caries, filling.Summary.Id, "16", "O")]);

        Assert.Equal(new[] { "Chronic periodontitis", "Caries" }, plan.Items.Select(i => i.DiagnosisLabel));
        Assert.Equal(("16", "O"), (plan.Items[1].ToothKey, plan.Items[1].Surface));
    }

    // ---------- acceptance 2: an error in the entry prompts for correction ----------

    [Fact]
    public async Task Every_missing_or_wrong_field_is_named_in_one_refusal_and_nothing_is_stored()
    {
        var e = await Refused(() => Svc(s => s.CreateAsync(Ann, new PlanInput(null, null, null), S.Actor, default)));
        Assert.Equal(("validation_failed", 400), (e.Code, e.StatusCode));
        foreach (var field in new[] { "title", "idempotencyKey", "items" }) Assert.True(e.FieldErrors.ContainsKey(field), $"{field} should be named");
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_missing_diagnosis_or_procedure_on_an_item_is_named_with_the_items_position()
    {
        var e = await Refused(() => CreateAsync([new PlanItemInput(null, null, null, null, null)]));
        Assert.Contains("Choose the diagnosis", e.FieldErrors["items[0].diagnosisId"]);
        Assert.Contains("Choose the procedure", e.FieldErrors["items[0].procedureId"]);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task One_wrong_item_among_good_ones_stores_nothing_at_all()
    {
        var diagnosis = await DiagnosisAsync();
        var good = await ProcedureAsync("PERIO-1");
        var e = await Refused(() => CreateAsync([Item(diagnosis, good.Summary.Id), Item(diagnosis, Guid.NewGuid())]));
        Assert.Contains("not found in the catalog", e.FieldErrors["items[1].procedureId"]);
        Assert.False(e.FieldErrors.ContainsKey("items[0].procedureId"));
        Assert.Equal((0, 0, 0, 0), await CountsAsync());                                      // the good first item was not saved either
    }

    [Fact]
    public async Task Another_patients_diagnosis_is_refused_in_the_same_words_as_one_that_does_not_exist_so_nothing_is_revealed()
    {
        var bosDiagnosis = await DiagnosisAsync(Bo);
        var procedure = await ProcedureAsync();
        var other = await Refused(() => CreateAsync([Item(bosDiagnosis, procedure.Summary.Id)]));
        var unknown = await Refused(() => CreateAsync([Item(Guid.NewGuid(), procedure.Summary.Id)]));
        Assert.Equal(unknown.FieldErrors["items[0].diagnosisId"], other.FieldErrors["items[0].diagnosisId"]);
        Assert.Contains("not found for this patient", other.FieldErrors["items[0].diagnosisId"]);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task A_withdrawn_diagnosis_cannot_be_planned_for()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        await WithdrawDiagnosisAsync(diagnosis);
        var e = await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)]));
        Assert.Contains("withdrawn", e.FieldErrors["items[0].diagnosisId"]);
    }

    [Fact]
    public async Task An_inactive_or_unknown_procedure_cannot_be_proposed()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        await Catalog(s => s.InactivateAsync(procedure.Summary.Id, "No longer offered", false, procedure.Summary.RowVersion, S.Actor, default));

        Assert.Contains("inactive", (await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)]))).FieldErrors["items[0].procedureId"]);
        Assert.Contains("not found in the catalog", (await Refused(() => CreateAsync([Item(diagnosis, Guid.NewGuid())]))).FieldErrors["items[0].procedureId"]);
    }

    [Theory]
    [InlineData(ProcedureScopes.WholeMouth, null, "16", null, "items[0].toothKey", "larger area")]
    [InlineData(ProcedureScopes.Tooth, null, null, null, "items[0].toothKey", "choose the tooth")]
    [InlineData(ProcedureScopes.Tooth, null, "16", "O", "items[0].surface", "leave the surface empty")]
    [InlineData(ProcedureScopes.ToothSurface, null, "16", null, "items[0].surface", "choose the surface")]
    [InlineData(ProcedureScopes.ToothSurface, null, null, "O", "items[0].surface", "needs the tooth")]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Permanent, "55", null, "items[0].toothKey", "permanent teeth")]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Primary, "16", null, "items[0].toothKey", "primary teeth")]
    [InlineData(ProcedureScopes.Tooth, null, "99", null, "items[0].toothKey", "not a tooth")]
    [InlineData(ProcedureScopes.ToothSurface, null, "11", "O", "items[0].surface", "does not exist")]       // an incisor has no occlusal surface
    public async Task A_tooth_or_surface_that_does_not_fit_the_procedure_is_refused_on_the_right_field(string scope, string? dentition, string? tooth, string? surface, string field, string messagePart)
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync("SITE-1", scope, dentition);
        var e = await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id, tooth, surface)]));
        Assert.Contains(messagePart, e.FieldErrors[field], StringComparison.OrdinalIgnoreCase);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Theory]
    [InlineData(ProcedureScopes.WholeMouth, null, null, null)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Permanent, "16", null)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Primary, "55", null)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Both, "55", null)]
    [InlineData(ProcedureScopes.ToothSurface, ProcedureDentitions.Both, "16", "O")]
    [InlineData(ProcedureScopes.ToothSurface, ProcedureDentitions.Both, "11", "I")]
    public async Task A_tooth_and_surface_that_fit_are_accepted(string scope, string? dentition, string? tooth, string? surface)
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync("SITE-2", scope, dentition);
        var plan = await CreateAsync([Item(diagnosis, procedure.Summary.Id, tooth, surface)]);
        Assert.Equal((tooth, surface), (plan.Items[0].ToothKey, plan.Items[0].Surface));
    }

    [Fact]
    public async Task The_title_must_be_present_short_enough_and_free_of_control_characters()
    {
        var (_, diagnosis, procedure) = await SimplePlanAsync();
        IEnumerable<PlanItemInput> items() => [Item(diagnosis, procedure.Summary.Id, key: null)];
        Assert.Contains("title is required", (await Refused(() => CreateAsync(items(), "   "))).FieldErrors["title"]);
        Assert.Contains("120", (await Refused(() => CreateAsync(items(), new string('x', 121)))).FieldErrors["title"]);
        Assert.Contains("control", (await Refused(() => CreateAsync(items(), "Bad\u0007title"))).FieldErrors["title"]);
        Assert.Equal(120, (await CreateAsync(items(), new string('x', 120))).Title.Length);
    }

    [Fact]
    public async Task A_plan_needs_at_least_one_procedure_and_at_most_fifty_to_start_with()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        Assert.Contains("at least one", (await Refused(() => CreateAsync([]))).FieldErrors["items"]);
        var tooMany = Enumerable.Range(0, 51).Select(_ => Item(diagnosis, procedure.Summary.Id));
        Assert.Contains("at most 50", (await Refused(() => CreateAsync(tooMany))).FieldErrors["items"]);
        Assert.Equal((0, 0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task The_same_procedure_for_the_same_diagnosis_and_place_twice_in_one_plan_is_refused_but_a_different_place_is_fine()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync("TOOTH-2", ProcedureScopes.Tooth, ProcedureDentitions.Both);
        var e = await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id, "16"), Item(diagnosis, procedure.Summary.Id, "16")]));
        Assert.Contains("same procedure", e.FieldErrors["items[1]"]);
        var ok = await CreateAsync([Item(diagnosis, procedure.Summary.Id, "16"), Item(diagnosis, procedure.Summary.Id, "17")]);
        Assert.Equal(2, ok.ActiveItemCount);
    }

    [Fact]
    public async Task An_unknown_patient_is_a_404_and_stores_nothing()
    {
        var (_, diagnosis, procedure) = await SimplePlanAsync();
        var e = await Refused(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)], patient: Guid.NewGuid()));
        Assert.Equal(("patient_not_found", 404), (e.Code, e.StatusCode));
        Assert.Equal(1, (await CountsAsync()).Plans);                                          // only the plan SimplePlanAsync made
    }

    // ---------- the fee is a snapshot ----------

    [Fact]
    public async Task A_later_catalog_fee_change_never_alters_the_plan_and_new_items_use_the_new_fee_and_version()
    {
        var (plan, _, procedure) = await SimplePlanAsync(fee: 50m);
        var revised = await Catalog(s => s.ReviseAsync(procedure.Summary.Id,
            new ProcedureInput(ProcedureCodeSystems.Local, "LOCAL-100", "Periodontal maintenance", "Periodontic", ProcedureScopes.WholeMouth, null, 80m, null, null, null, null), "Annual fee review", procedure.Summary.RowVersion, S.Actor, default));

        var unchanged = await Svc(s => s.GetAsync(plan.Id, default));
        Assert.Equal((50m, 1, plan.Items[0].ProcedureVersionId), (unchanged.Items[0].Fee, unchanged.Items[0].ProcedureVersionNumber, unchanged.Items[0].ProcedureVersionId));
        Assert.Equal(50m, unchanged.EstimateTotal);

        var another = await DiagnosisAsync(label: "Gingivitis");
        var more = await Svc(s => s.AddItemAsync(plan.Id, Item(another, procedure.Summary.Id, key: "second"), unchanged.RowVersion, S.Actor, default));
        Assert.Equal((80m, 2), (more.Items[1].Fee, more.Items[1].ProcedureVersionNumber));
        Assert.Equal(revised.Versions[1].VersionId, more.Items[1].ProcedureVersionId);
        Assert.Equal(130m, more.EstimateTotal);
    }

    // ---------- repeats are quiet; simultaneous saves make one plan ----------

    [Fact]
    public async Task Saving_the_same_plan_twice_with_the_same_key_makes_one_plan_with_one_log()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var first = await CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "visit-1");
        var again = await CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "visit-1");
        Assert.Equal(first.Id, again.Id);
        Assert.Equal((1, 1, 2, 2), await CountsAsync());                                       // one plan, one item, Created + ItemAdded events, two audit entries
    }

    [Fact]
    public async Task A_retry_still_returns_the_plan_after_the_procedure_has_been_inactivated()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var first = await CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "visit-2");
        await Catalog(s => s.InactivateAsync(procedure.Summary.Id, "Retired", false, procedure.Summary.RowVersion, S.Actor, default));
        var retry = await CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "visit-2");
        Assert.Equal(first.Id, retry.Id);                                                      // the retry of a save that worked is not judged again
    }

    [Fact]
    public async Task Simultaneous_saves_with_one_key_end_with_one_plan()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => CreateAsync([Item(diagnosis, procedure.Summary.Id)], key: "race-1"))));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal((1, 1, 2, 2), await CountsAsync());
    }

    [Fact]
    public async Task The_same_key_for_two_different_patients_makes_two_plans()
    {
        var annDiagnosis = await DiagnosisAsync(Ann);
        var boDiagnosis = await DiagnosisAsync(Bo);
        var procedure = await ProcedureAsync();
        var a = await CreateAsync([Item(annDiagnosis, procedure.Summary.Id)], key: "shared", patient: Ann);
        var b = await CreateAsync([Item(boDiagnosis, procedure.Summary.Id)], key: "shared", patient: Bo);
        Assert.NotEqual(a.Id, b.Id);
    }
}
