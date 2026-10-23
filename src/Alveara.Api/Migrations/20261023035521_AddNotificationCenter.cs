using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SenderUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RelatedPatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InReplyToMessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ThreadId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.CheckConstraint("CK_Messages_Body", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Body], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Body] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [Body] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Messages_Key", "LEN([IdempotencyKey]) > 0 AND [RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_Messages_Priority", "[Priority] COLLATE Latin1_General_CS_AS IN ('Normal','Important','Urgent')");
                    table.CheckConstraint("CK_Messages_Subject", "[Subject] IS NULL OR (LEN(LTRIM(RTRIM([Subject]))) > 0 AND CHARINDEX(NCHAR(0), [Subject] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Subject] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_Messages_Thread", "([InReplyToMessageId] IS NULL AND [ThreadId] = [Id]) OR ([InReplyToMessageId] IS NOT NULL AND [InReplyToMessageId] <> [Id] AND [ThreadId] <> [Id])");
                    table.CheckConstraint("CK_Messages_Time", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_Messages_Messages_InReplyToMessageId",
                        column: x => x.InReplyToMessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_Patients_RelatedPatientId",
                        column: x => x.RelatedPatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Messages_UserAccounts_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationCursors",
                columns: table => new
                {
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    LastEventNumber = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationCursors", x => x.Source);
                    table.CheckConstraint("CK_NotificationCursors_Last", "[LastEventNumber] >= 0");
                    table.CheckConstraint("CK_NotificationCursors_Source", "[Source] COLLATE Latin1_General_CS_AS IN ('DocumentEvents')");
                });

            migrationBuilder.CreateTable(
                name: "MessageRecipients",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageRecipients", x => new { x.MessageId, x.RecipientUserId });
                    table.ForeignKey(
                        name: "FK_MessageRecipients_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MessageRecipients_UserAccounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    MessageId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedPatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RelatedOtherDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SourceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DeletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.CheckConstraint("CK_Notifications_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received')");
                    table.CheckConstraint("CK_Notifications_Priority", "[Priority] COLLATE Latin1_General_CS_AS IN ('Normal','Important','Urgent')");
                    table.CheckConstraint("CK_Notifications_Shape", "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId])");
                    table.CheckConstraint("CK_Notifications_SourceKey", "[SourceKey] IS NULL OR (LEN([SourceKey]) > 0 AND [SourceKey] COLLATE Latin1_General_BIN2 NOT LIKE '%[^A-Za-z0-9:._-]%')");
                    table.CheckConstraint("CK_Notifications_Times", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND ([ReadAtUtc] IS NULL OR [ReadAtUtc] >= [CreatedAtUtc]) AND ([DeletedAtUtc] IS NULL OR ([ReadAtUtc] IS NOT NULL AND [DeletedAtUtc] >= [ReadAtUtc]))");
                    table.ForeignKey(
                        name: "FK_Notifications_Documents_RelatedDocumentId",
                        column: x => x.RelatedDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Documents_RelatedOtherDocumentId",
                        column: x => x.RelatedOtherDocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_Patients_RelatedPatientId",
                        column: x => x.RelatedPatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Notifications_UserAccounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationCursors",
                columns: new[] { "Source", "LastEventNumber", "UpdatedAtUtc" },
                values: new object[] { "DocumentEvents", 0L, new DateTimeOffset(new DateTime(2026, 10, 22, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) });

            // First release: the consumer starts at the high-water mark of the day the center is installed. Events recorded before then never produce notifications (no back-fill).
            migrationBuilder.Sql(@"UPDATE [NotificationCursors] SET [LastEventNumber] = ISNULL((SELECT MAX([EventNumber]) FROM [DocumentEvents]), 0), [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Source] = 'DocumentEvents'");

            migrationBuilder.CreateIndex(
                name: "IX_MessageRecipients_RecipientUserId",
                table: "MessageRecipients",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_InReplyToMessageId",
                table: "Messages",
                column: "InReplyToMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_RelatedPatientId",
                table: "Messages",
                column: "RelatedPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderUserId_CreatedAtUtc",
                table: "Messages",
                columns: new[] { "SenderUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_SenderUserId_IdempotencyKey",
                table: "Messages",
                columns: new[] { "SenderUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ThreadId",
                table: "Messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_MessageId_RecipientUserId",
                table: "Notifications",
                columns: new[] { "MessageId", "RecipientUserId" },
                unique: true,
                filter: "[MessageId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_DeletedAtUtc_ReadAtUtc",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "DeletedAtUtc", "ReadAtUtc" })
                .Annotation("SqlServer:Include", new[] { "Priority", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RecipientUserId_SourceKey",
                table: "Notifications",
                columns: new[] { "RecipientUserId", "SourceKey" },
                unique: true,
                filter: "[SourceKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedDocumentId",
                table: "Notifications",
                column: "RelatedDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedOtherDocumentId",
                table: "Notifications",
                column: "RelatedOtherDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedPatientId",
                table: "Notifications",
                column: "RelatedPatientId");
            // ---- ALV-N015 triggers (errors 51195 to 51203; 51204 and up are free). Messages and their recipients are append-only; a notification only gets a read time and a deleted time, once each; a cursor never moves backward. ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Messages_Immutable] ON [Messages] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51195, 'A message is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Messages_Links] ON [Messages] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Messages] p ON p.[Id] = i.[InReplyToMessageId] WHERE p.[ThreadId] <> i.[ThreadId])
        THROW 51196, 'A reply belongs to the thread of the message it answers.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_MessageRecipients_Immutable] ON [MessageRecipients] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51197, 'The recipients of a message are never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_MessageRecipients_Links] ON [MessageRecipients] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[RecipientUserId] WHERE u.[IsDisabled] = 1)
        THROW 51198, 'A message can only be addressed to an enabled account.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Notifications_Guard] ON [Notifications] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[RecipientUserId] <> d.[RecipientUserId] OR i.[Kind] <> d.[Kind] OR i.[Priority] <> d.[Priority] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc]
                  OR ISNULL(i.[MessageId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[MessageId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[RelatedPatientId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RelatedPatientId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[RelatedDocumentId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RelatedDocumentId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[RelatedOtherDocumentId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RelatedOtherDocumentId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourceKey], N'') <> ISNULL(d.[SourceKey], N''))
        THROW 51199, 'A notification only ever gets a read time and a deleted time.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE (d.[ReadAtUtc] IS NOT NULL AND (i.[ReadAtUtc] IS NULL OR i.[ReadAtUtc] <> d.[ReadAtUtc]))
                  OR (d.[DeletedAtUtc] IS NOT NULL AND (i.[DeletedAtUtc] IS NULL OR i.[DeletedAtUtc] <> d.[DeletedAtUtc])))
        THROW 51199, 'A read time and a deleted time are set once and never change.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Notifications_NoDelete] ON [Notifications] INSTEAD OF DELETE AS
BEGIN
    THROW 51200, 'A notification is never deleted; the person hides it.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Notifications_Links] ON [Notifications] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[RecipientUserId] WHERE u.[IsDisabled] = 1)
        THROW 51201, 'A notification goes to an enabled account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[MessageId] IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [MessageRecipients] r WHERE r.[MessageId] = i.[MessageId] AND r.[RecipientUserId] = i.[RecipientUserId]))
        THROW 51201, 'A message notification goes to a recipient of that message.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Messages] m ON m.[Id] = i.[MessageId]
               WHERE m.[Priority] <> i.[Priority]
                  OR ISNULL(m.[RelatedPatientId], '00000000-0000-0000-0000-000000000000') <> ISNULL(i.[RelatedPatientId], '00000000-0000-0000-0000-000000000000'))
        THROW 51201, 'A message notification carries the priority and the patient link of its message.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] d ON d.[Id] = i.[RelatedDocumentId] WHERE d.[PatientId] <> i.[RelatedPatientId])
        THROW 51201, 'A document notification names documents of its patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] d ON d.[Id] = i.[RelatedOtherDocumentId] WHERE d.[PatientId] <> i.[RelatedPatientId])
        THROW 51201, 'A document notification names documents of its patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_NotificationCursors_Guard] ON [NotificationCursors] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51202, 'A notification cursor is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Source] = i.[Source] WHERE i.[LastEventNumber] < d.[LastEventNumber])
        THROW 51202, 'A notification cursor only moves forward.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while nothing has been sent or notified: afterwards these tables are records that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Messages]) OR EXISTS (SELECT 1 FROM [Notifications])
    THROW 51203, 'Messages or notifications exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "MessageRecipients");

            migrationBuilder.DropTable(
                name: "NotificationCursors");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "Messages");
        }
    }
}
