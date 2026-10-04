using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalRecordAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SignedAtUtc",
                table: "Encounters",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SignedByUserId",
                table: "Encounters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TemplateId",
                table: "Encounters",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TemplateName",
                table: "Encounters",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "EncounterAddenda",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicalRecordEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalRecordEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalRecordEvents_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalRecordEvents_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalRecordItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reaction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Dose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalRecordItems", x => x.Id);
                    table.CheckConstraint("CK_ClinicalRecordItems_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('MedicalHistory','DentalHistory','Allergy','Medication')");
                    table.CheckConstraint("CK_ClinicalRecordItems_NameNotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_ClinicalRecordItems_Severity", "[Severity] IS NULL OR [Severity] COLLATE Latin1_General_CS_AS IN ('Mild','Moderate','Severe')");
                    table.CheckConstraint("CK_ClinicalRecordItems_Status", "([Kind] COLLATE Latin1_General_CS_AS = 'Medication' AND [Status] COLLATE Latin1_General_CS_AS IN ('Active','Inactive','Discontinued')) OR ([Kind] COLLATE Latin1_General_CS_AS <> 'Medication' AND [Status] COLLATE Latin1_General_CS_AS IN ('Active','Inactive','Resolved'))");
                    table.ForeignKey(
                        name: "FK_ClinicalRecordItems_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicalSectionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ReviewedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalSectionReviews", x => x.Id);
                    table.CheckConstraint("CK_ClinicalSectionReviews_Section", "[Section] COLLATE Latin1_General_CS_AS IN ('MedicalHistory','DentalHistory','Allergy','Medication')");
                    table.CheckConstraint("CK_ClinicalSectionReviews_State", "[State] COLLATE Latin1_General_CS_AS IN ('Reviewed','NoneKnown','Unknown')");
                    table.ForeignKey(
                        name: "FK_ClinicalSectionReviews_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    StarterText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterNotes", x => x.Id);
                    table.CheckConstraint("CK_EncounterNotes_Section", "[Section] COLLATE Latin1_General_CS_AS IN ('Subjective','Objective','Assessment','Plan','Progress','Treatment')");
                    table.ForeignKey(
                        name: "FK_EncounterNotes_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterVitals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MeasuredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SystolicMmHg = table.Column<int>(type: "int", nullable: true),
                    DiastolicMmHg = table.Column<int>(type: "int", nullable: true),
                    PulseBpm = table.Column<int>(type: "int", nullable: true),
                    RespirationsPerMinute = table.Column<int>(type: "int", nullable: true),
                    TemperatureC = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    OxygenSaturationPercent = table.Column<int>(type: "int", nullable: true),
                    WeightKg = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClientKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterVitals", x => x.Id);
                    table.CheckConstraint("CK_EncounterVitals_AtLeastOneValue", "[SystolicMmHg] IS NOT NULL OR [DiastolicMmHg] IS NOT NULL OR [PulseBpm] IS NOT NULL OR [RespirationsPerMinute] IS NOT NULL OR [TemperatureC] IS NOT NULL OR [OxygenSaturationPercent] IS NOT NULL OR [WeightKg] IS NOT NULL OR [HeightCm] IS NOT NULL");
                    table.CheckConstraint("CK_EncounterVitals_BloodPressurePair", "([SystolicMmHg] IS NULL AND [DiastolicMmHg] IS NULL) OR ([SystolicMmHg] IS NOT NULL AND [DiastolicMmHg] IS NOT NULL AND [DiastolicMmHg] < [SystolicMmHg])");
                    table.CheckConstraint("CK_EncounterVitals_VoidStamp", "([VoidedAtUtc] IS NULL AND [VoidedByUserId] IS NULL AND [VoidReason] IS NULL) OR ([VoidedAtUtc] IS NOT NULL AND [VoidedByUserId] IS NOT NULL AND [VoidReason] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_EncounterVitals_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EncounterVitals_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoteTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTemplates", x => x.Id);
                    table.CheckConstraint("CK_NoteTemplates_NameNotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
                });

            migrationBuilder.CreateTable(
                name: "ClinicalRecordItemVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reaction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Dose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalRecordItemVersions", x => x.Id);
                    table.CheckConstraint("CK_ClinicalRecordItemVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Added','Changed','StatusChanged','RemovedInError')");
                    table.ForeignKey(
                        name: "FK_ClinicalRecordItemVersions_ClinicalRecordItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "ClinicalRecordItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalRecordItemVersions_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NoteTemplateSections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    StarterText = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTemplateSections", x => x.Id);
                    table.CheckConstraint("CK_NoteTemplateSections_Section", "[Section] COLLATE Latin1_General_CS_AS IN ('Subjective','Objective','Assessment','Plan','Progress','Treatment')");
                    table.ForeignKey(
                        name: "FK_NoteTemplateSections_NoteTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "NoteTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_TemplateId",
                table: "Encounters",
                column: "TemplateId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Encounters_SignedStamp",
                table: "Encounters",
                sql: "([SignedAtUtc] IS NULL AND [SignedByUserId] IS NULL) OR ([SignedAtUtc] IS NOT NULL AND [SignedByUserId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordEvents_EncounterId",
                table: "ClinicalRecordEvents",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordEvents_PatientId_OccurredAtUtc",
                table: "ClinicalRecordEvents",
                columns: new[] { "PatientId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordItems_PatientId_Kind",
                table: "ClinicalRecordItems",
                columns: new[] { "PatientId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordItems_PatientId_Kind_Name",
                table: "ClinicalRecordItems",
                columns: new[] { "PatientId", "Kind", "Name" },
                unique: true,
                filter: "[RemovedAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordItemVersions_EncounterId",
                table: "ClinicalRecordItemVersions",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordItemVersions_ItemId_VersionNumber",
                table: "ClinicalRecordItemVersions",
                columns: new[] { "ItemId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordItemVersions_PatientId_OccurredAtUtc",
                table: "ClinicalRecordItemVersions",
                columns: new[] { "PatientId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalSectionReviews_PatientId_Section",
                table: "ClinicalSectionReviews",
                columns: new[] { "PatientId", "Section" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncounterNotes_EncounterId_Section",
                table: "EncounterNotes",
                columns: new[] { "EncounterId", "Section" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncounterVitals_EncounterId_ClientKey",
                table: "EncounterVitals",
                columns: new[] { "EncounterId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncounterVitals_PatientId_MeasuredAtUtc",
                table: "EncounterVitals",
                columns: new[] { "PatientId", "MeasuredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTemplates_Name",
                table: "NoteTemplates",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoteTemplateSections_TemplateId_Section",
                table: "NoteTemplateSections",
                columns: new[] { "TemplateId", "Section" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Encounters_NoteTemplates_TemplateId",
                table: "Encounters",
                column: "TemplateId",
                principalTable: "NoteTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ALV-005-C01: the database refuses, independently of the application, to delete clinical record items, notes, vitals and templates, to change the item
            // history, the record's event history, or a vital sign other than voiding it, and to change a note or a vital sign of a finalized encounter.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ClinicalRecordItems_NoDelete] ON [ClinicalRecordItems] INSTEAD OF DELETE AS
BEGIN
    THROW 51040, 'Clinical record items are never deleted; an item entered in error is marked removed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ClinicalRecordItemVersions_Immutable] ON [ClinicalRecordItemVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51041, 'The history of a clinical record item is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ClinicalRecordEvents_Immutable] ON [ClinicalRecordEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51042, 'The history of a clinical record is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterNotes_FinalizedImmutable] ON [EncounterNotes] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM [Encounters] e WHERE e.[Status] = 'Finalized' AND e.[Id] IN (SELECT [EncounterId] FROM inserted))
        THROW 51043, 'The notes of a finalized encounter cannot be changed; add an addendum.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterNotes_NoDelete] ON [EncounterNotes] INSTEAD OF DELETE AS
BEGIN
    THROW 51044, 'Encounter notes are never deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterVitals_FinalizedImmutable] ON [EncounterVitals] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM [Encounters] e WHERE e.[Status] = 'Finalized' AND e.[Id] IN (SELECT [EncounterId] FROM inserted))
        THROW 51045, 'The vital signs of a finalized encounter cannot be changed; add an addendum.', 1;
    IF UPDATE([MeasuredAtUtc]) OR UPDATE([SystolicMmHg]) OR UPDATE([DiastolicMmHg]) OR UPDATE([PulseBpm]) OR UPDATE([RespirationsPerMinute]) OR UPDATE([TemperatureC])
       OR UPDATE([OxygenSaturationPercent]) OR UPDATE([WeightKg]) OR UPDATE([HeightCm]) OR UPDATE([Note]) OR UPDATE([EncounterId]) OR UPDATE([PatientId]) OR UPDATE([ClientKey])
        IF EXISTS (SELECT 1 FROM deleted)
            THROW 51048, 'A recorded vital sign cannot be edited; void it and record it again.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterVitals_NoDelete] ON [EncounterVitals] INSTEAD OF DELETE AS
BEGIN
    THROW 51046, 'Vital signs are never deleted; a reading entered in error is voided.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_NoteTemplates_NoDelete] ON [NoteTemplates] INSTEAD OF DELETE AS
BEGIN
    THROW 51047, 'Note templates are never deleted; deactivate them.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Encounters_NoteTemplates_TemplateId",
                table: "Encounters");

            migrationBuilder.DropTable(
                name: "ClinicalRecordEvents");

            migrationBuilder.DropTable(
                name: "ClinicalRecordItemVersions");

            migrationBuilder.DropTable(
                name: "ClinicalSectionReviews");

            migrationBuilder.DropTable(
                name: "EncounterNotes");

            migrationBuilder.DropTable(
                name: "EncounterVitals");

            migrationBuilder.DropTable(
                name: "NoteTemplateSections");

            migrationBuilder.DropTable(
                name: "ClinicalRecordItems");

            migrationBuilder.DropTable(
                name: "NoteTemplates");

            migrationBuilder.DropIndex(
                name: "IX_Encounters_TemplateId",
                table: "Encounters");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Encounters_SignedStamp",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "SignedAtUtc",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "SignedByUserId",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "TemplateId",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "TemplateName",
                table: "Encounters");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "EncounterAddenda");
        }
    }
}
