using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-015 against real SQL Server: the database refuses, on its own, what the service refuses and what no service rule can see. These tests go around the service on purpose - a script, a future
/// module or a person with database access cannot delete a plan or an item, edit an item, rewrite the history, change a plan's patient, propose a procedure for another patient's or a withdrawn
/// diagnosis, for an inactive procedure, with a fee that is not the catalog version's, or on a tooth that does not fit, or add anything to a withdrawn plan.
/// </summary>
public class TreatmentPlanSchemaTests : TreatmentPlanTestBase
{
    private static async Task<SqlException> Sql(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private TreatmentPlan NewPlan(Guid? patient = null, string key = "plan-1", string title = "Plan") =>
        new() { Id = Guid.NewGuid(), PatientId = patient ?? Ann, IdempotencyKey = key, Title = title, Status = TreatmentPlanStatuses.Proposed, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };

    private TreatmentPlanItem NewItem(TreatmentPlan plan, Guid diagnosis, ProcedureDetailView procedure, int number = 1, string key = "item-1") =>
        new()
        {
            Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = plan.PatientId, ItemNumber = number, IdempotencyKey = key, DiagnosisId = diagnosis, ProcedureId = procedure.Summary.Id,
            ProcedureVersionId = procedure.Versions[0].VersionId, Fee = procedure.Versions[0].Fee, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor,
        };

    private async Task<TreatmentPlan> InsertPlanAsync(Action<TreatmentPlan>? tweak = null, Guid? patient = null, string key = "plan-1")
    {
        var plan = NewPlan(patient, key);
        tweak?.Invoke(plan);
        await using var db = Fixture.CreateContext();
        db.TreatmentPlans.Add(plan);
        await db.SaveChangesAsync();
        return plan;
    }

    private async Task<TreatmentPlanItem> InsertItemAsync(TreatmentPlan plan, Guid diagnosis, ProcedureDetailView procedure, Action<TreatmentPlanItem>? tweak = null, int number = 1, string key = "item-1")
    {
        var item = NewItem(plan, diagnosis, procedure, number, key);
        tweak?.Invoke(item);
        await using var db = Fixture.CreateContext();
        db.TreatmentPlanItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private async Task<int> RunAsync(string sql, params object[] args)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    private async Task<(TreatmentPlan Plan, TreatmentPlanItem Item, Guid Diagnosis, ProcedureDetailView Procedure)> ArrangedAsync()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var plan = await InsertPlanAsync();
        var item = await InsertItemAsync(plan, diagnosis, procedure);
        return (plan, item, diagnosis, procedure);
    }

    // ---------- check constraints: a plan ----------

    [Theory]
    [InlineData("blankKey", "CK_TreatmentPlans_Key")]
    [InlineData("blankTitle", "CK_TreatmentPlans_Title")]
    [InlineData("controlTitle", "CK_TreatmentPlans_TitleLine")]
    [InlineData("badStatus", "CK_TreatmentPlans_Status")]
    [InlineData("withdrawnNoStamp", "CK_TreatmentPlans_WithdrawnStamp")]
    [InlineData("proposedWithStamp", "CK_TreatmentPlans_WithdrawnStamp")]
    [InlineData("withdrawnBlankReason", "CK_TreatmentPlans_WithdrawnStamp")]
    public async Task The_database_refuses_a_plan_that_breaks_a_shape_or_status_rule(string which, string constraint)
    {
        var e = await Sql(() => InsertPlanAsync(p =>
        {
            switch (which)
            {
                case "blankKey": p.IdempotencyKey = "   "; break;
                case "blankTitle": p.Title = "   "; break;
                case "controlTitle": p.Title = "Bad\u0007title"; break;
                case "badStatus": p.Status = "Accepted"; break;
                case "withdrawnNoStamp": p.Status = TreatmentPlanStatuses.Withdrawn; break;
                case "proposedWithStamp": (p.WithdrawnAtUtc, p.WithdrawnByUserId, p.WithdrawnReason) = (DateTimeOffset.UtcNow, S.Actor, "x"); break;
                case "withdrawnBlankReason": (p.Status, p.WithdrawnAtUtc, p.WithdrawnByUserId, p.WithdrawnReason) = (TreatmentPlanStatuses.Withdrawn, DateTimeOffset.UtcNow, S.Actor, "  "); break;
            }
        }));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task The_database_refuses_the_same_save_key_twice_for_one_patient_but_allows_it_for_another()
    {
        await InsertPlanAsync(key: "same");
        Assert.Contains((await Sql(() => InsertPlanAsync(key: "same"))).Number, new[] { 2601, 2627 });
        await InsertPlanAsync(patient: Bo, key: "same");
    }

    // ---------- check constraints: an item ----------

    [Theory]
    [InlineData("number", "CK_TreatmentPlanItems_Number")]
    [InlineData("blankKey", "CK_TreatmentPlanItems_Key")]
    [InlineData("negativeFee", "CK_TreatmentPlanItems_Fee")]
    [InlineData("hugeFee", "CK_TreatmentPlanItems_Fee")]
    [InlineData("badTooth", "CK_TreatmentPlanItems_ToothKey")]
    [InlineData("surfaceWithoutTooth", "CK_TreatmentPlanItems_Surface")]
    [InlineData("badSurface", "CK_TreatmentPlanItems_Surface")]
    [InlineData("partialWithdrawal", "CK_TreatmentPlanItems_WithdrawnStamp")]
    [InlineData("blankWithdrawalReason", "CK_TreatmentPlanItems_WithdrawnStamp")]
    public async Task The_database_refuses_an_item_that_breaks_a_shape_rule(string which, string constraint)
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var plan = await InsertPlanAsync();
        var e = await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i =>
        {
            switch (which)
            {
                case "number": i.ItemNumber = 0; break;
                case "blankKey": i.IdempotencyKey = "  "; break;
                case "negativeFee": i.Fee = -1m; break;
                case "hugeFee": i.Fee = 1_000_000.01m; break;
                case "badTooth": i.ToothKey = "99"; break;
                case "surfaceWithoutTooth": i.Surface = "O"; break;
                case "badSurface": (i.ToothKey, i.Surface) = ("16", "Z"); break;
                case "partialWithdrawal": i.WithdrawnReason = "x"; break;
                case "blankWithdrawalReason": (i.WithdrawnAtUtc, i.WithdrawnByUserId, i.WithdrawnReason) = (DateTimeOffset.UtcNow, S.Actor, " "); break;
            }
        }));
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task The_database_refuses_a_duplicate_item_number_or_save_key_in_one_plan()
    {
        var (plan, _, diagnosis, procedure) = await ArrangedAsync();
        Assert.Contains((await Sql(() => InsertItemAsync(plan, diagnosis, procedure, number: 1, key: "other"))).Number, new[] { 2601, 2627 });
        Assert.Contains((await Sql(() => InsertItemAsync(plan, diagnosis, procedure, number: 2, key: "item-1"))).Number, new[] { 2601, 2627 });
    }

    [Fact]
    public async Task The_database_refuses_an_item_for_a_diagnosis_or_procedure_that_does_not_exist()
    {
        var (plan, _, diagnosis, procedure) = await ArrangedAsync();
        Assert.Equal(547, (await Sql(() => InsertItemAsync(plan, Guid.NewGuid(), procedure, number: 2, key: "a"))).Number);
        Assert.Equal(547, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i => i.ProcedureId = Guid.NewGuid(), 2, "b"))).Number);
        Assert.Equal(547, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i => i.ProcedureVersionId = Guid.NewGuid(), 2, "c"))).Number);
    }

    // ---------- check constraints: the history ----------

    [Theory]
    [InlineData("changeType", "CK_TreatmentPlanEvents_ChangeType")]
    [InlineData("number", "CK_TreatmentPlanEvents_Number")]
    [InlineData("itemAddedWithoutItem", "CK_TreatmentPlanEvents_Item")]
    [InlineData("createdWithItem", "CK_TreatmentPlanEvents_Item")]
    [InlineData("createdWithoutTitle", "CK_TreatmentPlanEvents_Title")]
    [InlineData("withdrawnWithTitle", "CK_TreatmentPlanEvents_Title")]
    [InlineData("itemWithdrawnNoReason", "CK_TreatmentPlanEvents_Reason")]
    [InlineData("withdrawnNoReason", "CK_TreatmentPlanEvents_Reason")]
    public async Task The_database_refuses_a_history_entry_that_breaks_a_shape_rule(string which, string constraint)
    {
        var (plan, item, _, _) = await ArrangedAsync();
        var ev = new TreatmentPlanEvent { Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = Ann, EventNumber = 90, ChangeType = "Renamed", Title = "New title", ActorUserId = S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow };
        switch (which)
        {
            case "changeType": ev.ChangeType = "Accepted"; break;
            case "number": ev.EventNumber = 0; break;
            case "itemAddedWithoutItem": (ev.ChangeType, ev.Title) = ("ItemAdded", null); break;
            case "createdWithItem": (ev.ChangeType, ev.ItemId) = ("Created", item.Id); break;
            case "createdWithoutTitle": (ev.ChangeType, ev.Title) = ("Created", null); break;
            case "withdrawnWithTitle": (ev.ChangeType, ev.Reason) = ("Withdrawn", "Because"); break;       // keeps Title "New title", which a Withdrawn event may not carry
            case "itemWithdrawnNoReason": (ev.ChangeType, ev.ItemId, ev.Title) = ("ItemWithdrawn", item.Id, null); break;
            case "withdrawnNoReason": (ev.ChangeType, ev.Title) = ("Withdrawn", null); break;
        }
        await using var db = Fixture.CreateContext();
        db.TreatmentPlanEvents.Add(ev);
        var e = await Sql(() => db.SaveChangesAsync());
        Assert.Contains(constraint, e.Message);
    }

    // ---------- triggers: nothing is deleted, nothing is rewritten ----------

    [Fact]
    public async Task A_plan_cannot_be_deleted()
    {
        var (plan, _, _, _) = await ArrangedAsync();
        Assert.Equal(51085, (await Sql(() => RunAsync("DELETE FROM TreatmentPlans WHERE Id = {0}", plan.Id))).Number);
    }

    [Fact]
    public async Task A_plans_patient_and_origin_cannot_change_but_its_title_can()
    {
        var (plan, _, _, _) = await ArrangedAsync();
        Assert.Equal(51086, (await Sql(() => RunAsync("UPDATE TreatmentPlans SET PatientId = {0} WHERE Id = {1}", Bo, plan.Id))).Number);
        Assert.Equal(51086, (await Sql(() => RunAsync("UPDATE TreatmentPlans SET IdempotencyKey = 'other' WHERE Id = {0}", plan.Id))).Number);
        Assert.Equal(51086, (await Sql(() => RunAsync("UPDATE TreatmentPlans SET CreatedByUserId = {0} WHERE Id = {1}", Other, plan.Id))).Number);
        Assert.Equal(1, await RunAsync("UPDATE TreatmentPlans SET Title = 'Renamed' WHERE Id = {0}", plan.Id));
    }

    [Fact]
    public async Task A_withdrawn_plan_cannot_be_renamed_or_brought_back()
    {
        var plan = await InsertPlanAsync(p => (p.Status, p.WithdrawnAtUtc, p.WithdrawnByUserId, p.WithdrawnReason) = (TreatmentPlanStatuses.Withdrawn, DateTimeOffset.UtcNow, S.Actor, "Not going ahead"));
        Assert.Equal(51087, (await Sql(() => RunAsync("UPDATE TreatmentPlans SET Title = 'Renamed' WHERE Id = {0}", plan.Id))).Number);
        Assert.Equal(51087, (await Sql(() => RunAsync("UPDATE TreatmentPlans SET Status = 'Proposed', WithdrawnAtUtc = NULL, WithdrawnByUserId = NULL, WithdrawnReason = NULL WHERE Id = {0}", plan.Id))).Number);
    }

    [Fact]
    public async Task An_item_cannot_be_deleted()
    {
        var (_, item, _, _) = await ArrangedAsync();
        Assert.Equal(51088, (await Sql(() => RunAsync("DELETE FROM TreatmentPlanItems WHERE Id = {0}", item.Id))).Number);
    }

    [Theory]
    [InlineData("Fee = 1")]
    [InlineData("ItemNumber = 9")]
    [InlineData("ToothKey = '16'")]
    [InlineData("DiagnosisId = NEWID()")]
    [InlineData("IdempotencyKey = 'changed'")]
    public async Task An_item_is_never_edited(string assignment)
    {
        var (_, item, _, _) = await ArrangedAsync();
        var e = await Sql(() => RunAsync($"UPDATE TreatmentPlanItems SET {assignment} WHERE Id = {{0}}", item.Id));
        Assert.Contains(e.Number, new[] { 51089, 547 });                  // an edited diagnosis that does not exist is refused by the foreign key first; every other edit by the trigger
        await using var db = Fixture.CreateContext();
        Assert.Equal(item.Fee, await db.TreatmentPlanItems.Where(i => i.Id == item.Id).Select(i => i.Fee).SingleAsync());
    }

    [Fact]
    public async Task An_item_can_be_withdrawn_once_and_the_withdrawal_never_changes()
    {
        var (_, item, _, _) = await ArrangedAsync();
        Assert.Equal(1, await RunAsync("UPDATE TreatmentPlanItems SET WithdrawnAtUtc = SYSUTCDATETIME(), WithdrawnByUserId = {0}, WithdrawnReason = 'Wrong tooth' WHERE Id = {1}", S.Actor, item.Id));
        Assert.Equal(51090, (await Sql(() => RunAsync("UPDATE TreatmentPlanItems SET WithdrawnReason = 'Other words' WHERE Id = {0}", item.Id))).Number);
        Assert.Equal(51090, (await Sql(() => RunAsync("UPDATE TreatmentPlanItems SET WithdrawnAtUtc = NULL, WithdrawnByUserId = NULL, WithdrawnReason = NULL WHERE Id = {0}", item.Id))).Number);
    }

    [Fact]
    public async Task A_history_entry_cannot_be_changed_or_removed()
    {
        var (plan, _, _, _) = await ArrangedAsync();
        await using (var db = Fixture.CreateContext())
        {
            db.TreatmentPlanEvents.Add(new TreatmentPlanEvent { Id = Guid.NewGuid(), PlanId = plan.Id, PatientId = Ann, EventNumber = 1, ChangeType = "Created", Title = "Plan", ActorUserId = S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
        }
        Assert.Equal(51091, (await Sql(() => RunAsync("UPDATE TreatmentPlanEvents SET Title = 'Rewritten' WHERE PlanId = {0}", plan.Id))).Number);
        Assert.Equal(51091, (await Sql(() => RunAsync("DELETE FROM TreatmentPlanEvents WHERE PlanId = {0}", plan.Id))).Number);
    }

    // ---------- triggers: what an item may refer to ----------

    [Fact]
    public async Task An_item_must_carry_the_same_patient_as_its_plan()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var plan = await InsertPlanAsync();
        Assert.Equal(51092, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i => i.PatientId = Bo))).Number);
    }

    [Fact]
    public async Task Nothing_can_be_added_to_a_withdrawn_plan()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        var plan = await InsertPlanAsync(p => (p.Status, p.WithdrawnAtUtc, p.WithdrawnByUserId, p.WithdrawnReason) = (TreatmentPlanStatuses.Withdrawn, DateTimeOffset.UtcNow, S.Actor, "Not going ahead"));
        Assert.Equal(51093, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure))).Number);
    }

    [Fact]
    public async Task An_items_diagnosis_must_be_the_same_patients_and_not_withdrawn()
    {
        var bosDiagnosis = await DiagnosisAsync(Bo);
        var withdrawn = await DiagnosisAsync();
        await WithdrawDiagnosisAsync(withdrawn);
        var procedure = await ProcedureAsync();
        var plan = await InsertPlanAsync();
        Assert.Equal(51094, (await Sql(() => InsertItemAsync(plan, bosDiagnosis, procedure))).Number);
        Assert.Equal(51095, (await Sql(() => InsertItemAsync(plan, withdrawn, procedure))).Number);
    }

    [Fact]
    public async Task An_items_fee_and_procedure_must_be_those_of_the_catalog_version_it_names()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync("PERIO-1", fee: 50m);
        var other = await ProcedureAsync("PERIO-2", fee: 70m);
        var plan = await InsertPlanAsync();
        Assert.Equal(51096, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i => i.Fee = 49.99m))).Number);                  // a fee that is not the version's
        Assert.Equal(51096, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i => i.ProcedureId = other.Summary.Id, 1, "b"))).Number);   // a version that belongs to another procedure
        await using (var db = Fixture.CreateContext()) Assert.Equal(0, await db.TreatmentPlanItems.CountAsync());
        await InsertItemAsync(plan, diagnosis, procedure, key: "c");                                                                       // the honest copy is accepted
    }

    [Fact]
    public async Task An_inactive_procedure_cannot_be_added_even_under_its_old_version()
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync();
        await Catalog(s => s.InactivateAsync(procedure.Summary.Id, "Retired", false, procedure.Summary.RowVersion, S.Actor, default));
        var plan = await InsertPlanAsync();
        Assert.Equal(51097, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure))).Number);
    }

    // ---------- triggers: the tooth and surface must fit the procedure ----------

    [Theory]
    [InlineData(ProcedureScopes.WholeMouth, ProcedureDentitions.Both, "16", null, 51098)]
    [InlineData(ProcedureScopes.Arch, ProcedureDentitions.Both, null, "O", 547)]               // a surface without a tooth is refused by the check first
    [InlineData(ProcedureScopes.Quadrant, ProcedureDentitions.Both, "16", "O", 51098)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Both, null, null, 51098)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Both, "16", "O", 51098)]
    [InlineData(ProcedureScopes.ToothSurface, ProcedureDentitions.Both, "16", null, 51098)]
    [InlineData(ProcedureScopes.ToothSurface, ProcedureDentitions.Both, null, null, 51098)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Permanent, "55", null, 51098)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Primary, "16", null, 51098)]
    [InlineData(ProcedureScopes.WholeMouth, ProcedureDentitions.Both, null, null, 0)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Permanent, "16", null, 0)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Primary, "85", null, 0)]
    [InlineData(ProcedureScopes.Tooth, ProcedureDentitions.Both, "55", null, 0)]
    [InlineData(ProcedureScopes.ToothSurface, ProcedureDentitions.Both, "16", "O", 0)]
    public async Task The_database_judges_where_an_item_is_done_against_what_the_procedure_applies_to(string scope, string dentition, string? tooth, string? surface, int expectedError)
    {
        var diagnosis = await DiagnosisAsync();
        var procedure = await ProcedureAsync("SITE-1", scope, dentition);
        var plan = await InsertPlanAsync();
        if (expectedError == 0)
        {
            await InsertItemAsync(plan, diagnosis, procedure, i => (i.ToothKey, i.Surface) = (tooth, surface));
            return;
        }
        Assert.Equal(expectedError, (await Sql(() => InsertItemAsync(plan, diagnosis, procedure, i => (i.ToothKey, i.Surface) = (tooth, surface)))).Number);
    }
}
