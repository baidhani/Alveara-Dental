using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2, migration M5 (F-052, owner decision 2026-11-15), the database on its own: a blocked time's calendar label is one of the controlled codes (case-sensitive) or null;
/// upgrading leaves every earlier block exactly as it was with no label; M5 rolls back and forward while no label is recorded and refuses (51286) once one is, leaving it in place.
/// </summary>
public class BlockedTimeCalendarLabelMigrationTests : SafetyTestBase
{
    private const string BeforeM5 = "20261116005444_AddAppointmentMoveDestination";

    private async Task SqlAsync(string sql, params object[] args)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    /// <summary>A block written with the columns every version of the table has; returns its id.</summary>
    private async Task<Guid> BlockAsync(string reason = "Staff lunch")
    {
        var id = Guid.NewGuid();
        await SqlAsync(@"INSERT INTO [ProviderBlockedTimes] ([Id], [ProviderProfileId], [StartUtc], [EndUtc], [Reason], [CreatedAtUtc])
                         VALUES ({0}, {1}, '2030-01-14T18:00:00+00:00', '2030-01-14T19:00:00+00:00', {2}, SYSUTCDATETIME())", id, S.ProviderA, reason);
        return id;
    }

    private Task LabelAsync(Guid id, string label) => SqlAsync("UPDATE [ProviderBlockedTimes] SET [CalendarLabel] = {0} WHERE [Id] = {1}", label, id);

    [Theory]
    [InlineData("lunch")]
    [InlineData("meeting")]
    [InlineData("training")]
    [InlineData("unavailable")]
    [InlineData("other")]
    public async Task Each_controlled_label_is_accepted(string label) => await LabelAsync(await BlockAsync(), label);

    [Theory]
    [InlineData("Lunch")]          // case-sensitive
    [InlineData("coffee")]
    [InlineData("")]
    public async Task Any_other_label_is_refused(string label)
    {
        var e = await Refusal(async () => await LabelAsync(await BlockAsync(), label));
        Assert.True(e.Number == 547 && e.Message.Contains("CK_ProviderBlockedTimes_CalendarLabel"), $"{e.Number}: {e.Message}");
    }

    [Fact]
    public async Task Upgrading_leaves_every_earlier_block_unchanged_and_without_a_label()
    {
        await MigrateToAsync(BeforeM5);
        var earlier = await BlockAsync("Staff lunch");
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();

        await using var check = Fixture.CreateContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        var row = await check.ProviderBlockedTimes.AsNoTracking().SingleAsync(b => b.Id == earlier);
        Assert.Equal(("Staff lunch", (string?)null, S.ProviderA), (row.Reason, row.CalendarLabel, row.ProviderProfileId));
    }

    [Fact]
    public async Task M5_rolls_back_and_forward_while_no_label_is_recorded()
    {
        await BlockAsync();
        await MigrateToAsync(BeforeM5);
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await LabelAsync(await BlockAsync(), "meeting");   // the restored schema works
    }

    [Fact]
    public async Task M5_refuses_to_roll_back_over_a_recorded_label()
    {
        var id = await BlockAsync();
        await LabelAsync(id, "lunch");
        Assert.Equal(51286, (await Refusal(() => MigrateToAsync(BeforeM5))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("lunch", (await db.ProviderBlockedTimes.AsNoTracking().SingleAsync(b => b.Id == id)).CalendarLabel);
    }
}
