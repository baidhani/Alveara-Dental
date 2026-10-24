using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Closure",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTaskEvents_Actor",
                table: "FollowUpTaskEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTaskEvents_Kind",
                table: "FollowUpTaskEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpReminderDeliveries_Outcome",
                table: "FollowUpReminderDeliveries");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AlterColumn<Guid>(
                name: "AssigneeUserId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "AppointmentId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ClosedBySystem",
                table: "FollowUpTasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "FollowUpTasks",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "Priority",
                table: "FollowUpTasks",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Normal");

            migrationBuilder.AddColumn<string>(
                name: "SourceKey",
                table: "FollowUpTasks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourcePlanItemId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatientRecallPlans",
                columns: table => new
                {
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: true),
                    OptedOut = table.Column<bool>(type: "bit", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientRecallPlans", x => x.PatientId);
                    table.CheckConstraint("CK_PatientRecallPlans_Interval", "[IntervalDays] IS NULL OR ([IntervalDays] >= 1 AND [IntervalDays] <= 3650)");
                    table.CheckConstraint("CK_PatientRecallPlans_Means", "[OptedOut] = 1 OR [IntervalDays] IS NOT NULL");
                    table.CheckConstraint("CK_PatientRecallPlans_Reason", "[Reason] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Reason], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Reason] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_PatientRecallPlans_Time", "[UpdatedAtUtc] >= '2000-01-01' AND [UpdatedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_PatientRecallPlans_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientRecallPlans_UserAccounts_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RecallSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    DefaultIntervalDays = table.Column<int>(type: "int", nullable: false),
                    LeadDays = table.Column<int>(type: "int", nullable: false),
                    MaxVisitAgeMonths = table.Column<int>(type: "int", nullable: false),
                    DueSoonDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecallSettings", x => x.Id);
                    table.CheckConstraint("CK_RecallSettings_DueSoon", "[DueSoonDays] >= 1 AND [DueSoonDays] <= 30");
                    table.CheckConstraint("CK_RecallSettings_Interval", "[DefaultIntervalDays] >= 1 AND [DefaultIntervalDays] <= 3650");
                    table.CheckConstraint("CK_RecallSettings_Lead", "[LeadDays] >= 0 AND [LeadDays] <= 365");
                    table.CheckConstraint("CK_RecallSettings_OneRow", "[Id] = 1");
                    table.CheckConstraint("CK_RecallSettings_VisitAge", "[MaxVisitAgeMonths] >= 1 AND [MaxVisitAgeMonths] <= 120");
                    table.ForeignKey(
                        name: "FK_RecallSettings_UserAccounts_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RecallSettings",
                columns: new[] { "Id", "DefaultIntervalDays", "DueSoonDays", "Enabled", "LeadDays", "MaxVisitAgeMonths", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[] { 1, 180, 7, false, 30, 24, new DateTimeOffset(new DateTime(2026, 10, 24, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_AppointmentId",
                table: "FollowUpTasks",
                column: "AppointmentId",
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_Open_Unassigned",
                table: "FollowUpTasks",
                column: "DueAtUtc",
                filter: "[AssigneeUserId] IS NULL AND [Status] = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_SourceKey",
                table: "FollowUpTasks",
                column: "SourceKey",
                unique: true,
                filter: "[SourceKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_SourcePlanItemId",
                table: "FollowUpTasks",
                column: "SourcePlanItemId",
                filter: "[SourcePlanItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_Status_Kind_DueAtUtc",
                table: "FollowUpTasks",
                columns: new[] { "Status", "Kind", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_Status_Priority_DueAtUtc",
                table: "FollowUpTasks",
                columns: new[] { "Status", "Priority", "DueAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Closure",
                table: "FollowUpTasks",
                sql: "([Status] = 'Open' AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL AND [ClosedBySystem] = 0) OR ([Status] <> 'Open' AND [ClosedAtUtc] IS NOT NULL AND (([ClosedByUserId] IS NOT NULL AND [ClosedBySystem] = 0) OR ([ClosedByUserId] IS NULL AND [ClosedBySystem] = 1)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Creator",
                table: "FollowUpTasks",
                sql: "[CreatedByUserId] IS NOT NULL OR [Kind] <> 'Manual'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Kind",
                table: "FollowUpTasks",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Manual','Recall','UnscheduledTreatment')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Priority",
                table: "FollowUpTasks",
                sql: "[Priority] COLLATE Latin1_General_CS_AS IN ('Low','Normal','High','Urgent')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Source",
                table: "FollowUpTasks",
                sql: "([Kind] = 'Manual' AND [SourcePlanItemId] IS NULL AND [SourceKey] IS NULL) OR ([Kind] = 'Recall' AND [SourcePlanItemId] IS NULL AND [SourceKey] IS NOT NULL) OR ([Kind] = 'UnscheduledTreatment' AND [SourcePlanItemId] IS NOT NULL AND [SourceKey] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_SourceKey",
                table: "FollowUpTasks",
                sql: "[SourceKey] IS NULL OR (LEN([SourceKey]) > 0 AND [SourceKey] COLLATE Latin1_General_BIN2 NOT LIKE '%[^A-Za-z0-9:._]%')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTaskEvents_Actor",
                table: "FollowUpTaskEvents",
                sql: "[ActorUserId] IS NOT NULL OR [Kind] IN ('Reminded','SystemCreated','SystemClosed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTaskEvents_Kind",
                table: "FollowUpTaskEvents",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Created','Edited','Reassigned','DueChanged','Reminded','Completed','Cancelled','RecallCreated','Assigned','PriorityChanged','SystemCreated','SystemClosed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpReminderDeliveries_Outcome",
                table: "FollowUpReminderDeliveries",
                sql: "[Outcome] COLLATE Latin1_General_CS_AS IN ('Delivered','Fallback','Unassigned')");

            migrationBuilder.CreateIndex(
                name: "IX_PatientRecallPlans_UpdatedByUserId",
                table: "PatientRecallPlans",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecallSettings_UpdatedByUserId",
                table: "RecallSettings",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUpTasks_Appointments_AppointmentId",
                table: "FollowUpTasks",
                column: "AppointmentId",
                principalTable: "Appointments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUpTasks_TreatmentPlanItems_SourcePlanItemId",
                table: "FollowUpTasks",
                column: "SourcePlanItemId",
                principalTable: "TreatmentPlanItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ALV-009-C01: the follow-up triggers learn the new columns (a task may be unassigned or system-made; its kind and source never change; its appointment and plan item belong to its patient)
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Guard] ON [FollowUpTasks] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51210, 'A follow-up task is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] <> 'Open')
        THROW 51210, 'A completed or cancelled follow-up task is final and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[Kind] <> d.[Kind]
                  OR ISNULL(i.[CreatedByUserId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[CreatedByUserId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourcePlanItemId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SourcePlanItemId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourceKey], N'') <> ISNULL(d.[SourceKey], N''))
        THROW 51210, 'A follow-up task keeps its patient, creator, creation time, kind, source and recall link.', 1;
END");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Links] ON [FollowUpTasks] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[AssigneeUserId]
               WHERE u.[IsDisabled] = 1 AND i.[Status] = 'Open'
                 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[AssigneeUserId] = i.[AssigneeUserId]))
        THROW 51211, 'An open follow-up task is assigned to an enabled account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [FollowUpTasks] p ON p.[Id] = i.[RecallOfTaskId] WHERE p.[PatientId] <> i.[PatientId])
        THROW 51211, 'A recall belongs to the patient of the task it follows.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Appointments] a ON a.[Id] = i.[AppointmentId] WHERE a.[PatientId] <> i.[PatientId])
        THROW 51211, 'A follow-up task links an appointment of its own patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] x ON x.[Id] = i.[SourcePlanItemId] WHERE x.[PatientId] <> i.[PatientId])
        THROW 51211, 'A follow-up task names a treatment-plan item of its own patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_RecallSettings_Guard] ON [RecallSettings] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51214, 'The recall settings are never deleted.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no task uses the new features (a kind other than Manual, no owner, no creator, a priority, an appointment link, a system closure) and no patient has a recall plan
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [FollowUpTasks] WHERE [Kind] <> 'Manual' OR [AssigneeUserId] IS NULL OR [CreatedByUserId] IS NULL OR [Priority] <> 'Normal' OR [AppointmentId] IS NOT NULL OR [ClosedBySystem] = 1)
   OR EXISTS (SELECT 1 FROM [PatientRecallPlans])
   OR EXISTS (SELECT 1 FROM [FollowUpTaskEvents] WHERE [Kind] IN ('Assigned','PriorityChanged','SystemCreated','SystemClosed'))
   OR EXISTS (SELECT 1 FROM [FollowUpReminderDeliveries] WHERE [Outcome] = 'Unassigned')
    THROW 51215, 'Work-queue data exists, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Guard] ON [FollowUpTasks] AFTER UPDATE, DELETE AS
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
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Links] ON [FollowUpTasks] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[AssigneeUserId]
               WHERE u.[IsDisabled] = 1 AND i.[Status] = 'Open'
                 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[AssigneeUserId] = i.[AssigneeUserId]))
        THROW 51211, 'An open follow-up task is assigned to an enabled account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [FollowUpTasks] p ON p.[Id] = i.[RecallOfTaskId] WHERE p.[PatientId] <> i.[PatientId])
        THROW 51211, 'A recall belongs to the patient of the task it follows.', 1;
END");
            migrationBuilder.DropForeignKey(
                name: "FK_FollowUpTasks_Appointments_AppointmentId",
                table: "FollowUpTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_FollowUpTasks_TreatmentPlanItems_SourcePlanItemId",
                table: "FollowUpTasks");

            migrationBuilder.DropTable(
                name: "PatientRecallPlans");

            migrationBuilder.DropTable(
                name: "RecallSettings");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_AppointmentId",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_Open_Unassigned",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_SourceKey",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_SourcePlanItemId",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_Status_Kind_DueAtUtc",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_Status_Priority_DueAtUtc",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Closure",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Creator",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Kind",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Priority",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Source",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_SourceKey",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTaskEvents_Actor",
                table: "FollowUpTaskEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTaskEvents_Kind",
                table: "FollowUpTaskEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpReminderDeliveries_Outcome",
                table: "FollowUpReminderDeliveries");

            migrationBuilder.DropColumn(
                name: "AppointmentId",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "ClosedBySystem",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "SourceKey",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "SourcePlanItemId",
                table: "FollowUpTasks");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatedByUserId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "AssigneeUserId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Closure",
                table: "FollowUpTasks",
                sql: "([Status] = 'Open' AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] <> 'Open' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTaskEvents_Actor",
                table: "FollowUpTaskEvents",
                sql: "[ActorUserId] IS NOT NULL OR [Kind] = 'Reminded'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTaskEvents_Kind",
                table: "FollowUpTaskEvents",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Created','Edited','Reassigned','DueChanged','Reminded','Completed','Cancelled','RecallCreated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpReminderDeliveries_Outcome",
                table: "FollowUpReminderDeliveries",
                sql: "[Outcome] COLLATE Latin1_General_CS_AS IN ('Delivered','Fallback')");
        }
    }
}
