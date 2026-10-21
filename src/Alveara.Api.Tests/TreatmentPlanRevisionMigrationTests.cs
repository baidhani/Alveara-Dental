using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-015-C01 migration check: a plan that exists before the revision tables do gets its first revision (its content as it stands) when the migration runs, with its procedures (the withdrawn one marked),
/// revision number 1 and no acceptance state; rolling back removes only what the migration added and keeps the plan, its procedures and its history.
/// </summary>
[RealMigrations]
public class TreatmentPlanRevisionMigrationTests : TreatmentPlanTestBase
{
    private const string Before = "20261008164128_AddTreatmentPlans";

    private async Task<bool> TableExistsAsync(string name)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {name}").AnyAsync(x => x > 0);
    }

    [Fact]
    public async Task Rolling_back_keeps_the_plan_and_rolling_forward_gives_it_one_revision_holding_its_procedures()
    {
        var diagnosis = await DiagnosisAsync();
        var first = await ProcedureAsync("LOCAL-100");
        var second = await ProcedureAsync("LOCAL-200", fee: 70m);
        var plan = await CreateAsync([Item(diagnosis, first.Summary.Id), Item(diagnosis, second.Summary.Id)], title: "Gum health plan");
        var withdrawn = plan.Items[1];
        plan = await Svc(s => s.WithdrawItemAsync(plan.Id, withdrawn.Id, "Patient declined", plan.RowVersion, S.Actor, default));
        var (plans, items, events, _) = await CountsAsync();

        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);
        Assert.False(await TableExistsAsync("TreatmentPlanRevisions"));
        Assert.False(await TableExistsAsync("TreatmentPlanItemStates"));
        Assert.False(await TableExistsAsync("DiagnosisPlanResolutions"));
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal((plans, items, events), (await db.TreatmentPlans.CountAsync(), await db.TreatmentPlanItems.CountAsync(), await db.TreatmentPlanEvents.CountAsync()));    // nothing of the plan was lost
        }

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using var check = Fixture.CreateContext();
        var revisions = await check.TreatmentPlanRevisions.AsNoTracking().Where(r => r.PlanId == plan.Id).ToListAsync();
        var revision = Assert.Single(revisions);
        Assert.Equal((1, "Revised", "Gum health plan"), (revision.RevisionNumber, revision.ChangeType, revision.Title));
        Assert.Equal(await check.TreatmentPlanEvents.Where(e => e.PlanId == plan.Id).MaxAsync(e => e.EventNumber), revision.EventNumber);
        var held = await check.TreatmentPlanRevisionItems.AsNoTracking().Where(i => i.RevisionId == revision.Id).ToListAsync();
        Assert.Equal(2, held.Count);
        Assert.Equal([false, true], held.OrderBy(h => h.ItemId == withdrawn.Id).Select(h => h.IsWithdrawn).ToArray());
        Assert.All(held, h => Assert.Null(h.PhaseNumber));
        Assert.Equal(1, (await check.TreatmentPlans.AsNoTracking().SingleAsync(p => p.Id == plan.Id)).CurrentRevision);
        Assert.Equal(0, await check.TreatmentPlanItemStates.CountAsync());
    }
}
