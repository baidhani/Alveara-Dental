using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Treatment;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-008-C02 against real SQL Server, going around the service on purpose: the database itself refuses an adoption reason that is blank or holds a control character, an adoption of more than one procedure
/// (trigger 51172) and any change to a recorded adoption (the set's existing append-only trigger 51144); it accepts an ordinary set with no reason and a reason of the maximum length. The migration that adds the
/// column and the trigger can be rolled back and forward again with a completion in the database, and is refused (not silently lossy) while an adoption exists.
/// </summary>
[RealMigrations]
public class CompletionAdoptionSchemaTests : CompletionTestBase
{
    private const string Before = "20261012133723_AddRefundsReleasesAndDocuments";

    private async Task<PlanView> PlanAsync() => await AcceptedPlanAsync(2);

    private static CompletionSet Adopted(PlanView plan, string? reason, int count = 1, string key = "adopt-1")
    {
        var set = SetRow(plan, key, plan.CurrentRevision, count);
        set.AdoptionReason = reason;
        return set;
    }

    [Fact]
    public async Task An_ordinary_set_has_no_adoption_reason_and_may_hold_several_procedures_while_an_adoption_of_one_is_accepted()
    {
        var plan = await PlanAsync();
        await AddRowsAsync(SetRow(plan, "set-1", plan.CurrentRevision, count: 2));
        await AddRowsAsync(Adopted(plan, "Completed before procedure records existed"));
        await using var db = Fixture.CreateContext();
        Assert.Equal([null, "Completed before procedure records existed"], (await db.CompletionSets.AsNoTracking().OrderBy(s => s.IdempotencyKey).ToListAsync()).Select(s => s.AdoptionReason).OrderBy(r => r).ToArray());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData("line one\nline two")]
    [InlineData("tab\there")]
    public async Task A_blank_or_control_character_reason_is_refused_by_the_database(string reason)
    {
        var plan = await PlanAsync();
        var e = await SqlRefusal(() => AddRowsAsync(Adopted(plan, reason)));
        Assert.Equal(547, e.Number);                                                                    // CK_CompletionSets_AdoptionReason
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Fact]
    public async Task A_reason_of_the_maximum_length_is_accepted_and_one_character_more_is_not()
    {
        var plan = await PlanAsync();
        await AddRowsAsync(Adopted(plan, new string('r', CompletionLimits.ReasonMax)));
        await SqlRefusal(() => AddRowsAsync(Adopted(plan, new string('r', CompletionLimits.ReasonMax + 1), key: "adopt-2")));
        Assert.Equal((1, 0, 0), await CompletionCountsAsync());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task An_adoption_records_exactly_one_procedure(int count)
    {
        var plan = await PlanAsync();
        var e = await SqlRefusal(() => AddRowsAsync(Adopted(plan, "Completed before procedure records existed", count)));
        Assert.Equal(51172, e.Number);
        Assert.Equal((0, 0, 0), await CompletionCountsAsync());
    }

    [Fact]
    public async Task A_recorded_adoption_reason_can_be_neither_changed_nor_removed_and_the_set_cannot_be_deleted()
    {
        var plan = await PlanAsync();
        await AddRowsAsync(Adopted(plan, "Completed before procedure records existed"));
        Assert.Equal(51144, (await SqlRefusal(() => ExecuteAsync("UPDATE [CompletionSets] SET [AdoptionReason] = N'Rewritten'"))).Number);
        Assert.Equal(51144, (await SqlRefusal(() => ExecuteAsync("UPDATE [CompletionSets] SET [AdoptionReason] = NULL"))).Number);
        Assert.Equal(51144, (await SqlRefusal(() => ExecuteAsync("DELETE FROM [CompletionSets]"))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("Completed before procedure records existed", (await db.CompletionSets.AsNoTracking().SingleAsync()).AdoptionReason);
    }

    // ---------- the migration ----------

    private async Task<int> CountAsync(string sql)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
    }

    private Task<int> ColumnAsync() => CountAsync("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('CompletionSets') AND name = 'AdoptionReason'");
    private Task<int> TriggerAsync() => CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_CompletionSets_Adoption'");

    [Fact]
    public async Task Rolling_back_removes_the_column_and_trigger_keeps_the_completions_and_rolling_forward_restores_them()
    {
        var plan = await PlanAsync();
        var provider = await ProviderAsync();
        var set = SetRow(plan, "set-1", plan.CurrentRevision);
        await AddRowsAsync(set);
        Assert.Equal((1, 1), (await ColumnAsync(), await TriggerAsync()));

        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);
        Assert.Equal((0, 0), (await ColumnAsync(), await TriggerAsync()));
        Assert.Equal((1, 0, 0), await CompletionCountsAsync());                                         // the ordinary completion set is still there

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        Assert.Equal((1, 1), (await ColumnAsync(), await TriggerAsync()));
        var e = await SqlRefusal(() => AddRowsAsync(Adopted(plan, "Completed before procedure records existed", count: 2)));
        Assert.Equal(51172, e.Number);                                                                  // the restored trigger refuses again
        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Rolling_back_is_refused_while_an_adoption_exists_and_changes_nothing()
    {
        var plan = await PlanAsync();
        await AddRowsAsync(Adopted(plan, "Completed before procedure records existed"));
        var refused = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await using var db = Fixture.CreateContext();
            await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(Before);
        });
        Assert.Contains("adopted completion exists", refused.ToString());
        Assert.Equal((1, 1), (await ColumnAsync(), await TriggerAsync()));                              // the whole step was rolled back
        await using var check = Fixture.CreateContext();
        Assert.Equal("Completed before procedure records existed", (await check.CompletionSets.AsNoTracking().SingleAsync()).AdoptionReason);
    }
}
