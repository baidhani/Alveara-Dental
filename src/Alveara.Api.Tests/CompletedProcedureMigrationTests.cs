using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Completion;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C01: the migration that adds the completion tables can be rolled back and forward again. Rolling back removes the three tables and their six triggers and keeps everything else (a plan with a
/// completed procedure stays); rolling forward restores them and the database again refuses a change to a completion row. A rollback is only possible while no completion has been recorded: after that the
/// tables hold authoritative history and are deliberately not dropped by a script (they are append-only), the same stance as the billing ledger.
/// </summary>
public class CompletedProcedureMigrationTests : CompletionTestBase
{
    private const string Before = "20261011155619_AddBillingLedger";
    private static readonly string[] Tables = ["CompletionSets", "CompletedProcedures", "CompletedProcedureAmendments"];
    private static readonly string[] Triggers =
    [
        "TR_CompletionSets_Immutable", "TR_CompletionSets_Links", "TR_CompletedProcedures_Immutable", "TR_CompletedProcedures_Links",
        "TR_CompletedProcedureAmendments_Immutable", "TR_CompletedProcedureAmendments_Links",
    ];

    private async Task<int> TableCountAsync(string name)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {name}").SingleAsync();
    }

    private async Task<int> TriggerCountAsync(string name)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = {name}").SingleAsync();
    }

    [Fact]
    public async Task Rolling_back_removes_the_completion_tables_and_triggers_keeps_the_plan_and_rolling_forward_restores_them()
    {
        var plan = await BillablePlanAsync();
        var counts = await CountsAsync();
        foreach (var t in Tables) Assert.Equal(1, await TableCountAsync(t));
        foreach (var t in Triggers) Assert.Equal(1, await TriggerCountAsync(t));

        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);
        foreach (var t in Tables) Assert.Equal(0, await TableCountAsync(t));
        foreach (var t in Triggers) Assert.Equal(0, await TriggerCountAsync(t));
        Assert.Equal(counts, await CountsAsync());                                                      // nothing of the plan was lost
        await using (var db = Fixture.CreateContext())
        {
            var completed = await db.TreatmentPlanItemStates.CountAsync(s => s.ItemId == plan.Completed && s.Completion == "Completed");
            Assert.Equal(1, completed);
        }

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        foreach (var t in Tables) Assert.Equal(1, await TableCountAsync(t));
        foreach (var t in Triggers) Assert.Equal(1, await TriggerCountAsync(t));
        Assert.Equal(counts, await CountsAsync());

        var provider = await ProviderAsync();
        var item = plan.Plan.Items.Single(i => i.Id == plan.Completed);
        var set = SetRow(plan.Plan, revision: plan.Plan.CurrentRevision);
        await AddRowsAsync(set, ProcedureRow(set, item, AnnEncounter, provider.Id));                    // the restored tables accept a completion...
        var e = await SqlRefusal(() => ExecuteAsync("DELETE FROM [CompletedProcedures]"));              // ...and refuse to lose it
        Assert.Equal(51145, e.Number);
        Assert.Equal((1, 1, 0), await CompletionCountsAsync());
    }
}
