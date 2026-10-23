using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Notifications;

/// <summary>
/// ALV-N015: the durable, cursor-driven consumer of a producer's immutable, ordered event history. It reads events after the cursor, turns the ones it knows into notifications and moves the cursor in the same
/// transaction, so a crash between creating notifications and moving the cursor repeats the batch and the unique source key makes the repeat harmless. The producer never calls the center and is unaffected when it is
/// unavailable; delivery is eventually consistent. Only <see cref="DocumentEventKinds.ImportedAsDuplicate"/> is registered: the consumer reads an event's number, kind, document ids, patient id and time and never a
/// file name, description or content (the event holds none). The recipients are the enabled accounts whose role holds <see cref="Permission.ReceiveDocumentNotifications"/> <i>when the event is processed</i>: a permission
/// granted later does not back-fill an event that has already passed, and a removed permission prevents future delivery. The initiating person is included when they hold the permission.
/// </summary>
public class NotificationConsumer(AlveraDbContext db, IPracticeClock clock)
{
    public const int BatchSize = 100;

    /// <summary>Processes up to one batch of document events and returns how many events were examined (0 when the consumer is caught up).</summary>
    public async Task<int> ProcessDocumentEventsAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var cursor = await db.NotificationCursors.SingleAsync(c => c.Source == NotificationSources.DocumentEvents, ct);
        var events = await db.DocumentEvents.AsNoTracking().Where(e => e.EventNumber > cursor.LastEventNumber).OrderBy(e => e.EventNumber).Take(BatchSize).ToListAsync(ct);
        if (events.Count == 0) return 0;

        var roles = Enum.GetValues<Role>().Where(r => PermissionMatrix.RoleHas(r, Permission.ReceiveDocumentNotifications)).ToList();
        var recipients = await db.UserAccounts.AsNoTracking().Where(u => !u.IsDisabled && roles.Contains(u.Role)).Select(u => u.Id).ToListAsync(ct);
        var now = clock.UtcNow;

        var duplicates = events.Where(e => e.Kind == DocumentEventKinds.ImportedAsDuplicate && e.RelatedDocumentId != null).ToList();
        var keys = duplicates.Select(e => Key(e.EventNumber)).ToList();
        var existing = keys.Count == 0 ? [] : (await db.Notifications.AsNoTracking().Where(n => n.SourceKey != null && keys.Contains(n.SourceKey)).Select(n => new { n.RecipientUserId, n.SourceKey }).ToListAsync(ct))
            .Select(n => (n.RecipientUserId, n.SourceKey!)).ToHashSet();
        foreach (var e in duplicates)
            foreach (var recipient in recipients)
            {
                if (existing.Contains((recipient, Key(e.EventNumber)))) continue;                                  // a repeat of an event already turned into a notification
                db.Notifications.Add(new Notification
                {
                    Id = Guid.NewGuid(), RecipientUserId = recipient, Kind = NotificationKinds.DocumentDuplicateAdded, Priority = NotificationPriorities.Important,
                    RelatedPatientId = e.PatientId, RelatedDocumentId = e.DocumentId, RelatedOtherDocumentId = e.RelatedDocumentId, SourceKey = Key(e.EventNumber), CreatedAtUtc = now,
                });
            }

        cursor.LastEventNumber = events[^1].EventNumber;
        cursor.UpdatedAtUtc = now;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return events.Count;
    }

    /// <summary>Processes batches until the consumer is caught up.</summary>
    public async Task<int> ProcessAllAsync(CancellationToken ct)
    {
        var total = 0;
        int n;
        while ((n = await ProcessDocumentEventsAsync(ct)) > 0) total += n;
        return total;
    }

    public static string Key(long eventNumber) => $"{NotificationSources.DocumentEvents}:{eventNumber}";
}

/// <summary>
/// Runs the consumer in the background. A failure is logged with its class only (never a row or an event's content), the loop waits longer after each consecutive failure (capped) and carries on; the producers are
/// unaffected. <c>Notifications:ConsumerPollSeconds</c> sets the poll interval (default 5); 0 turns the loop off (tests that drive the consumer themselves).
/// </summary>
public sealed class NotificationConsumerHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<NotificationConsumerHostedService> logger) : BackgroundService
{
    private const int MaxBackoffSeconds = 60;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = configuration.GetValue<int?>("Notifications:ConsumerPollSeconds") ?? 5;
        if (poll <= 0) return;
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<NotificationConsumer>().ProcessAllAsync(stoppingToken);
                failures = 0;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                logger.LogWarning("notification_consumer_failed error_class={ErrorClass} consecutive_failures={Failures}", ex.GetType().Name, failures);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(poll * (1 << Math.Min(failures, 4)), MaxBackoffSeconds)), stoppingToken); }
            catch (TaskCanceledException) { /* shutdown */ }
        }
    }
}
