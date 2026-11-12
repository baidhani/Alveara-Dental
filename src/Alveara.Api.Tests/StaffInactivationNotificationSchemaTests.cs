using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Notifications;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N020 (F-021; migration decision Option A), the database on its own: the notification kind <c>staff.inactivation_failed</c> is accepted only with its
/// permitted shape (a source key; no patient, message or document), an unknown kind is still refused, and the migration
/// <c>AddStaffInactivationFailedNotification</c> rolls back and forward on an empty table and refuses to roll back (51275) once such a notification exists,
/// leaving it in place. Rows are written straight to the tables, around any service.
/// </summary>
public class StaffInactivationNotificationSchemaTests : SafetyTestBase
{
    private const string BeforeThisMigration = "20261029043602_AddReportBaselines";

    private async Task AddAsync(params object[] rows)
    {
        await using var db = Fixture.CreateContext();
        db.AddRange(rows);
        await db.SaveChangesAsync();
    }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private Notification Alert(Action<Notification>? change = null)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = Other, Kind = NotificationKinds.StaffInactivationFailed, Priority = NotificationPriorities.Urgent,
            SourceKey = InAppStaffInactivationAlert.SourceKeyFor(Guid.NewGuid()), CreatedAtUtc = DateTimeOffset.UtcNow,   // the real key, so the database checks its format
        };
        change?.Invoke(n);
        return n;
    }

    private async Task<int> AlertCountAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.Notifications.CountAsync(n => n.Kind == NotificationKinds.StaffInactivationFailed);
    }

    private static void ShapeRefused(Microsoft.Data.SqlClient.SqlException e) =>
        Assert.True(e.Number == 547 && e.Message.Contains("CK_Notifications_Shape"), $"expected the shape constraint, got {e.Number}: {e.Message}");

    [Fact]
    public async Task The_new_kind_is_accepted_with_only_a_source_key()
    {
        await AddAsync(Alert());
        Assert.Equal(1, await AlertCountAsync());
    }

    [Fact]
    public async Task The_new_kind_is_refused_with_a_patient_link()
    {
        ShapeRefused(await Refusal(() => AddAsync(Alert(n => n.RelatedPatientId = Ann))));
        Assert.Equal(0, await AlertCountAsync());
    }

    [Fact]
    public async Task The_new_kind_is_refused_with_a_message_link()
    {
        var id = Guid.NewGuid();
        await AddAsync(new Message
        {
            Id = id, SenderUserId = S.Actor, Subject = "Room 3", Body = "Please set up room 3.", Priority = NotificationPriorities.Urgent, ThreadId = id,
            CreatedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"k-{Guid.NewGuid():N}", RequestHash = new string('a', 64),
        }, new MessageRecipient { MessageId = id, RecipientUserId = Other });
        ShapeRefused(await Refusal(() => AddAsync(Alert(n => n.MessageId = id))));
        Assert.Equal(0, await AlertCountAsync());
    }

    [Fact]
    public async Task The_new_kind_is_refused_without_a_source_key()
    {
        ShapeRefused(await Refusal(() => AddAsync(Alert(n => n.SourceKey = null))));
        Assert.Equal(0, await AlertCountAsync());
    }

    [Fact]
    public async Task An_unknown_kind_is_still_refused()
    {
        var e = await Refusal(() => AddAsync(Alert(n => n.Kind = "staff.something_else")));
        Assert.True(e.Number == 547 && e.Message.Contains("CK_Notifications_Kind"), $"expected the kind constraint, got {e.Number}: {e.Message}");
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_on_an_empty_table_and_refuses_to_roll_back_over_an_alert()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeThisMigration);
        var refusedBefore = await Refusal(() => AddAsync(Alert()));                                         // the three-kind rules are back
        Assert.True(refusedBefore.Number == 547 && refusedBefore.Message.Contains("CK_Notifications_Kind"), $"{refusedBefore.Number}: {refusedBefore.Message}");

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();                   // the upgrade
        await AddAsync(Alert());
        Assert.Equal(1, await AlertCountAsync());

        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeThisMigration); });
        Assert.Equal(51275, e.Number);
        Assert.Equal(1, await AlertCountAsync());                                                          // immutable history is never deleted to make a rollback succeed
        await using (var db = Fixture.CreateContext())
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());                                   // still on the latest schema
    }
}
