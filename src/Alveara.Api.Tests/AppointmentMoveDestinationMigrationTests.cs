using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Scheduling;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2, migration M4 (owner decision 2026-11-15), the database on its own: rows are written straight to the table, around any service.
/// - A destination id always comes with a non-blank name, and only Rescheduled, ProviderReassigned and RoomChanged may carry a destination.
/// - Upgrading over existing history leaves every earlier row exactly as it was, with no destination.
/// - M4 rolls back and forward on an empty destination set and refuses to roll back (51285) once a destination is recorded, leaving it in place.
/// </summary>
public class AppointmentMoveDestinationMigrationTests : SafetyTestBase
{
    private const string BeforeM4 = "20261114170534_AddVisitCheckoutOutcomes";

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

    private static void Constraint(Microsoft.Data.SqlClient.SqlException e, string name) =>
        Assert.True(e.Number == 547 && e.Message.Contains(name), $"expected {name}, got {e.Number}: {e.Message}");

    private async Task MigrateToAsync(string target)
    {
        await using var db = Fixture.CreateContext();
        await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private async Task<Guid> AppointmentAsync() => (await S.ScheduleAsync(S.Request(Ann, At(9)))).Appointment.Id;

    /// <summary>Inserts one event with the columns every version of the table has; returns its id.</summary>
    private async Task<Guid> EventAsync(Guid appointment, string type)
    {
        var id = Guid.NewGuid();
        await SqlAsync(@"INSERT INTO [AppointmentEvents] ([Id], [AppointmentId], [EventType], [ActorUserId], [OccurredAtUtc], [PreviousStartUtc], [PreviousProviderProfileId], [PreviousOperatoryId], [Detail])
                         VALUES ({0}, {1}, {2}, NULL, '2030-01-14T13:00:00+00:00', '2030-01-14T15:00:00+00:00', {3}, {4}, N'Rescheduled for 60 minutes.')", id, appointment, type, S.ProviderA, S.Op1);
        return id;
    }

    private Task SetAsync(Guid eventId, string assignments, params object[] args) =>
        SqlAsync($"UPDATE [AppointmentEvents] SET {assignments} WHERE [Id] = {{{args.Length}}}", [.. args, eventId]);

    // ---------- the rules ----------

    [Theory]
    [InlineData("Rescheduled")]
    [InlineData("ProviderReassigned")]
    [InlineData("RoomChanged")]
    public async Task Each_move_event_takes_a_destination(string type)
    {
        var e = await EventAsync(await AppointmentAsync(), type);
        await SetAsync(e, "[NewStartUtc] = SYSUTCDATETIME(), [NewProviderProfileId] = {0}, [NewProviderName] = N'Dr. Patel', [NewOperatoryId] = {1}, [NewOperatoryName] = N'Op 2'", S.ProviderB, S.Op2);
    }

    [Theory]
    [InlineData("Scheduled")]
    [InlineData("Cancelled")]
    [InlineData("CheckedIn")]
    public async Task Any_other_event_is_refused_a_destination(string type)
    {
        var e = await EventAsync(await AppointmentAsync(), type);
        Constraint(await Refusal(() => SetAsync(e, "[NewStartUtc] = SYSUTCDATETIME()")), "CK_AppointmentEvents_Destination");
        Constraint(await Refusal(() => SetAsync(e, "[NewOperatoryId] = {0}, [NewOperatoryName] = N'Op 2'", S.Op2)), "CK_AppointmentEvents_Destination");
    }

    [Theory]
    [InlineData("[NewProviderProfileId] = {0}", "CK_AppointmentEvents_NewProvider")]                                   // an id without its name
    [InlineData("[NewProviderProfileId] = {0}, [NewProviderName] = N'   '", "CK_AppointmentEvents_NewProvider")]       // a blank name
    [InlineData("[NewProviderName] = N'Dr. Patel'", "CK_AppointmentEvents_NewProvider")]                               // a name without its id
    [InlineData("[NewOperatoryId] = {0}", "CK_AppointmentEvents_NewOperatory")]
    [InlineData("[NewOperatoryId] = {0}, [NewOperatoryName] = N''", "CK_AppointmentEvents_NewOperatory")]
    [InlineData("[NewOperatoryName] = N'Op 2'", "CK_AppointmentEvents_NewOperatory")]
    public async Task A_destination_id_always_comes_with_a_non_blank_name(string assignments, string constraint)
    {
        var e = await EventAsync(await AppointmentAsync(), AppointmentEventTypes.Rescheduled);
        Constraint(await Refusal(() => SetAsync(e, assignments, Guid.NewGuid())), constraint);
    }

    // ---------- upgrade, rollback and forward ----------

    [Fact]
    public async Task Upgrading_over_existing_history_leaves_every_earlier_row_unchanged_and_without_a_destination()
    {
        var appointment = await AppointmentAsync();
        await MigrateToAsync(BeforeM4);
        var earlier = await EventAsync(appointment, AppointmentEventTypes.Rescheduled);   // written by the pre-M4 schema

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();

        await using var check = Fixture.CreateContext();
        Assert.Empty(await check.Database.GetPendingMigrationsAsync());
        var row = await check.AppointmentEvents.AsNoTracking().SingleAsync(e => e.Id == earlier);
        Assert.Equal((AppointmentEventTypes.Rescheduled, S.ProviderA, S.Op1, "Rescheduled for 60 minutes."), (row.EventType, row.PreviousProviderProfileId!.Value, row.PreviousOperatoryId!.Value, row.Detail));
        Assert.Equal(new DateTimeOffset(2030, 1, 14, 15, 0, 0, TimeSpan.Zero), row.PreviousStartUtc);
        Assert.Equal((null, null, null, null, null), (row.NewStartUtc, row.NewProviderProfileId, row.NewProviderName, row.NewOperatoryId, row.NewOperatoryName));
    }

    [Fact]
    public async Task M4_rolls_back_and_forward_when_no_destination_is_recorded()
    {
        var appointment = await AppointmentAsync();
        await EventAsync(appointment, AppointmentEventTypes.Rescheduled);   // history without a destination does not block the rollback
        await MigrateToAsync(BeforeM4);
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext()) Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var e = await EventAsync(appointment, AppointmentEventTypes.RoomChanged);
        await SetAsync(e, "[NewOperatoryId] = {0}, [NewOperatoryName] = N'Op 2'", S.Op2);   // the restored schema works
    }

    [Fact]
    public async Task M4_refuses_to_roll_back_over_a_recorded_destination()
    {
        var e = await EventAsync(await AppointmentAsync(), AppointmentEventTypes.RoomChanged);
        await SetAsync(e, "[NewOperatoryId] = {0}, [NewOperatoryName] = N'Op 2'", S.Op2);
        Assert.Equal(51285, (await Refusal(() => MigrateToAsync(BeforeM4))).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("Op 2", (await db.AppointmentEvents.AsNoTracking().SingleAsync(x => x.Id == e)).NewOperatoryName);
    }
}
