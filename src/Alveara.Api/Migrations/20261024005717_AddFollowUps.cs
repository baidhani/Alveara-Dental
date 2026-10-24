using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications");

            migrationBuilder.CreateTable(
                name: "FollowUpTasks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RecallIntervalDays = table.Column<int>(type: "int", nullable: true),
                    RecallOfTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosureNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpTasks", x => x.Id);
                    table.CheckConstraint("CK_FollowUpTasks_Closure", "([Status] = 'Open' AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] <> 'Open' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_FollowUpTasks_ClosureNote", "[ClosureNote] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([ClosureNote], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [ClosureNote] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [ClosureNote] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_FollowUpTasks_Interval", "[RecallIntervalDays] IS NULL OR ([RecallIntervalDays] >= 1 AND [RecallIntervalDays] <= 3650)");
                    table.CheckConstraint("CK_FollowUpTasks_Note", "[Note] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Note], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Note] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [Note] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_FollowUpTasks_Recall", "[RecallOfTaskId] IS NULL OR [RecallOfTaskId] <> [Id]");
                    table.CheckConstraint("CK_FollowUpTasks_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Open','Completed','Cancelled')");
                    table.CheckConstraint("CK_FollowUpTasks_Times", "[DueAtUtc] >= '2000-01-01' AND [DueAtUtc] < '2200-01-01' AND [CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [CreatedAtUtc])");
                    table.CheckConstraint("CK_FollowUpTasks_Title", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Title], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Title] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Title] COLLATE Latin1_General_BIN2) = 0");
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_FollowUpTasks_RecallOfTaskId",
                        column: x => x.RecallOfTaskId,
                        principalTable: "FollowUpTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_UserAccounts_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_UserAccounts_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FollowUpReminderDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Channel = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpReminderDeliveries", x => x.Id);
                    table.CheckConstraint("CK_FollowUpReminderDeliveries_Channel", "[Channel] COLLATE Latin1_General_CS_AS IN ('InApp')");
                    table.CheckConstraint("CK_FollowUpReminderDeliveries_Outcome", "[Outcome] COLLATE Latin1_General_CS_AS IN ('Delivered','Fallback')");
                    table.CheckConstraint("CK_FollowUpReminderDeliveries_Time", "[DueAtUtc] >= '2000-01-01' AND [DueAtUtc] < '2200-01-01' AND [CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_FollowUpReminderDeliveries_FollowUpTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "FollowUpTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpReminderDeliveries_UserAccounts_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FollowUpTaskEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpTaskEvents", x => x.Id);
                    table.CheckConstraint("CK_FollowUpTaskEvents_Actor", "[ActorUserId] IS NOT NULL OR [Kind] = 'Reminded'");
                    table.CheckConstraint("CK_FollowUpTaskEvents_Detail", "[Detail] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Detail], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Detail] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Detail] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_FollowUpTaskEvents_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Created','Edited','Reassigned','DueChanged','Reminded','Completed','Cancelled','RecallCreated')");
                    table.CheckConstraint("CK_FollowUpTaskEvents_Time", "[OccurredAtUtc] >= '2000-01-01' AND [OccurredAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_FollowUpTaskEvents_FollowUpTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "FollowUpTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpTaskEvents_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received','followup.due')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId]) OR ([Kind] = 'followup.due' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpReminderDeliveries_RecipientUserId",
                table: "FollowUpReminderDeliveries",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpReminderDeliveries_TaskId_DueAtUtc_Channel",
                table: "FollowUpReminderDeliveries",
                columns: new[] { "TaskId", "DueAtUtc", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTaskEvents_ActorUserId",
                table: "FollowUpTaskEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTaskEvents_TaskId_OccurredAtUtc",
                table: "FollowUpTaskEvents",
                columns: new[] { "TaskId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_AssigneeUserId_Status_DueAtUtc",
                table: "FollowUpTasks",
                columns: new[] { "AssigneeUserId", "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_ClosedByUserId",
                table: "FollowUpTasks",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_CreatedByUserId",
                table: "FollowUpTasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_PatientId_Status_DueAtUtc",
                table: "FollowUpTasks",
                columns: new[] { "PatientId", "Status", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_RecallOfTaskId",
                table: "FollowUpTasks",
                column: "RecallOfTaskId",
                unique: true,
                filter: "[RecallOfTaskId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_Status_DueAtUtc",
                table: "FollowUpTasks",
                columns: new[] { "Status", "DueAtUtc" });

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FollowUpTaskEvents_Immutable] ON [FollowUpTaskEvents] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51209, 'A follow-up task event is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FollowUpReminderDeliveries_Immutable] ON [FollowUpReminderDeliveries] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51212, 'A follow-up reminder delivery is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FollowUpTasks_Guard] ON [FollowUpTasks] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51210, 'A follow-up task is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] <> 'Open')
        THROW 51210, 'A completed or cancelled follow-up task is final and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreatedByUserId] <> d.[CreatedByUserId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc]
                  OR ISNULL(i.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000'))
        THROW 51210, 'A follow-up task keeps its patient, creator, creation time and recall link.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FollowUpTasks_Links] ON [FollowUpTasks] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[AssigneeUserId]
               WHERE u.[IsDisabled] = 1 AND i.[Status] = 'Open'
                 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[AssigneeUserId] = i.[AssigneeUserId]))
        THROW 51211, 'An open follow-up task is assigned to an enabled account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [FollowUpTasks] p ON p.[Id] = i.[RecallOfTaskId] WHERE p.[PatientId] <> i.[PatientId])
        THROW 51211, 'A recall belongs to the patient of the task it follows.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no follow-up task exists: afterwards these tables are records that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [FollowUpTasks])
    THROW 51213, 'Follow-up tasks exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "FollowUpReminderDeliveries");

            migrationBuilder.DropTable(
                name: "FollowUpTaskEvents");

            migrationBuilder.DropTable(
                name: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId])");
        }
    }
}
