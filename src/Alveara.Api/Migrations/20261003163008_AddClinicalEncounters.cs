using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalEncounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EncounterAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Encounters", x => x.Id);
                    table.CheckConstraint("CK_Encounters_FinalizedStamp", "([Status] = 'Finalized' AND [FinalizedAtUtc] IS NOT NULL AND [FinalizedByUserId] IS NOT NULL) OR ([Status] = 'Draft' AND [FinalizedAtUtc] IS NULL AND [FinalizedByUserId] IS NULL)");
                    table.CheckConstraint("CK_Encounters_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Draft','Finalized')");
                    table.ForeignKey(
                        name: "FK_Encounters_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Encounters_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterAddenda",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ClientKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterAddenda", x => x.Id);
                    table.CheckConstraint("CK_EncounterAddenda_TextNotBlank", "LEN(LTRIM(RTRIM([Text]))) > 0");
                    table.ForeignKey(
                        name: "FK_EncounterAddenda_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Reaction = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Dose = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Frequency = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RemovedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterEntries", x => x.Id);
                    table.CheckConstraint("CK_EncounterEntries_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('MedicalHistory','DentalHistory','Allergy','Medication')");
                    table.CheckConstraint("CK_EncounterEntries_NameNotBlank", "LEN(LTRIM(RTRIM([Name]))) > 0");
                    table.CheckConstraint("CK_EncounterEntries_Severity", "[Severity] IS NULL OR [Severity] COLLATE Latin1_General_CS_AS IN ('Mild','Moderate','Severe')");
                    table.ForeignKey(
                        name: "FK_EncounterEntries_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EncounterEvents_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EncounterEvents_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "EncounterSectionMarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Section = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    MarkedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    MarkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterSectionMarks", x => x.Id);
                    table.CheckConstraint("CK_EncounterSectionMarks_Section", "[Section] COLLATE Latin1_General_CS_AS IN ('MedicalHistory','DentalHistory','Allergy','Medication')");
                    table.CheckConstraint("CK_EncounterSectionMarks_State", "[State] COLLATE Latin1_General_CS_AS IN ('NoneReported')");
                    table.ForeignKey(
                        name: "FK_EncounterSectionMarks_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EncounterAddenda_EncounterId_ClientKey",
                table: "EncounterAddenda",
                columns: new[] { "EncounterId", "ClientKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EncounterAddenda_EncounterId_CreatedAtUtc",
                table: "EncounterAddenda",
                columns: new[] { "EncounterId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EncounterEntries_EncounterId_Kind",
                table: "EncounterEntries",
                columns: new[] { "EncounterId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_EncounterEvents_EncounterId_OccurredAtUtc",
                table: "EncounterEvents",
                columns: new[] { "EncounterId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EncounterEvents_PatientId",
                table: "EncounterEvents",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_AppointmentId",
                table: "Encounters",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_PatientId_EncounterAtUtc",
                table: "Encounters",
                columns: new[] { "PatientId", "EncounterAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_StartKey",
                table: "Encounters",
                column: "StartKey",
                unique: true,
                filter: "[StartKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EncounterSectionMarks_EncounterId_Section",
                table: "EncounterSectionMarks",
                columns: new[] { "EncounterId", "Section" },
                unique: true);

            // STORY-005: the database refuses, independently of the application, to change a finalized encounter, to change or add to its entries and section
            // marks, to add an addendum to anything but a finalized encounter, to change an addendum or the history, and to delete any clinical record.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Encounters_FinalizedImmutable] ON [Encounters] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted WHERE [Status] = 'Finalized')
        THROW 51030, 'A finalized encounter cannot be changed; add an addendum.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Encounters_NoDelete] ON [Encounters] INSTEAD OF DELETE AS
BEGIN
    THROW 51031, 'Clinical encounters are never deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterEntries_FinalizedImmutable] ON [EncounterEntries] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM [Encounters] e WHERE e.[Status] = 'Finalized'
               AND (e.[Id] IN (SELECT [EncounterId] FROM inserted) OR e.[Id] IN (SELECT [EncounterId] FROM deleted)))
        THROW 51032, 'The entries of a finalized encounter cannot be changed; add an addendum.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterEntries_NoDelete] ON [EncounterEntries] INSTEAD OF DELETE AS
BEGIN
    THROW 51033, 'Clinical entries are never deleted; a removed entry is marked removed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterSectionMarks_FinalizedImmutable] ON [EncounterSectionMarks] AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM [Encounters] e WHERE e.[Status] = 'Finalized'
               AND (e.[Id] IN (SELECT [EncounterId] FROM inserted) OR e.[Id] IN (SELECT [EncounterId] FROM deleted)))
        THROW 51034, 'The section reviews of a finalized encounter cannot be changed; add an addendum.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterAddenda_OnlyAfterFinalize] ON [EncounterAddenda] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e ON e.[Id] = i.[EncounterId] WHERE e.[Status] <> 'Finalized')
        THROW 51035, 'An addendum can only be added to a finalized encounter.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterAddenda_Immutable] ON [EncounterAddenda] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51036, 'Encounter addenda are append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_EncounterEvents_Immutable] ON [EncounterEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51037, 'Encounter history is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EncounterAddenda");

            migrationBuilder.DropTable(
                name: "EncounterEntries");

            migrationBuilder.DropTable(
                name: "EncounterEvents");

            migrationBuilder.DropTable(
                name: "EncounterSectionMarks");

            migrationBuilder.DropTable(
                name: "Encounters");
        }
    }
}
