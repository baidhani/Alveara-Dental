using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Backup;

/// <summary>ALV-N026 (PD-25): which backup notification kinds reach the administrators' inbox, and how urgently. Anything else stays a file drop and a durable record only.</summary>
public static class BackupAlertKinds
{
    public const string BackupFailed = "BackupFailed";
    public const string BackupVerificationFailed = "BackupVerificationFailed";
    public const string RestoreDrillFailed = "RestoreDrillFailed";
    public const string RestoreDrillSucceeded = "RestoreDrillSucceeded";
    public const string TestNotification = "TestNotification";

    /// <summary>The failures: Urgent in the inbox, shown in the administrator banner until acknowledged, and a System Status warning until a later success resolves them.</summary>
    public static readonly IReadOnlyList<string> Failures = [BackupFailed, BackupVerificationFailed, RestoreDrillFailed];

    /// <summary>Results that are worth telling but need no action: Normal in the inbox, no banner.</summary>
    public static readonly IReadOnlyList<string> Informational = [RestoreDrillSucceeded, TestNotification];

    public static bool GoesToInbox(string kind) => Failures.Contains(kind) || Informational.Contains(kind);
    public static string PriorityOf(string kind) => Failures.Contains(kind) ? NotificationPriorities.Urgent : NotificationPriorities.Normal;
}

/// <summary>A delivery channel failed; <see cref="Code"/> names which (file drop, inbox or both), for the alert record. Never changes the backup's own outcome.</summary>
public sealed class BackupDeliveryException(string code, Exception? inner = null) : Exception($"Backup notification delivery failed: {code}.", inner)
{
    public string Code { get; } = code;
}

/// <summary>
/// ALV-N026 (PD-25): delivers a backup notification by every channel - the existing file drop always, and the administrators' in-app inbox for the kinds in <see cref="BackupAlertKinds"/>.
/// Each channel is attempted whatever the other did. If any fails, a <see cref="BackupDeliveryException"/> names the failed channel(s) so the caller records it on the durable alert row;
/// the file drop and the durable record remain whatever happens to the inbox. The inbox step is bounded by <see cref="InboxTimeout"/>.
/// </summary>
public sealed class CompositeBackupNotifier(FileDropBackupNotifier fileDrop, IServiceScopeFactory scopes, ILogger<CompositeBackupNotifier> log) : IBackupNotifier
{
    public static readonly TimeSpan InboxTimeout = TimeSpan.FromSeconds(10);

    public async Task DeliverAsync(BackupNotification notification, CancellationToken cancellationToken)
    {
        var failed = new List<string>();
        Exception? first = null;
        try
        {
            await fileDrop.DeliverAsync(notification, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            failed.Add("file_drop"); first ??= ex;
            log.LogError("event=backup_alert_file_drop_failed outcome=failure error_class={ErrorClass} kind={Kind}", ex.GetType().Name, notification.Kind);
        }

        if (BackupAlertKinds.GoesToInbox(notification.Kind) && notification.AlertId is { } alertId)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(InboxTimeout);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var inbox = scope.ServiceProvider.GetRequiredService<InAppBackupAlert>();
                await inbox.SendAsync(alertId, notification.Kind, timeout.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed.Add("inbox"); first ??= ex;
                log.LogError("event=backup_alert_inbox_failed outcome=failure error_class={ErrorClass} kind={Kind} alert={AlertId}", ex.GetType().Name, notification.Kind, alertId);
            }
        }

        if (failed.Count > 0) throw new BackupDeliveryException(string.Join("+", failed) + "_failed", first);
    }
}

/// <summary>
/// ALV-N026 (PD-25): one in-app notification per enabled administrator for a backup alert. Kind <see cref="NotificationKinds.BackupAlert"/>, source key <c>BackupAlert:&lt;alert id&gt;</c>, no text, patient
/// or document (the inbox shows a fixed label; the alert's own record holds the details). Idempotent: the unique (recipient, source key) index and a pre-check give at most one notification per
/// administrator per alert, however often this runs; losing a unique-key race to a concurrent send means it already exists.
/// </summary>
public sealed class InAppBackupAlert(AlveraDbContext db, ILogger<InAppBackupAlert> log)
{
    /// <summary>Letters, digits and a colon only: the database rule CK_Notifications_SourceKey.</summary>
    public static string SourceKeyFor(Guid alertId) => $"BackupAlert:{alertId:N}";

    public async Task SendAsync(Guid alertId, string kind, CancellationToken ct)
    {
        var sourceKey = SourceKeyFor(alertId);
        var recipients = await db.UserAccounts.AsNoTracking().Where(a => !a.IsDisabled && a.Role == Role.Admin).Select(a => a.Id).ToListAsync(ct);
        var already = await db.Notifications.AsNoTracking().Where(n => n.SourceKey == sourceKey && recipients.Contains(n.RecipientUserId)).Select(n => n.RecipientUserId).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var recipient in recipients.Except(already))
            db.Notifications.Add(new Notification { Id = Guid.NewGuid(), RecipientUserId = recipient, Kind = NotificationKinds.BackupAlert, Priority = BackupAlertKinds.PriorityOf(kind), SourceKey = sourceKey, CreatedAtUtc = now });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();
            log.LogInformation("event=backup_alert_inbox_already_sent alert={AlertId}", alertId);
            return;
        }
        log.LogWarning("event=backup_alert_inbox_sent outcome=success kind={Kind} alert={AlertId} recipients={Count}", kind, alertId, recipients.Count - already.Count);
    }
}

public sealed record BackupAlertView(Guid Id, string Kind, DateTimeOffset CreatedAtUtc, string Delivery, string? DeliveryFailureCode, bool Acknowledged, DateTimeOffset? AcknowledgedAtUtc);

/// <summary>
/// ALV-N026 (PD-25): reading and acknowledging backup failure alerts. An acknowledgement is one append-only row per alert (the database refuses a change or a deletion), audited as
/// <c>BackupAlertAcknowledged</c>; acknowledging again changes nothing and logs nothing. Acknowledging hides the alert from the administrator banner only: System Status stays in warning until the
/// underlying condition is resolved (<see cref="BackupHealth"/>).
/// </summary>
public sealed class BackupAlertService(AlveraDbContext db)
{
    public async Task<IReadOnlyList<BackupAlertView>> UnacknowledgedFailuresAsync(CancellationToken ct)
    {
        var failures = BackupAlertKinds.Failures;
        var rows = await (from n in db.BackupNotifications.AsNoTracking()
                          where failures.Contains(n.Kind) && !db.BackupAlertAcknowledgements.Any(a => a.BackupNotificationRecordId == n.Id)
                          orderby n.CreatedAtUtc descending
                          select n).Take(50).ToListAsync(ct);
        return rows.Select(n => new BackupAlertView(n.Id, n.Kind, n.CreatedAtUtc, n.Delivery.ToString(), n.DeliveryFailureCode, false, null)).ToList();
    }

    public async Task<BackupAlertView> AcknowledgeAsync(Guid alertId, Guid actor, CancellationToken ct)
    {
        var alert = await db.BackupNotifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == alertId, ct)
            ?? throw new BackupException("alert_not_found", "That backup alert was not found.", 404);
        if (!BackupAlertKinds.Failures.Contains(alert.Kind)) throw new BackupException("alert_not_acknowledgeable", "Only a backup or restore failure is acknowledged.", 409);

        var existing = await db.BackupAlertAcknowledgements.AsNoTracking().SingleOrDefaultAsync(a => a.BackupNotificationRecordId == alertId, ct);
        if (existing is not null) return View(alert, existing);

        var ack = new BackupAlertAcknowledgement { Id = Guid.NewGuid(), BackupNotificationRecordId = alertId, AcknowledgedByUserId = actor, AcknowledgedAtUtc = DateTimeOffset.UtcNow };
        db.BackupAlertAcknowledgements.Add(ack);
        AuditService.Record(db, "BackupAlertAcknowledged", nameof(BackupNotificationRecord), alertId, actor, $"Backup alert acknowledged ({alert.Kind}).");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                       // a concurrent acknowledgement won: that one stands
            existing = await db.BackupAlertAcknowledgements.AsNoTracking().SingleAsync(a => a.BackupNotificationRecordId == alertId, ct);
            return View(alert, existing);
        }
        return View(alert, ack);
    }

    private static BackupAlertView View(BackupNotificationRecord n, BackupAlertAcknowledgement a) =>
        new(n.Id, n.Kind, n.CreatedAtUtc, n.Delivery.ToString(), n.DeliveryFailureCode, true, a.AcknowledgedAtUtc);
}

public sealed record BackupHealthView(DateTimeOffset? LastGoodBackupUtc, DateTimeOffset? LastRestoreDrillUtc, string? LastRestoreDrillOutcome, bool RecoveryKeyConfigured, bool ScheduleEnabled,
    bool Overdue, int UnresolvedFailures, int UnacknowledgedAlerts, bool Warning);

/// <summary>
/// ALV-N026 (D-06, PD-25): the backup-and-recovery state for System Status. A failure is unresolved until something later succeeds: a backup failure until a later successful backup, a
/// verification failure until a later fully verified backup, a drill failure until a later successful drill. Overdue: the schedule is on and no backup has succeeded within its interval plus
/// one hour. The warning holds while anything is unresolved, overdue or the recovery key is missing - acknowledging an alert does not clear it.
/// </summary>
public sealed class BackupHealth(AlveraDbContext db)
{
    public static readonly TimeSpan OverdueGrace = TimeSpan.FromHours(1);

    public async Task<BackupHealthView> ReadAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = await db.BackupSettings.AsNoTracking().SingleOrDefaultAsync(ct);
        var lastGood = await db.BackupRecords.AsNoTracking().Where(r => r.Status == BackupStatus.Succeeded).MaxAsync(r => (DateTimeOffset?)r.CompletedAtUtc, ct);
        var lastVerified = await db.BackupRecords.AsNoTracking().Where(r => r.VerificationStatus == BackupVerificationStatus.FullyVerified).MaxAsync(r => (DateTimeOffset?)r.VerifiedAtUtc, ct);
        var lastDrill = await db.RestoreDrills.AsNoTracking().OrderByDescending(d => d.StartedAtUtc).FirstOrDefaultAsync(ct);
        var lastGoodDrill = await db.RestoreDrills.AsNoTracking().Where(d => d.Outcome == "Succeeded").MaxAsync(d => (DateTimeOffset?)d.CompletedAtUtc, ct);

        var failures = BackupAlertKinds.Failures;
        var alerts = await db.BackupNotifications.AsNoTracking().Where(n => failures.Contains(n.Kind)).Select(n => new { n.Id, n.Kind, n.CreatedAtUtc }).ToListAsync(ct);
        bool Resolved(string kind, DateTimeOffset at) => kind switch
        {
            BackupAlertKinds.BackupFailed => lastGood > at,
            BackupAlertKinds.BackupVerificationFailed => lastVerified > at,
            BackupAlertKinds.RestoreDrillFailed => lastGoodDrill > at,
            _ => true,
        };
        var unresolved = alerts.Count(a => !Resolved(a.Kind, a.CreatedAtUtc));
        var acknowledged = (await db.BackupAlertAcknowledgements.AsNoTracking().Select(a => a.BackupNotificationRecordId).ToListAsync(ct)).ToHashSet();
        var unacknowledged = alerts.Count(a => !acknowledged.Contains(a.Id));

        var scheduleOn = settings?.ScheduleEnabled == true;
        var overdue = scheduleOn && (lastGood is null || now - lastGood > TimeSpan.FromHours(settings!.ScheduleIntervalHours) + OverdueGrace);
        var keyConfigured = settings?.RecoveryPublicKeyPem is not null;
        return new BackupHealthView(lastGood, lastDrill?.StartedAtUtc, lastDrill?.Outcome, keyConfigured, scheduleOn, overdue, unresolved, unacknowledged,
            Warning: unresolved > 0 || overdue || !keyConfigured);
    }
}
