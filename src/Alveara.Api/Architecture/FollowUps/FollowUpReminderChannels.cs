using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>What a reminder says: which task, which patient, when it was due, who is told, and the key that makes telling them twice impossible. It carries no title and no note.</summary>
public sealed record ReminderRequest(Guid TaskId, Guid PatientId, DateTimeOffset DueAtUtc, Guid RecipientUserId, string SourceKey);

public enum ReminderChannelResult
{
    /// <summary>The recipient has been told (or the telling is staged in the same save, for the in-app channel).</summary>
    Delivered,
    /// <summary>The channel could not tell the recipient now; the reminder pass will try again on its next run. No delivery is recorded.</summary>
    RetryLater,
}

/// <summary>
/// STORY-009: one way of telling someone that a follow-up is due. Only the in-app channel exists. A channel for email is designed for and not built (docs/design/FOLLOW_UP_TASKS_DESIGN.md, "Later channels"): it
/// would be one new class and one registration, with its own timeout, capped retries and dead-letter handling, and the pass, the keys and the tests would not change. A channel must be safe to call again for the same
/// <see cref="ReminderRequest.SourceKey"/> and recipient (it must not tell the person twice), because a pass that fails after a channel has sent is repeated. An exception from a channel is treated as
/// <see cref="ReminderChannelResult.RetryLater"/> for that task and never stops the other tasks or the other channels.
/// </summary>
public interface IReminderChannel
{
    /// <summary>The stable name recorded with each delivery (see <see cref="FollowUpChannels"/>).</summary>
    string Channel { get; }

    Task<ReminderChannelResult> SendAsync(ReminderRequest request, CancellationToken ct);
}

/// <summary>
/// The in-app channel: stages a notification for the recipient in the notification center, in the same save as the delivery record. The notification carries the patient and a key naming the task and its due time,
/// never the title or note; opening it takes the person to the patient's follow-ups. The unique key per recipient makes a repeat harmless.
/// </summary>
public sealed class InAppReminderChannel(AlveraDbContext db, IPracticeClock clock) : IReminderChannel
{
    public string Channel => FollowUpChannels.InApp;

    public Task<ReminderChannelResult> SendAsync(ReminderRequest request, CancellationToken ct)
    {
        db.Notifications.Add(new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = request.RecipientUserId, Kind = NotificationKinds.FollowUpDue, Priority = NotificationPriorities.Normal,
            RelatedPatientId = request.PatientId, SourceKey = request.SourceKey, CreatedAtUtc = clock.UtcNow,
        });
        return Task.FromResult(ReminderChannelResult.Delivered);
    }
}
