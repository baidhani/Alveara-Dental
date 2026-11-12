using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Configuration;

/// <summary>
/// ALV-N020 (F-021; migration decision Option A): the alert for a staff inactivation that could not disable the linked login. One Urgent
/// <see cref="NotificationKinds.StaffInactivationFailed"/> notification to every enabled Admin and OfficeManager account, the acting administrator included
/// when they hold one of those roles.
/// <list type="bullet">
/// <item>Called only after the durable failure audit entry exists (<see cref="StaffInactivationService"/>); the source key names that entry, so the
/// notification is never the only evidence.</item>
/// <item>Idempotent: the unique index (RecipientUserId, SourceKey) and the pre-check give at most one notification per recipient per recorded failure, however
/// often this runs. A unique-key race with a concurrent send means the notification already exists.</item>
/// <item>Carries no text from the exception and no patient, message or document link: the list shows its fixed label.</item>
/// <item>Best effort: the caller bounds it with a timeout and logs any failure; a failure here never changes the refusal or hides the record. A recipient disabled
/// between the read and the write makes the database refuse the whole batch (TR_Notifications_Links); that is logged by the caller like any other failure.</item>
/// </list>
/// </summary>
public sealed class InAppStaffInactivationAlert(AlveraDbContext db, IPracticeClock clock, ILogger<InAppStaffInactivationAlert> log) : IStaffInactivationAlert
{
    /// <summary>Letters, digits and a colon only, like <c>DocumentEvents:42</c>: the database rule CK_Notifications_SourceKey refuses a hyphen.</summary>
    public static string SourceKeyFor(Guid failureRecordId) => $"StaffInactivationFailed:{failureRecordId:N}";

    public async Task SendAsync(StaffInactivationFailure failure, CancellationToken ct)
    {
        var sourceKey = SourceKeyFor(failure.FailureRecordId);
        var recipients = await db.UserAccounts.AsNoTracking()
            .Where(a => !a.IsDisabled && (a.Role == Role.Admin || a.Role == Role.OfficeManager))
            .Select(a => a.Id).ToListAsync(ct);
        var already = await db.Notifications.AsNoTracking()
            .Where(n => n.SourceKey == sourceKey && recipients.Contains(n.RecipientUserId))
            .Select(n => n.RecipientUserId).ToListAsync(ct);
        var now = clock.UtcNow;
        foreach (var recipient in recipients.Except(already))
        {
            db.Notifications.Add(new Notification
            {
                Id = Guid.NewGuid(), RecipientUserId = recipient, Kind = NotificationKinds.StaffInactivationFailed, Priority = NotificationPriorities.Urgent,
                SourceKey = sourceKey, CreatedAtUtc = now,
            });
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            log.LogInformation("staff_inactivation_alert_already_sent staff={StaffProfileId} failure_record={FailureRecordId} correlation={CorrelationId}",
                failure.StaffProfileId, failure.FailureRecordId, failure.CorrelationId);
            return;
        }
        catch
        {
            db.ChangeTracker.Clear();                                                                   // leave the shared context clean; the caller logs the failure
            throw;
        }
        log.LogWarning("staff_inactivation_alert_sent staff={StaffProfileId} failure_record={FailureRecordId} recipients={Count} error_class={ErrorClass} correlation={CorrelationId}",
            failure.StaffProfileId, failure.FailureRecordId, recipients.Count - already.Count, failure.ErrorClass, failure.CorrelationId);
    }
}
