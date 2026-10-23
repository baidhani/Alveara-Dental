namespace Alveara.Api.Architecture.Notifications;

/// <summary>ALV-N015: the three priorities. Anyone who may send a message may pick any of them (design decision 3); accountability is the immutable audit record and the per-sender rate limit.</summary>
public static class NotificationPriorities
{
    public const string Normal = "Normal";
    public const string Important = "Important";
    public const string Urgent = "Urgent";
    public static readonly IReadOnlyList<string> All = [Normal, Important, Urgent];
    /// <summary>The banner is shown for these two only.</summary>
    public static bool ShowsBanner(string priority) => priority is Important or Urgent;
}

/// <summary>Stable notification kinds. A new kind needs a migration on purpose: the database refuses a kind it does not know.</summary>
public static class NotificationKinds
{
    public const string DocumentDuplicateAdded = "document.duplicate_added";
    public const string MessageReceived = "message.received";
    public static readonly IReadOnlyList<string> All = [DocumentDuplicateAdded, MessageReceived];
}

/// <summary>The producers whose immutable, ordered event history the consumer reads. One cursor row per source.</summary>
public static class NotificationSources
{
    public const string DocumentEvents = "DocumentEvents";
    public static readonly IReadOnlyList<string> All = [DocumentEvents];
}

public static class MessageRules
{
    public const int SubjectMaxLength = 120;
    public const int BodyMaxLength = 2000;
    /// <summary>The per-sender rate limit (design: about 30 messages a minute).</summary>
    public const int MaxMessagesPerMinute = 30;
}

/// <summary>
/// An operational message: content a person wrote. Immutable once sent (the database refuses any update or delete). It is operational communication only, never clinical documentation; the
/// optional patient link is a navigation aid that is resolved on the server from the message, never a raw id handed to the client. Holds plain text only.
/// </summary>
public class Message
{
    public Guid Id { get; set; }
    public Guid SenderUserId { get; set; }
    public string? Subject { get; set; }
    public required string Body { get; set; }
    public required string Priority { get; set; }
    public Guid? RelatedPatientId { get; set; }
    public Guid? InReplyToMessageId { get; set; }
    /// <summary>The first message of a thread has <c>ThreadId = Id</c>; every reply carries the thread of the message it answers.</summary>
    public Guid ThreadId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public required string IdempotencyKey { get; set; }
    /// <summary>SHA-256 of the canonical request, so the same key with a changed request is a conflict and the same request is a quiet replay.</summary>
    public required string RequestHash { get; set; }
}

/// <summary>One named recipient of a message. Append-only; the pair is unique.</summary>
public class MessageRecipient
{
    public Guid MessageId { get; set; }
    public Guid RecipientUserId { get; set; }
}

/// <summary>
/// One person's inbox entry. Only <see cref="ReadAtUtc"/> and <see cref="DeletedAtUtc"/> can ever change, each once, and an entry cannot be deleted before it is read; nothing is ever physically
/// deleted. It holds ids and a kind only: no text, no patient name, no filename. For a message the read time is also the sender's read receipt.
/// </summary>
public class Notification
{
    public Guid Id { get; set; }
    public Guid RecipientUserId { get; set; }
    public required string Kind { get; set; }
    public required string Priority { get; set; }
    public Guid? MessageId { get; set; }
    public Guid? RelatedPatientId { get; set; }
    public Guid? RelatedDocumentId { get; set; }
    public Guid? RelatedOtherDocumentId { get; set; }
    /// <summary>For a system notification, a stable key of the source event (for example <c>DocumentEvents:42</c>). Unique per recipient, so processing an event twice creates one notification.</summary>
    public string? SourceKey { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? ReadAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>The position the consumer has reached in one producer's ordered history. It starts at the high-water mark of the day the center is installed (no back-fill) and only ever moves forward.</summary>
public class NotificationCursor
{
    public required string Source { get; set; }
    public long LastEventNumber { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}
