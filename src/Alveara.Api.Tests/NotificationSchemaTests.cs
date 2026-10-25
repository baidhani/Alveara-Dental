using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Notifications;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N015, the database on its own: rows written straight to the tables, around any service, so the tests prove the database refuses by itself what the rules refuse. Messages and their recipient rows are
/// append-only (51195, 51197), a reply stays in its thread (51196), nobody can address a disabled account (51198, 51201), a notification only ever gets a read time and a deleted time, once each, and cannot be
/// deleted unread (51199, constraint) or physically (51200), a notification belongs to a recipient of its message and carries its priority and patient link (51201), a system notification is unique per source
/// event and recipient, a cursor only moves forward (51202), the cursor starts at the high-water mark of the document events present when the center is installed, and the migration rolls back and forward
/// while nothing was sent and refuses to roll back over a message or a notification (51203).
/// </summary>
public class NotificationSchemaTests : SafetyTestBase
{
    private const string BeforeNotifications = "20261020173300_AddDocumentLibrary";
    private static readonly string Hash = new('a', 64);

    // ---------- helpers ----------

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

    private async Task<Microsoft.Data.SqlClient.SqlException> SqlRefusalAsync(FormattableString sql)
    {
        await using var db = Fixture.CreateContext();
        return await Refusal(() => db.Database.ExecuteSqlAsync(sql));
    }

    /// <summary>The database refused: by the trigger (51199) or, when the new shape is itself invalid, by a check constraint (547) that is evaluated first.</summary>
    private static void Refused(Microsoft.Data.SqlClient.SqlException e) => Assert.True(e.Number is 51199 or 547, $"unexpected error {e.Number}: {e.Message}");

    private async Task<int> CountAsync<T>() where T : class { await using var db = Fixture.CreateContext(); return await db.Set<T>().CountAsync(); }

    private async Task<Guid> DisabledUserAsync()
    {
        var id = Guid.NewGuid();
        await AddAsync(new UserAccount { Id = id, Username = $"off-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow, IsDisabled = true });
        return id;
    }

    private Message NewMessage(Action<Message>? change = null)
    {
        var id = Guid.NewGuid();
        var m = new Message { Id = id, SenderUserId = S.Actor, Subject = "Room 3", Body = "Please set up room 3 for the 10:30.", Priority = NotificationPriorities.Normal, ThreadId = id, CreatedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"k-{Guid.NewGuid():N}", RequestHash = Hash };
        change?.Invoke(m);
        return m;
    }

    private async Task<Message> SentAsync(Action<Message>? change = null, params Guid[] recipients)
    {
        var m = NewMessage(change);
        await using var db = Fixture.CreateContext();
        db.Messages.Add(m);
        foreach (var r in recipients.Length == 0 ? [Other] : recipients) db.MessageRecipients.Add(new MessageRecipient { MessageId = m.Id, RecipientUserId = r });
        await db.SaveChangesAsync();
        return m;
    }

    private Notification MessageNotification(Message m, Guid? recipient = null, Action<Notification>? change = null)
    {
        var n = new Notification { Id = Guid.NewGuid(), RecipientUserId = recipient ?? Other, Kind = NotificationKinds.MessageReceived, Priority = m.Priority, MessageId = m.Id, RelatedPatientId = m.RelatedPatientId, CreatedAtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(n);
        return n;
    }

    private PatientDocument NewDocument(Guid? patient = null) => new()
    {
        Id = Guid.NewGuid(), PatientId = patient ?? Ann, CategoryId = DocumentCategorySeeds.Radiograph, DocumentDate = new DateOnly(2026, 10, 1), Description = "Bitewing.", OriginalFileName = "bitewing.png", DetectedType = "png",
        ContentType = "image/png", SizeBytes = 2048, Sha256 = Hash, BlobId = Guid.NewGuid(), ImportedByUserId = S.Actor, ImportedAtUtc = DateTimeOffset.UtcNow, IdempotencyKey = $"doc-{Guid.NewGuid():N}", RequestHash = Hash,
    };

    private async Task<(PatientDocument First, PatientDocument Second)> TwoDocumentsAsync(Guid? patient = null)
    {
        var a = NewDocument(patient);
        var b = NewDocument(patient);
        b.DuplicateOfDocumentId = a.Id;
        await using var db = Fixture.CreateContext();
        db.Documents.Add(a);
        await db.SaveChangesAsync();
        db.Documents.Add(b);
        await db.SaveChangesAsync();
        return (a, b);
    }

    private Notification DocumentNotification((PatientDocument First, PatientDocument Second) docs, Guid? recipient = null, Action<Notification>? change = null)
    {
        var n = new Notification
        {
            Id = Guid.NewGuid(), RecipientUserId = recipient ?? Other, Kind = NotificationKinds.DocumentDuplicateAdded, Priority = NotificationPriorities.Important, RelatedPatientId = docs.First.PatientId,
            RelatedDocumentId = docs.Second.Id, RelatedOtherDocumentId = docs.First.Id, SourceKey = $"DocumentEvents:{Random.Shared.NextInt64(1, 1_000_000)}", CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        change?.Invoke(n);
        return n;
    }

    // ---------- what the migration seeds ----------

    [Fact]
    public async Task The_migration_seeds_one_cursor_for_the_document_events_at_zero_on_an_empty_database_and_creates_no_message_or_notification()
    {
        await using var db = Fixture.CreateContext();
        var cursor = await db.NotificationCursors.AsNoTracking().SingleAsync();
        Assert.Equal((NotificationSources.DocumentEvents, 0L), (cursor.Source, cursor.LastEventNumber));
        Assert.Equal((0, 0, 0), (await db.Messages.CountAsync(), await db.MessageRecipients.CountAsync(), await db.Notifications.CountAsync()));
    }

    // ---------- messages ----------

    [Fact]
    public async Task A_message_with_a_subject_a_multiline_body_and_every_priority_is_stored()
    {
        foreach (var p in NotificationPriorities.All)
            await SentAsync(m => { m.Priority = p; m.Body = "Line one\r\n\tLine two"; });
        await SentAsync(m => m.Subject = null);
        Assert.Equal(4, await CountAsync<Message>());
    }

    [Theory]
    [InlineData("priority-urgent-lowercase")]
    [InlineData("priority-unknown")]
    [InlineData("subject-blank")]
    [InlineData("subject-control-character")]
    [InlineData("body-blank")]
    [InlineData("body-control-character")]
    [InlineData("body-over-2000")]
    [InlineData("subject-over-120")]
    [InlineData("request-hash-not-hex")]
    [InlineData("request-hash-short")]
    [InlineData("key-blank")]
    [InlineData("time-before-2000")]
    [InlineData("time-after-2200")]
    [InlineData("root-with-another-thread")]
    [InlineData("reply-answering-itself")]
    [InlineData("reply-in-its-own-thread")]
    public async Task The_database_refuses_an_incorrect_message(string flaw)
    {
        var root = await SentAsync();
        var m = NewMessage(x =>
        {
            switch (flaw)
            {
                case "priority-urgent-lowercase": x.Priority = "urgent"; break;
                case "priority-unknown": x.Priority = "High"; break;
                case "subject-blank": x.Subject = "   "; break;
                case "subject-control-character": x.Subject = "Room\u0007 3"; break;
                case "body-blank": x.Body = "  \r\n "; break;
                case "body-control-character": x.Body = "Hello\u0000there"; break;
                case "body-over-2000": x.Body = new string('x', MessageRules.BodyMaxLength + 1); break;
                case "subject-over-120": x.Subject = new string('s', MessageRules.SubjectMaxLength + 1); break;
                case "request-hash-not-hex": x.RequestHash = new string('z', 64); break;
                case "request-hash-short": x.RequestHash = "abc"; break;
                case "key-blank": x.IdempotencyKey = ""; break;
                case "time-before-2000": x.CreatedAtUtc = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero); break;
                case "time-after-2200": x.CreatedAtUtc = new DateTimeOffset(2200, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
                case "root-with-another-thread": x.ThreadId = Guid.NewGuid(); break;
                case "reply-answering-itself": x.InReplyToMessageId = x.Id; x.ThreadId = root.ThreadId; break;
                case "reply-in-its-own-thread": x.InReplyToMessageId = root.Id; break;
            }
        });
        await Refusal(() => AddAsync(m));
        Assert.Equal(1, await CountAsync<Message>());                                                             // only the root
    }

    [Fact]
    public async Task A_reply_keeps_the_thread_of_the_message_it_answers_and_a_reply_into_another_thread_is_refused()
    {
        var root = await SentAsync();
        var reply = await SentAsync(m => { m.InReplyToMessageId = root.Id; m.ThreadId = root.ThreadId; m.SenderUserId = Other; }, S.Actor);
        Assert.Equal(root.ThreadId, reply.ThreadId);

        var elsewhere = Guid.NewGuid();
        var e = await Refusal(() => AddAsync(NewMessage(m => { m.InReplyToMessageId = root.Id; m.ThreadId = elsewhere; })));
        Assert.Equal(51196, e.Number);
    }

    [Fact]
    public async Task The_same_sender_cannot_reuse_a_key_but_another_sender_can()
    {
        var first = await SentAsync(m => m.IdempotencyKey = "same-key");
        await Refusal(() => AddAsync(NewMessage(m => { m.IdempotencyKey = "same-key"; })));
        await SentAsync(m => { m.IdempotencyKey = "same-key"; m.SenderUserId = Other; }, S.Actor);
        Assert.Equal(2, await CountAsync<Message>());
        Assert.NotNull(first);
    }

    [Fact]
    public async Task A_message_can_carry_a_patient_link_and_the_patient_must_exist()
    {
        await SentAsync(m => m.RelatedPatientId = Ann);
        await Refusal(() => AddAsync(NewMessage(m => m.RelatedPatientId = Guid.NewGuid())));
    }

    [Fact]
    public async Task A_message_is_never_updated_or_deleted_not_even_by_direct_sql()
    {
        var m = await SentAsync();
        Assert.Equal(51195, (await SqlRefusalAsync($"UPDATE [Messages] SET [Body] = N'changed' WHERE [Id] = {m.Id}")).Number);
        Assert.Equal(51195, (await SqlRefusalAsync($"UPDATE [Messages] SET [Priority] = 'Urgent' WHERE [Id] = {m.Id}")).Number);
        Assert.Equal(51195, (await SqlRefusalAsync($"DELETE FROM [Messages] WHERE [Id] = {m.Id}")).Number);
        Assert.Equal(51195, (await SqlRefusalAsync($"DELETE FROM [Messages]")).Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal("Please set up room 3 for the 10:30.", (await db.Messages.AsNoTracking().SingleAsync()).Body);
    }

    // ---------- recipients ----------

    [Fact]
    public async Task A_message_has_one_row_per_recipient_and_a_recipient_cannot_be_listed_twice()
    {
        var m = await SentAsync(null, Other, S.Actor);
        Assert.Equal(2, await CountAsync<MessageRecipient>());
        await Refusal(() => AddAsync(new MessageRecipient { MessageId = m.Id, RecipientUserId = Other }));
    }

    [Fact]
    public async Task A_message_cannot_be_addressed_to_a_disabled_account_or_an_unknown_one()
    {
        var off = await DisabledUserAsync();
        var m = NewMessage();
        await using (var db = Fixture.CreateContext()) { db.Messages.Add(m); await db.SaveChangesAsync(); }
        Assert.Equal(51198, (await Refusal(() => AddAsync(new MessageRecipient { MessageId = m.Id, RecipientUserId = off }))).Number);
        await Refusal(() => AddAsync(new MessageRecipient { MessageId = m.Id, RecipientUserId = Guid.NewGuid() }));
        Assert.Equal(0, await CountAsync<MessageRecipient>());
    }

    [Fact]
    public async Task The_recipient_rows_of_a_message_are_never_updated_or_deleted()
    {
        var m = await SentAsync(null, Other);
        Assert.Equal(51197, (await SqlRefusalAsync($"UPDATE [MessageRecipients] SET [RecipientUserId] = {S.Actor} WHERE [MessageId] = {m.Id}")).Number);
        Assert.Equal(51197, (await SqlRefusalAsync($"DELETE FROM [MessageRecipients] WHERE [MessageId] = {m.Id}")).Number);
        Assert.Equal(1, await CountAsync<MessageRecipient>());
    }

    // ---------- notifications ----------

    [Fact]
    public async Task A_message_notification_and_a_document_notification_are_stored()
    {
        var m = await SentAsync(x => { x.Priority = NotificationPriorities.Urgent; x.RelatedPatientId = Ann; });
        await AddAsync(MessageNotification(m));
        await AddAsync(DocumentNotification(await TwoDocumentsAsync()));
        Assert.Equal(2, await CountAsync<Notification>());
    }

    [Theory]
    [InlineData("kind-unknown")]
    [InlineData("priority-unknown")]
    [InlineData("message-kind-without-message")]
    [InlineData("message-kind-with-source-key")]
    [InlineData("document-kind-without-source-key")]
    [InlineData("document-kind-with-message")]
    [InlineData("document-kind-without-patient")]
    [InlineData("document-kind-same-document-twice")]
    [InlineData("source-key-blank")]
    [InlineData("source-key-bad-characters")]
    [InlineData("read-before-created")]
    [InlineData("deleted-without-read")]
    [InlineData("deleted-before-read")]
    [InlineData("created-before-2000")]
    public async Task The_database_refuses_an_incorrect_notification(string flaw)
    {
        var m = await SentAsync();
        var docs = await TwoDocumentsAsync();
        var now = DateTimeOffset.UtcNow;
        var isDocument = flaw.StartsWith("document-") || flaw.StartsWith("source-key");
        var n = isDocument ? DocumentNotification(docs) : MessageNotification(m);
        switch (flaw)
        {
            case "kind-unknown": n.Kind = "message.other"; break;
            case "priority-unknown": n.Priority = "Loud"; break;
            case "message-kind-without-message": n.MessageId = null; break;
            case "message-kind-with-source-key": n.SourceKey = "DocumentEvents:1"; break;
            case "document-kind-without-source-key": n.SourceKey = null; break;
            case "document-kind-with-message": n.MessageId = m.Id; break;
            case "document-kind-without-patient": n.RelatedPatientId = null; break;
            case "document-kind-same-document-twice": n.RelatedOtherDocumentId = n.RelatedDocumentId; break;
            case "source-key-blank": n.SourceKey = ""; break;
            case "source-key-bad-characters": n.SourceKey = "DocumentEvents: 1 or 1=1"; break;
            case "read-before-created": n.ReadAtUtc = n.CreatedAtUtc.AddSeconds(-1); break;
            case "deleted-without-read": n.DeletedAtUtc = now; break;
            case "deleted-before-read": n.ReadAtUtc = now; n.DeletedAtUtc = now.AddSeconds(-5); break;
            case "created-before-2000": n.CreatedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero); break;
        }
        await Refusal(() => AddAsync(n));
        Assert.Equal(0, await CountAsync<Notification>());
    }

    [Fact]
    public async Task A_notification_can_be_marked_read_once_and_then_hidden_once_and_nothing_else_about_it_can_change()
    {
        var m = await SentAsync();
        var n = MessageNotification(m);
        await AddAsync(n);
        var read = DateTimeOffset.UtcNow.AddSeconds(1);
        var hidden = read.AddSeconds(1);

        await SqlRefusalAsync($"UPDATE [Notifications] SET [DeletedAtUtc] = {hidden} WHERE [Id] = {n.Id}");     // not before it is read
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [Notifications] SET [ReadAtUtc] = {read} WHERE [Id] = {n.Id}");
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [Notifications] SET [ReadAtUtc] = {read} WHERE [Id] = {n.Id}");      // the same value again is quiet
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [Notifications] SET [DeletedAtUtc] = {hidden} WHERE [Id] = {n.Id}");

        Refused(await SqlRefusalAsync($"UPDATE [Notifications] SET [ReadAtUtc] = {read.AddMinutes(1)} WHERE [Id] = {n.Id}"));          // a read time never changes (the trigger, or the deleted-after-read constraint that already refuses the new shape)
        Refused(await SqlRefusalAsync($"UPDATE [Notifications] SET [ReadAtUtc] = NULL WHERE [Id] = {n.Id}"));                           // and is never cleared
        Assert.Equal(51199, (await SqlRefusalAsync($"UPDATE [Notifications] SET [DeletedAtUtc] = {hidden.AddMinutes(1)} WHERE [Id] = {n.Id}")).Number);       // a deleted time never changes
        Assert.Equal(51199, (await SqlRefusalAsync($"UPDATE [Notifications] SET [DeletedAtUtc] = NULL WHERE [Id] = {n.Id}")).Number);                        // and a hidden entry never comes back
        await using var check = Fixture.CreateContext();
        var stored = await check.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal((read, hidden), (stored.ReadAtUtc, stored.DeletedAtUtc));
    }

    [Theory]
    [InlineData("Kind")]
    [InlineData("Priority")]
    [InlineData("RecipientUserId")]
    [InlineData("MessageId")]
    [InlineData("RelatedPatientId")]
    [InlineData("SourceKey")]
    [InlineData("CreatedAtUtc")]
    public async Task Nothing_about_a_notification_but_its_read_and_deleted_times_can_be_changed(string column)
    {
        var m = await SentAsync(x => x.RelatedPatientId = Ann);
        var n = MessageNotification(m);
        await AddAsync(n);
        var e = column switch
        {
            "Kind" => await SqlRefusalAsync($"UPDATE [Notifications] SET [Kind] = 'document.duplicate_added' WHERE [Id] = {n.Id}"),
            "Priority" => await SqlRefusalAsync($"UPDATE [Notifications] SET [Priority] = 'Urgent' WHERE [Id] = {n.Id}"),
            "RecipientUserId" => await SqlRefusalAsync($"UPDATE [Notifications] SET [RecipientUserId] = {S.Actor} WHERE [Id] = {n.Id}"),
            "MessageId" => await SqlRefusalAsync($"UPDATE [Notifications] SET [MessageId] = NULL WHERE [Id] = {n.Id}"),
            "RelatedPatientId" => await SqlRefusalAsync($"UPDATE [Notifications] SET [RelatedPatientId] = NULL WHERE [Id] = {n.Id}"),
            "SourceKey" => await SqlRefusalAsync($"UPDATE [Notifications] SET [SourceKey] = N'DocumentEvents:9' WHERE [Id] = {n.Id}"),
            _ => await SqlRefusalAsync($"UPDATE [Notifications] SET [CreatedAtUtc] = DATEADD(day, 1, [CreatedAtUtc]) WHERE [Id] = {n.Id}"),
        };
        Assert.True(e.Number is 51199 or 547, $"unexpected error {e.Number}: {e.Message}");              // the trigger, or a constraint that already refuses the new shape
    }

    [Fact]
    public async Task A_notification_is_never_physically_deleted()
    {
        var m = await SentAsync();
        var n = MessageNotification(m);
        await AddAsync(n);
        Assert.Equal(51200, (await SqlRefusalAsync($"DELETE FROM [Notifications] WHERE [Id] = {n.Id}")).Number);
        Assert.Equal(51200, (await SqlRefusalAsync($"DELETE FROM [Notifications]")).Number);
        Assert.Equal(1, await CountAsync<Notification>());
    }

    [Fact]
    public async Task A_system_notification_is_unique_per_source_event_and_recipient_and_a_message_notification_is_unique_per_recipient()
    {
        var docs = await TwoDocumentsAsync();
        await AddAsync(DocumentNotification(docs, Other, n => n.SourceKey = "DocumentEvents:7"));
        await Refusal(() => AddAsync(DocumentNotification(docs, Other, n => n.SourceKey = "DocumentEvents:7")));   // the same event, the same person: refused
        await AddAsync(DocumentNotification(docs, S.Actor, n => n.SourceKey = "DocumentEvents:7"));               // the same event, another person: fine
        await AddAsync(DocumentNotification(docs, Other, n => n.SourceKey = "DocumentEvents:8"));                 // another event, the same person: fine

        var m = await SentAsync(null, Other);
        await AddAsync(MessageNotification(m));
        await Refusal(() => AddAsync(MessageNotification(m)));
        Assert.Equal(4, await CountAsync<Notification>());
    }

    [Fact]
    public async Task A_notification_cannot_go_to_a_disabled_account_to_a_non_recipient_or_carry_another_priority_or_patient_than_its_message()
    {
        var off = await DisabledUserAsync();
        var m = await SentAsync(x => { x.Priority = NotificationPriorities.Important; x.RelatedPatientId = Ann; }, Other);
        var docs = await TwoDocumentsAsync();
        Assert.Equal(51201, (await Refusal(() => AddAsync(DocumentNotification(docs, off)))).Number);
        Assert.Equal(51201, (await Refusal(() => AddAsync(MessageNotification(m, S.Actor)))).Number);                               // S.Actor is not a recipient
        Assert.Equal(51201, (await Refusal(() => AddAsync(MessageNotification(m, Other, n => n.Priority = NotificationPriorities.Normal)))).Number);
        Assert.Equal(51201, (await Refusal(() => AddAsync(MessageNotification(m, Other, n => n.RelatedPatientId = Bo)))).Number);
        Assert.Equal(51201, (await Refusal(() => AddAsync(MessageNotification(m, Other, n => n.RelatedPatientId = null)))).Number);
        await AddAsync(MessageNotification(m, Other));
        Assert.Equal(1, await CountAsync<Notification>());
    }

    [Fact]
    public async Task A_document_notification_must_name_documents_of_its_patient()
    {
        var docs = await TwoDocumentsAsync();
        Assert.Equal(51201, (await Refusal(() => AddAsync(DocumentNotification(docs, Other, n => n.RelatedPatientId = Bo)))).Number);
        await AddAsync(DocumentNotification(docs));
    }

    // ---------- cursor ----------

    [Fact]
    public async Task A_cursor_only_moves_forward_and_is_never_deleted()
    {
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [NotificationCursors] SET [LastEventNumber] = 5 WHERE [Source] = 'DocumentEvents'");
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [NotificationCursors] SET [LastEventNumber] = 5 WHERE [Source] = 'DocumentEvents'");    // staying put is quiet
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlAsync($"UPDATE [NotificationCursors] SET [LastEventNumber] = 9 WHERE [Source] = 'DocumentEvents'");    // forward by more than one is fine (batches)
        Assert.Equal(51202, (await SqlRefusalAsync($"UPDATE [NotificationCursors] SET [LastEventNumber] = 8 WHERE [Source] = 'DocumentEvents'")).Number);
        Assert.Equal(51202, (await SqlRefusalAsync($"DELETE FROM [NotificationCursors]")).Number);
        await using var check = Fixture.CreateContext();
        Assert.Equal(9L, (await check.NotificationCursors.AsNoTracking().SingleAsync()).LastEventNumber);
    }

    [Fact]
    public async Task A_cursor_has_a_known_source_and_a_non_negative_position_and_there_is_one_per_source()
    {
        await Refusal(() => AddAsync(new NotificationCursor { Source = "SomethingElse", LastEventNumber = 0, UpdatedAtUtc = DateTimeOffset.UtcNow }));
        await Refusal(() => AddAsync(new NotificationCursor { Source = NotificationSources.DocumentEvents, LastEventNumber = 0, UpdatedAtUtc = DateTimeOffset.UtcNow }));
        await SqlRefusalAsync($"UPDATE [NotificationCursors] SET [LastEventNumber] = -1 WHERE [Source] = 'DocumentEvents'");
        Assert.Equal(1, await CountAsync<NotificationCursor>());
    }

    // ---------- the migration ----------

    [Fact]
    public async Task The_cursor_starts_at_the_high_water_mark_of_the_document_events_present_when_the_center_is_installed_and_never_back_fills()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeNotifications);
        var docs = await TwoDocumentsAsync();
        long last;
        await using (var db = Fixture.CreateContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            for (var i = 0; i < 3; i++)
            {
                await db.Database.ExecuteSqlAsync($"UPDATE [DocumentEventCounter] SET [LastEventNumber] = [LastEventNumber] + 1 WHERE [Id] = 1");
                var number = await db.Database.SqlQuery<long>($"SELECT [LastEventNumber] AS [Value] FROM [DocumentEventCounter] WHERE [Id] = 1").SingleAsync();
                db.DocumentEvents.Add(new DocumentEvent { EventNumber = number, Kind = DocumentEventKinds.ImportedAsDuplicate, DocumentId = docs.Second.Id, RelatedDocumentId = docs.First.Id, PatientId = Ann, ByUserId = S.Actor, AtUtc = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }
            await tx.CommitAsync();
            last = await db.DocumentEvents.MaxAsync(e => e.EventNumber);
        }
        Assert.True(last >= 3);

        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using var check = Fixture.CreateContext();
        Assert.Equal(last, (await check.NotificationCursors.AsNoTracking().SingleAsync()).LastEventNumber);       // events that already exist produce no notification later
        Assert.Equal(0, await check.Notifications.CountAsync());
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_nothing_was_sent_and_refuses_to_roll_back_over_a_message_or_a_notification()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeNotifications);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('Messages','MessageRecipients','Notifications','NotificationCursors')").SingleAsync());
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(4, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('Messages','MessageRecipients','Notifications','NotificationCursors')").SingleAsync());
            Assert.Equal(8, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE (name LIKE 'TR[_]Message%' OR name LIKE 'TR[_]Notification%') AND name NOT LIKE '%[_]MergeScope'").SingleAsync());
            Assert.Equal(1, await db.NotificationCursors.CountAsync());                                           // seeded once, again after the roll forward
        }

        var m = await SentAsync();
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeNotifications); });
        Assert.Equal(51203, e.Number);
        Assert.Equal(1, await CountAsync<Message>());                                                             // the message is still there
        Assert.NotEqual(Guid.Empty, m.Id);
    }
}
