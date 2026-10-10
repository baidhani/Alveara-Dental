using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentPlanCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents");

            migrationBuilder.AddColumn<int>(
                name: "CurrentRevision",
                table: "TreatmentPlans",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceCode",
                table: "TreatmentPlans",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ChangeType",
                table: "TreatmentPlanEvents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(13)",
                oldMaxLength: 13);

            migrationBuilder.AddColumn<int>(
                name: "RevisionNumber",
                table: "TreatmentPlanEvents",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiagnosisPlanResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagnosisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferenceText = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosisPlanResolutions", x => x.Id);
                    table.CheckConstraint("CK_DiagnosisPlanResolutions_Method", "[Method] COLLATE Latin1_General_CS_AS IN ('ReferenceCode','PlanId','Chosen')");
                    table.CheckConstraint("CK_DiagnosisPlanResolutions_Text", "LEN(LTRIM(RTRIM([ReferenceText]))) > 0");
                    table.ForeignKey(
                        name: "FK_DiagnosisPlanResolutions_Diagnoses_DiagnosisId",
                        column: x => x.DiagnosisId,
                        principalTable: "Diagnoses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiagnosisPlanResolutions_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanItemStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Acceptance = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Scheduling = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: false),
                    Completion = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PlannedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DecidedByName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanItemStates", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Acceptance", "[Acceptance] COLLATE Latin1_General_CS_AS IN ('Pending','Accepted','Declined')");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Accepted", "([Scheduling] = 'Unscheduled' AND [Completion] = 'NotStarted') OR [Acceptance] = 'Accepted'");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Completion", "[Completion] COLLATE Latin1_General_CS_AS IN ('NotStarted','Completed')");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Declined", "[Acceptance] <> 'Declined' OR [Reason] IS NOT NULL");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Number", "[Sequence] >= 1 AND [RevisionNumber] >= 1");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_PlannedDate", "[PlannedDate] IS NULL OR [Scheduling] = 'Scheduled'");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Scheduling", "[Scheduling] COLLATE Latin1_General_CS_AS IN ('Unscheduled','Scheduled')");
                    table.CheckConstraint("CK_TreatmentPlanItemStates_Text", "([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0) AND ([DecidedByName] IS NULL OR LEN(LTRIM(RTRIM([DecidedByName]))) > 0)");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItemStates_TreatmentPlanItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EventNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ClinicalRationale = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PatientExplanation = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanRevisions", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlanRevisions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','ItemAdded','ItemWithdrawn','Renamed','Revised')");
                    table.CheckConstraint("CK_TreatmentPlanRevisions_Number", "[RevisionNumber] >= 1 AND [EventNumber] >= 1");
                    table.CheckConstraint("CK_TreatmentPlanRevisions_Text", "([ClinicalRationale] IS NULL OR LEN(LTRIM(RTRIM([ClinicalRationale]))) > 0) AND ([PatientExplanation] IS NULL OR LEN(LTRIM(RTRIM([PatientExplanation]))) > 0) AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)");
                    table.CheckConstraint("CK_TreatmentPlanRevisions_Title", "LEN(LTRIM(RTRIM([Title]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Title] COLLATE Latin1_General_BIN2) = 0");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanRevisions_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanRevisionPhases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhaseNumber = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    PlannedVisit = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    TargetDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanRevisionPhases", x => x.Id);
                    table.UniqueConstraint("AK_TreatmentPlanRevisionPhases_RevisionId_PhaseNumber", x => new { x.RevisionId, x.PhaseNumber });
                    table.CheckConstraint("CK_TreatmentPlanRevisionPhases_Name", "LEN(LTRIM(RTRIM([Name]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Name] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_TreatmentPlanRevisionPhases_Number", "[PhaseNumber] >= 1 AND [PhaseNumber] <= 12");
                    table.CheckConstraint("CK_TreatmentPlanRevisionPhases_Visit", "[PlannedVisit] IS NULL OR (LEN(LTRIM(RTRIM([PlannedVisit]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [PlannedVisit] COLLATE Latin1_General_BIN2) = 0)");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanRevisionPhases_TreatmentPlanRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "TreatmentPlanRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanRevisionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PhaseNumber = table.Column<int>(type: "int", nullable: true),
                    IsWithdrawn = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanRevisionItems", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlanRevisionItems_Phase", "[PhaseNumber] IS NULL OR [PhaseNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanRevisionItems_TreatmentPlanItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanRevisionItems_TreatmentPlanRevisionPhases_RevisionId_PhaseNumber",
                        columns: x => new { x.RevisionId, x.PhaseNumber },
                        principalTable: "TreatmentPlanRevisionPhases",
                        principalColumns: new[] { "RevisionId", "PhaseNumber" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanRevisionItems_TreatmentPlanRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "TreatmentPlanRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_PatientId_ReferenceCode",
                table: "TreatmentPlans",
                columns: new[] { "PatientId", "ReferenceCode" },
                unique: true,
                filter: "[ReferenceCode] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlans_CurrentRevision",
                table: "TreatmentPlans",
                sql: "[CurrentRevision] >= 1");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlans_ReferenceCode",
                table: "TreatmentPlans",
                sql: "[ReferenceCode] IS NULL OR (LEN(LTRIM(RTRIM([ReferenceCode]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [ReferenceCode] COLLATE Latin1_General_BIN2) = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','ItemAdded','ItemWithdrawn','Renamed','Withdrawn','Revised','ReferenceCodeSet','ItemAccepted','ItemDeclined','PlanAccepted','PlanDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents",
                sql: "([ChangeType] IN ('ItemAdded','ItemWithdrawn','ItemAccepted','ItemDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted') AND [ItemId] IS NOT NULL) OR ([ChangeType] NOT IN ('ItemAdded','ItemWithdrawn','ItemAccepted','ItemDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted') AND [ItemId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] NOT IN ('ItemWithdrawn','Withdrawn','ItemDeclined','PlanDeclined','Revised') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_RevisionNumber",
                table: "TreatmentPlanEvents",
                sql: "[RevisionNumber] IS NULL OR [RevisionNumber] >= 1");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosisPlanResolutions_DiagnosisId",
                table: "DiagnosisPlanResolutions",
                column: "DiagnosisId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosisPlanResolutions_PlanId",
                table: "DiagnosisPlanResolutions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemStates_ItemId_Sequence",
                table: "TreatmentPlanItemStates",
                columns: new[] { "ItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemStates_PlanId",
                table: "TreatmentPlanItemStates",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanRevisionItems_ItemId",
                table: "TreatmentPlanRevisionItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanRevisionItems_RevisionId_ItemId",
                table: "TreatmentPlanRevisionItems",
                columns: new[] { "RevisionId", "ItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanRevisionItems_RevisionId_PhaseNumber",
                table: "TreatmentPlanRevisionItems",
                columns: new[] { "RevisionId", "PhaseNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanRevisionPhases_RevisionId_PhaseNumber",
                table: "TreatmentPlanRevisionPhases",
                columns: new[] { "RevisionId", "PhaseNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanRevisions_PlanId_EventNumber",
                table: "TreatmentPlanRevisions",
                columns: new[] { "PlanId", "EventNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanRevisions_PlanId_RevisionNumber",
                table: "TreatmentPlanRevisions",
                columns: new[] { "PlanId", "RevisionNumber" },
                unique: true);
            // ---- ALV-015-C01: give every plan that already exists its first revision (its current content), so revisions are never missing ----
            migrationBuilder.Sql(@"INSERT INTO [TreatmentPlanRevisions] ([Id], [PlanId], [PatientId], [RevisionNumber], [ChangeType], [EventNumber], [Title], [Reason], [CreatedAtUtc], [CreatedByUserId])
SELECT NEWID(), p.[Id], p.[PatientId], 1, 'Revised', ISNULL((SELECT MAX(e.[EventNumber]) FROM [TreatmentPlanEvents] e WHERE e.[PlanId] = p.[Id]), 1), p.[Title],
       'Recorded when plan revisions were introduced; the plan''s content as it stood then.', SYSDATETIMEOFFSET(), p.[CreatedByUserId]
FROM [TreatmentPlans] p");
            migrationBuilder.Sql(@"INSERT INTO [TreatmentPlanRevisionItems] ([Id], [RevisionId], [PlanId], [PatientId], [ItemId], [PhaseNumber], [IsWithdrawn])
SELECT NEWID(), r.[Id], i.[PlanId], i.[PatientId], i.[Id], NULL, CASE WHEN i.[WithdrawnAtUtc] IS NULL THEN 0 ELSE 1 END
FROM [TreatmentPlanItems] i JOIN [TreatmentPlanRevisions] r ON r.[PlanId] = i.[PlanId] AND r.[RevisionNumber] = 1");

            // ---- ALV-015-C01 triggers: what no single-row check can see (error numbers 51099-51117) ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlans_Revision] ON [TreatmentPlans] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE d.ReferenceCode IS NOT NULL AND (i.ReferenceCode IS NULL OR i.ReferenceCode COLLATE Latin1_General_BIN2 <> d.ReferenceCode COLLATE Latin1_General_BIN2))
        THROW 51099, 'A treatment plan''s reference code is set once and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id WHERE i.CurrentRevision < d.CurrentRevision)
        THROW 51100, 'A treatment plan''s revision number only goes up.', 1;
END");
            foreach (var table in new[] { "TreatmentPlanRevisions", "TreatmentPlanRevisionPhases", "TreatmentPlanRevisionItems", "TreatmentPlanItemStates", "DiagnosisPlanResolutions" })
            {
                var (number, message) = table switch
                {
                    "TreatmentPlanRevisions" => (51101, "A saved revision of a treatment plan is never edited or deleted; a change saves the next revision."),
                    "TreatmentPlanRevisionPhases" => (51102, "The phases of a saved revision are never edited or deleted."),
                    "TreatmentPlanRevisionItems" => (51103, "The procedures of a saved revision are never edited or deleted."),
                    "TreatmentPlanItemStates" => (51105, "The history of a procedure''s acceptance, scheduling and completion is append-only."),
                    _ => (51113, "A diagnosis''s plan resolution is never edited or deleted."),
                };
                migrationBuilder.Sql($@"CREATE TRIGGER [TR_{table}_Immutable] ON [{table}] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW {number}, '{message}', 1;
END");
            }
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanRevisions_Links] ON [TreatmentPlanRevisions] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE p.PatientId <> i.PatientId)
        THROW 51110, 'A revision must carry its plan''s patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanRevisionPhases_Links] ON [TreatmentPlanRevisionPhases] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanRevisions] r ON r.Id = i.RevisionId WHERE r.PlanId <> i.PlanId OR r.PatientId <> i.PatientId)
        THROW 51111, 'A phase must carry the plan and patient of its revision.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanRevisionItems_Links] ON [TreatmentPlanRevisionItems] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanRevisions] r ON r.Id = i.RevisionId WHERE r.PlanId <> i.PlanId OR r.PatientId <> i.PatientId)
        THROW 51104, 'A revision''s procedure must carry the plan and patient of that revision.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] t ON t.Id = i.ItemId WHERE t.PlanId <> i.PlanId OR t.PatientId <> i.PatientId)
        THROW 51104, 'A revision''s procedure must belong to the same plan and patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanItemStates_Links] ON [TreatmentPlanItemStates] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] t ON t.Id = i.ItemId WHERE t.PlanId <> i.PlanId OR t.PatientId <> i.PatientId)
        THROW 51106, 'A procedure''s state must carry the plan and patient of that procedure.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] t ON t.Id = i.ItemId WHERE t.WithdrawnAtUtc IS NOT NULL)
        THROW 51107, 'Nothing is recorded for a withdrawn procedure.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE p.Status = 'Withdrawn')
        THROW 51108, 'A withdrawn treatment plan cannot be changed.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItemStates] s ON s.ItemId = i.ItemId AND s.Sequence < i.Sequence WHERE s.Completion = 'Completed')
        THROW 51109, 'A completed procedure is final.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE i.RevisionNumber > p.CurrentRevision)
        THROW 51112, 'A procedure''s state cannot be recorded against a revision that does not exist.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Diagnoses_ReferenceResolved] ON [Diagnoses] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id JOIN [DiagnosisPlanResolutions] r ON r.DiagnosisId = i.Id
               WHERE ISNULL(i.TreatmentPlanReference, '') COLLATE Latin1_General_BIN2 <> ISNULL(d.TreatmentPlanReference, '') COLLATE Latin1_General_BIN2)
        THROW 51116, 'A diagnosis whose treatment-plan reference has been resolved to a plan keeps that reference.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanItems_CompletedFinal] ON [TreatmentPlanItems] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id JOIN [TreatmentPlanItemStates] s ON s.ItemId = i.Id
               WHERE d.WithdrawnAtUtc IS NULL AND i.WithdrawnAtUtc IS NOT NULL AND s.Completion = 'Completed')
        THROW 51117, 'A completed procedure cannot be withdrawn.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DiagnosisPlanResolutions_Links] ON [DiagnosisPlanResolutions] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Diagnoses] g ON g.Id = i.DiagnosisId JOIN [TreatmentPlans] p ON p.Id = i.PlanId
               WHERE g.PatientId <> i.PatientId OR p.PatientId <> i.PatientId)
        THROW 51114, 'A diagnosis can only be resolved to a treatment plan of the same patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Diagnoses] g ON g.Id = i.DiagnosisId
               WHERE g.TreatmentPlanReference IS NULL OR i.ReferenceText COLLATE Latin1_General_BIN2 <> g.TreatmentPlanReference COLLATE Latin1_General_BIN2)
        THROW 51115, 'A resolution must carry the reference the diagnosis holds.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Down cannot restore the old, narrower history rules once an entry of a newer kind (Revised, ItemAccepted...) exists, because the history is append-only; run it on a scratch database.
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_TreatmentPlans_Revision]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_Diagnoses_ReferenceResolved]");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_TreatmentPlanItems_CompletedFinal]");
            migrationBuilder.DropTable(
                name: "DiagnosisPlanResolutions");

            migrationBuilder.DropTable(
                name: "TreatmentPlanItemStates");

            migrationBuilder.DropTable(
                name: "TreatmentPlanRevisionItems");

            migrationBuilder.DropTable(
                name: "TreatmentPlanRevisionPhases");

            migrationBuilder.DropTable(
                name: "TreatmentPlanRevisions");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentPlans_PatientId_ReferenceCode",
                table: "TreatmentPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlans_CurrentRevision",
                table: "TreatmentPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlans_ReferenceCode",
                table: "TreatmentPlans");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_RevisionNumber",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropColumn(
                name: "CurrentRevision",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "ReferenceCode",
                table: "TreatmentPlans");

            migrationBuilder.DropColumn(
                name: "RevisionNumber",
                table: "TreatmentPlanEvents");

            migrationBuilder.AlterColumn<string>(
                name: "ChangeType",
                table: "TreatmentPlanEvents",
                type: "nvarchar(13)",
                maxLength: 13,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','ItemAdded','ItemWithdrawn','Renamed','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents",
                sql: "([ChangeType] IN ('ItemAdded','ItemWithdrawn') AND [ItemId] IS NOT NULL) OR ([ChangeType] IN ('Created','Renamed','Withdrawn') AND [ItemId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] NOT IN ('ItemWithdrawn','Withdrawn') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
        }
    }
}
