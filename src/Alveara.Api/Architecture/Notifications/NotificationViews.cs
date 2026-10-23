namespace Alveara.Api.Architecture.Notifications;

/// <summary>One thing wrong with a field of a request, named so the screen can point at it.</summary>
public sealed record NotificationProblem(string Field, string Code, string Message);

/// <summary>A notification or message action that was refused; nothing was saved. <see cref="Code"/> is the stable <c>error</c> code of the API (docs/NOTIFICATIONS.md).</summary>
public class NotificationException(string code, string message, int statusCode, IReadOnlyList<NotificationProblem>? problems = null, int? retryAfterSeconds = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<NotificationProblem> Problems { get; } = problems ?? [];
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>What the bell and the banner need, from an index: never the list. The banner counts only Important and Urgent unread entries.</summary>
public sealed record NotificationSummary(int Unread, int UnreadImportant, int UnreadUrgent, DateTimeOffset? NewestBannerAtUtc);

/// <summary>
/// One entry of the caller's own list. It holds a kind, a priority and times, and for a message the sender's name and the subject (the panel shows them); it never holds a patient name, a patient id,
/// a file name or message text. <see cref="HasPatientLink"/> only says that a server-checked link exists; the link is resolved from the notification, never built from an id the client holds.
/// </summary>
public sealed record NotificationView(Guid Id, string Kind, string Priority, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc, Guid? MessageId, string? SenderName, bool HasPatientLink);

public sealed record NotificationPage(IReadOnlyList<NotificationView> Items, int Total, int Page, int PageSize);

public sealed record NotificationResult(int Changed);

/// <summary>Where a notification or message link goes, returned only after the server confirmed the caller may view that patient.</summary>
public sealed record PatientLinkTarget(string Path);

/// <summary>A message the caller wrote or received. Read receipts are shown to the sender only.</summary>
public sealed record MessageView(Guid Id, Guid ThreadId, Guid? InReplyToMessageId, string SenderName, bool IsFromMe, string? Subject, string Body, string Priority, DateTimeOffset SentAtUtc, bool HasPatientLink,
    IReadOnlyList<MessageRecipientView> Recipients, DateTimeOffset? MyReadAtUtc);

public sealed record MessageRecipientView(Guid UserId, string Name, DateTimeOffset? ReadAtUtc);

public sealed record MessageListItem(Guid Id, Guid ThreadId, string SenderName, bool IsFromMe, string? Subject, string Priority, DateTimeOffset SentAtUtc, int RecipientCount, int ReadCount, bool IsUnread);

public sealed record MessagePage(IReadOnlyList<MessageListItem> Items, int Total, int Page, int PageSize);

public sealed record MessageRecipientChoice(Guid UserId, string Name, string Role);

/// <summary>What a send asks for. Nothing in it is trusted until the service has checked it. The patient link is checked by the caller's permission before it reaches the service.</summary>
public sealed record SendMessageRequest(IReadOnlyList<Guid>? RecipientUserIds, string? Subject, string? Body, string? Priority, Guid? RelatedPatientId, Guid? InReplyToMessageId, string? IdempotencyKey);

public sealed record SendMessageResult(MessageView Message, bool Replayed);

/// <summary>One row of the administrator's notification history: metadata only, never a subject, a body or a patient identifier.</summary>
public sealed record NotificationHistoryItem(Guid Id, string RecipientName, string Kind, string Priority, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc, DateTimeOffset? DeletedAtUtc, string? SourceKey, Guid? MessageId);

public sealed record NotificationHistoryPage(IReadOnlyList<NotificationHistoryItem> Items, int Total, int Page, int PageSize);

/// <summary>The content of one message or thread opened through the audited break-glass action.</summary>
public sealed record BreakGlassResult(Guid? MessageId, Guid? ThreadId, IReadOnlyList<BreakGlassMessage> Messages);

public sealed record BreakGlassMessage(Guid Id, string SenderName, string? Subject, string Body, string Priority, DateTimeOffset SentAtUtc, IReadOnlyList<string> RecipientNames);

/// <summary>What the break-glass request states: whose message, why, and that the administrator confirmed.</summary>
public sealed record BreakGlassRequest(Guid? MessageId, Guid? ThreadId, string? Reason, bool Confirmed);
