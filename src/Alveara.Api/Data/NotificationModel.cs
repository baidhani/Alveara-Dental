using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Data;

/// <summary>
/// ALV-N015: the model for notifications and operational messages (docs/NOTIFICATIONS.md). As elsewhere, the database refuses by itself what the rules refuse. Triggers (created in the migration; errors
/// 51195 to 51202): a message is never updated or deleted (51195); a reply belongs to the thread of the message it answers (51196); a message's recipient rows are never updated or deleted (51197); a recipient
/// is an enabled account (51198); a notification can only have its read and deleted times set, each once, and is never deleted or deleted unread (51199, 51200); a notification belongs to a recipient of its
/// message, carries its message's priority and patient link, and goes to an enabled account (51201); a source cursor never moves backward and is never deleted (51202).
/// </summary>
internal static class NotificationModel
{
    private const string Utc = "'2000-01-01'";

    /// <summary>Not blank once spaces, tabs and line breaks are removed (LTRIM and RTRIM only remove spaces).</summary>
    private static string NotBlank(string column) => $"LEN(REPLACE(REPLACE(REPLACE(REPLACE([{column}], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0";

    /// <summary>The existing control-character check starts at NCHAR(1), so NUL gets its own check.</summary>
    private static string NoNul(string column) => $"CHARINDEX(NCHAR(0), [{column}] COLLATE Latin1_General_BIN2) = 0";

    private static string Hex(string column) => $"[{column}] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([{column}]) = 64";

    /// <summary>A message body may contain tab, line feed and carriage return but no other control character.</summary>
    private static string BodyChars(string column) =>
        $"PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [{column}] COLLATE Latin1_General_BIN2) = 0";

    public static void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Message>(e =>
        {
            e.ToTable("Messages");
            e.Property(x => x.Subject).HasMaxLength(MessageRules.SubjectMaxLength);
            e.Property(x => x.Body).HasMaxLength(MessageRules.BodyMaxLength);
            e.Property(x => x.Priority).HasMaxLength(10);
            e.Property(x => x.IdempotencyKey).HasMaxLength(64);
            e.Property(x => x.RequestHash).HasMaxLength(64).IsFixedLength().IsUnicode(false);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.SenderUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.RelatedPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.InReplyToMessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SenderUserId, x.IdempotencyKey }).IsUnique();
            e.HasIndex(x => new { x.SenderUserId, x.CreatedAtUtc });
            e.HasIndex(x => x.ThreadId);
            e.HasIndex(x => x.InReplyToMessageId);
            e.HasIndex(x => x.RelatedPatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Messages_Immutable");
                t.HasTrigger("TR_Messages_Links");
                t.HasCheckConstraint("CK_Messages_Priority", $"[Priority] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(NotificationPriorities.All)})");
                t.HasCheckConstraint("CK_Messages_Subject", $"[Subject] IS NULL OR (LEN(LTRIM(RTRIM([Subject]))) > 0 AND {NoNul("Subject")} AND {PrescriptionModel.NoControl("Subject")})");
                t.HasCheckConstraint("CK_Messages_Body", $"{NotBlank("Body")} AND {NoNul("Body")} AND {BodyChars("Body")}");
                t.HasCheckConstraint("CK_Messages_Key", $"LEN([IdempotencyKey]) > 0 AND {Hex("RequestHash")}");
                // the first message of a thread is its own thread; a reply never answers itself
                t.HasCheckConstraint("CK_Messages_Thread", "([InReplyToMessageId] IS NULL AND [ThreadId] = [Id]) OR ([InReplyToMessageId] IS NOT NULL AND [InReplyToMessageId] <> [Id] AND [ThreadId] <> [Id])");
                t.HasCheckConstraint("CK_Messages_Time", $"[CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < '2200-01-01'");
            });
        });

        modelBuilder.Entity<MessageRecipient>(e =>
        {
            e.ToTable("MessageRecipients");
            e.HasKey(x => new { x.MessageId, x.RecipientUserId });
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.RecipientUserId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_MessageRecipients_Immutable");
                t.HasTrigger("TR_MessageRecipients_Links");
            });
        });

        modelBuilder.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Priority).HasMaxLength(10);
            e.Property(x => x.SourceKey).HasMaxLength(100);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasOne<UserAccount>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Message>().WithMany().HasForeignKey(x => x.MessageId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(x => x.RelatedPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.RelatedDocumentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PatientDocument>().WithMany().HasForeignKey(x => x.RelatedOtherDocumentId).OnDelete(DeleteBehavior.Restrict);
            // one system notification per source event per person, however many times the event is processed; one notification per message per recipient
            e.HasIndex(x => new { x.RecipientUserId, x.SourceKey }).IsUnique().HasFilter("[SourceKey] IS NOT NULL");
            e.HasIndex(x => new { x.MessageId, x.RecipientUserId }).IsUnique().HasFilter("[MessageId] IS NOT NULL");
            // the bell reads this index, not the list
            e.HasIndex(x => new { x.RecipientUserId, x.DeletedAtUtc, x.ReadAtUtc }).IncludeProperties(x => new { x.Priority, x.CreatedAtUtc });
            e.HasIndex(x => x.RelatedPatientId);
            e.ToTable(t =>
            {
                t.HasTrigger("TR_Notifications_Guard");
                t.HasTrigger("TR_Notifications_NoDelete");
                t.HasTrigger("TR_Notifications_Links");
                t.HasCheckConstraint("CK_Notifications_Priority", $"[Priority] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(NotificationPriorities.All)})");
                t.HasCheckConstraint("CK_Notifications_Kind", $"[Kind] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(NotificationKinds.All)})");
                // a message notification names its message and has no source key; a document-duplicate notification names its event, its patient and both documents and has no message
                t.HasCheckConstraint("CK_Notifications_Shape",
                    $"([Kind] = '{NotificationKinds.MessageReceived}' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) " +
                    $"OR ([Kind] = '{NotificationKinds.DocumentDuplicateAdded}' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL " +
                    "AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId])");
                t.HasCheckConstraint("CK_Notifications_SourceKey", "[SourceKey] IS NULL OR (LEN([SourceKey]) > 0 AND [SourceKey] COLLATE Latin1_General_BIN2 NOT LIKE '%[^A-Za-z0-9:._-]%')");
                // an entry is read before it is deleted; both times are real times
                t.HasCheckConstraint("CK_Notifications_Times",
                    $"[CreatedAtUtc] >= {Utc} AND [CreatedAtUtc] < '2200-01-01' AND ([ReadAtUtc] IS NULL OR [ReadAtUtc] >= [CreatedAtUtc]) " +
                    "AND ([DeletedAtUtc] IS NULL OR ([ReadAtUtc] IS NOT NULL AND [DeletedAtUtc] >= [ReadAtUtc]))");
            });
        });

        modelBuilder.Entity<NotificationCursor>(e =>
        {
            e.ToTable("NotificationCursors");
            e.HasKey(x => x.Source);
            e.Property(x => x.Source).HasMaxLength(40);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.ToTable(t =>
            {
                t.HasTrigger("TR_NotificationCursors_Guard");
                t.HasCheckConstraint("CK_NotificationCursors_Source", $"[Source] COLLATE Latin1_General_CS_AS IN ({PrescriptionModel.In(NotificationSources.All)})");
                t.HasCheckConstraint("CK_NotificationCursors_Last", "[LastEventNumber] >= 0");
            });
            // installed at 0 here; the migration then moves it to the current high-water mark of the source (no back-fill)
            e.HasData(new NotificationCursor { Source = NotificationSources.DocumentEvents, LastEventNumber = 0, UpdatedAtUtc = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero) });
        });
    }
}
