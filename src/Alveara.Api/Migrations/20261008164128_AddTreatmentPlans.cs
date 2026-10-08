using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlans", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlans_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                    table.CheckConstraint("CK_TreatmentPlans_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Proposed','Withdrawn')");
                    table.CheckConstraint("CK_TreatmentPlans_Title", "LEN(LTRIM(RTRIM([Title]))) > 0");
                    table.CheckConstraint("CK_TreatmentPlans_TitleLine", "PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Title] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_TreatmentPlans_WithdrawnStamp", "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) OR ([Status] = 'Proposed' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TreatmentPlans_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanEvents", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlanEvents_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','ItemAdded','ItemWithdrawn','Renamed','Withdrawn')");
                    table.CheckConstraint("CK_TreatmentPlanEvents_Item", "([ChangeType] IN ('ItemAdded','ItemWithdrawn') AND [ItemId] IS NOT NULL) OR ([ChangeType] IN ('Created','Renamed','Withdrawn') AND [ItemId] IS NULL)");
                    table.CheckConstraint("CK_TreatmentPlanEvents_Number", "[EventNumber] >= 1");
                    table.CheckConstraint("CK_TreatmentPlanEvents_Reason", "[ChangeType] NOT IN ('ItemWithdrawn','Withdrawn') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
                    table.CheckConstraint("CK_TreatmentPlanEvents_Title", "([ChangeType] IN ('Created','Renamed') AND [Title] IS NOT NULL AND LEN(LTRIM(RTRIM([Title]))) > 0) OR ([ChangeType] NOT IN ('Created','Renamed') AND [Title] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanEvents_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemNumber = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    DiagnosisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    Surface = table.Column<string>(type: "nchar(1)", fixedLength: true, maxLength: 1, nullable: true),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanItems", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlanItems_Fee", "[Fee] >= 0 AND [Fee] <= 1000000.00 AND [Fee] = ROUND([Fee], 2)");
                    table.CheckConstraint("CK_TreatmentPlanItems_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                    table.CheckConstraint("CK_TreatmentPlanItems_Number", "[ItemNumber] >= 1");
                    table.CheckConstraint("CK_TreatmentPlanItems_Surface", "[Surface] IS NULL OR ([ToothKey] IS NOT NULL AND [Surface] COLLATE Latin1_General_CS_AS IN ('M','O','I','D','B','F','L'))");
                    table.CheckConstraint("CK_TreatmentPlanItems_ToothKey", "[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48','51','52','53','54','55','61','62','63','64','65','71','72','73','74','75','81','82','83','84','85')");
                    table.CheckConstraint("CK_TreatmentPlanItems_WithdrawnStamp", "([WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL) OR ([WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0)");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItems_Diagnoses_DiagnosisId",
                        column: x => x.DiagnosisId,
                        principalTable: "Diagnoses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItems_ProcedureDefinitions_ProcedureId",
                        column: x => x.ProcedureId,
                        principalTable: "ProcedureDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItems_ProcedureVersions_ProcedureVersionId",
                        column: x => x.ProcedureVersionId,
                        principalTable: "ProcedureVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItems_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanEvents_PlanId_EventNumber",
                table: "TreatmentPlanEvents",
                columns: new[] { "PlanId", "EventNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_DiagnosisId",
                table: "TreatmentPlanItems",
                column: "DiagnosisId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_PlanId_IdempotencyKey",
                table: "TreatmentPlanItems",
                columns: new[] { "PlanId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_PlanId_ItemNumber",
                table: "TreatmentPlanItems",
                columns: new[] { "PlanId", "ItemNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_ProcedureId",
                table: "TreatmentPlanItems",
                column: "ProcedureId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItems_ProcedureVersionId",
                table: "TreatmentPlanItems",
                column: "ProcedureVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_PatientId_IdempotencyKey",
                table: "TreatmentPlans",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlans_PatientId_Status",
                table: "TreatmentPlans",
                columns: new[] { "PatientId", "Status" });

            // ---- STORY-015 triggers: what no single-row check can see (error numbers 51085-51098) ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlans_NoDelete] ON [TreatmentPlans] INSTEAD OF DELETE AS
BEGIN
    THROW 51085, 'Treatment plans are never deleted; a plan that should not stand is withdrawn with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlans_Identity] ON [TreatmentPlans] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE i.PatientId <> d.PatientId OR i.IdempotencyKey <> d.IdempotencyKey OR i.CreatedAtUtc <> d.CreatedAtUtc OR i.CreatedByUserId <> d.CreatedByUserId)
        THROW 51086, 'A treatment plan''s patient and origin never change.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id WHERE d.Status = 'Withdrawn' AND (i.Status <> d.Status OR i.Title <> d.Title))
        THROW 51087, 'A withdrawn treatment plan is final.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanItems_NoDelete] ON [TreatmentPlanItems] INSTEAD OF DELETE AS
BEGIN
    THROW 51088, 'Treatment plan items are never deleted; a wrong item is withdrawn with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanItems_Immutable] ON [TreatmentPlanItems] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE i.PlanId <> d.PlanId OR i.PatientId <> d.PatientId OR i.ItemNumber <> d.ItemNumber OR i.IdempotencyKey <> d.IdempotencyKey
                  OR i.DiagnosisId <> d.DiagnosisId OR i.ProcedureId <> d.ProcedureId OR i.ProcedureVersionId <> d.ProcedureVersionId
                  OR ISNULL(i.ToothKey, '') <> ISNULL(d.ToothKey, '') OR ISNULL(i.Surface, '') <> ISNULL(d.Surface, '')
                  OR i.Fee <> d.Fee OR i.CreatedAtUtc <> d.CreatedAtUtc OR i.CreatedByUserId <> d.CreatedByUserId)
        THROW 51089, 'A treatment plan item is never edited; withdraw it with a reason and add the correct one.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE d.WithdrawnAtUtc IS NOT NULL AND (i.WithdrawnAtUtc IS NULL OR i.WithdrawnAtUtc <> d.WithdrawnAtUtc OR i.WithdrawnByUserId <> d.WithdrawnByUserId OR ISNULL(i.WithdrawnReason, '') <> ISNULL(d.WithdrawnReason, '')))
        THROW 51090, 'A withdrawn treatment plan item stays withdrawn.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanEvents_Immutable] ON [TreatmentPlanEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51091, 'The history of a treatment plan is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanItems_Links] ON [TreatmentPlanItems] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE p.PatientId <> i.PatientId)
        THROW 51092, 'A plan item must carry the same patient as its plan.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE p.Status = 'Withdrawn')
        THROW 51093, 'A procedure cannot be added to a withdrawn treatment plan.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Diagnoses] g ON g.Id = i.DiagnosisId WHERE g.PatientId <> i.PatientId)
        THROW 51094, 'A plan item''s diagnosis must be the same patient''s.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Diagnoses] g ON g.Id = i.DiagnosisId WHERE g.Status = 'Withdrawn')
        THROW 51095, 'A plan item cannot be proposed for a withdrawn diagnosis.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ProcedureVersions] v ON v.Id = i.ProcedureVersionId WHERE v.ProcedureId <> i.ProcedureId OR v.Fee <> i.Fee)
        THROW 51096, 'A plan item''s fee and procedure must be those of the catalog version it names.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ProcedureDefinitions] d ON d.Id = i.ProcedureId WHERE d.IsActive = 0)
        THROW 51097, 'An inactive procedure cannot be added to a treatment plan.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ProcedureVersions] v ON v.Id = i.ProcedureVersionId
               WHERE (v.Scope IN ('WholeMouth','Arch','Quadrant') AND (i.ToothKey IS NOT NULL OR i.Surface IS NOT NULL))
                  OR (v.Scope = 'Tooth' AND (i.ToothKey IS NULL OR i.Surface IS NOT NULL))
                  OR (v.Scope = 'ToothSurface' AND (i.ToothKey IS NULL OR i.Surface IS NULL))
                  OR (v.Dentition = 'Permanent' AND i.ToothKey IS NOT NULL AND LEFT(i.ToothKey, 1) NOT IN ('1','2','3','4'))
                  OR (v.Dentition = 'Primary' AND i.ToothKey IS NOT NULL AND LEFT(i.ToothKey, 1) NOT IN ('5','6','7','8')))
        THROW 51098, 'The tooth and surface do not fit what the procedure applies to.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TreatmentPlanEvents");

            migrationBuilder.DropTable(
                name: "TreatmentPlanItems");

            migrationBuilder.DropTable(
                name: "TreatmentPlans");
        }
    }
}
