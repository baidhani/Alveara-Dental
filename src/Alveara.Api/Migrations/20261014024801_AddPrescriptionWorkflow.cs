using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptionWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrescriptionDrafts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Medication = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DoseValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    DoseUnit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    QuantityValue = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: true),
                    QuantityUnit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    Refills = table.Column<int>(type: "int", nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupersedesPrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SafetyFingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SafetySnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    FinalizedPrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DiscardedReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreateKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreateHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionDrafts", x => x.Id);
                    table.CheckConstraint("CK_PrescriptionDrafts_Dose", "[DoseValue] > 0 AND (([DoseUnit] COLLATE Latin1_General_CS_AS = 'mg' AND [DoseValue] <= 5000) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'mcg' AND [DoseValue] <= 5000) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'g' AND [DoseValue] <= 10) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'mL' AND [DoseValue] <= 100) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'tablet' AND [DoseValue] <= 10) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'capsule' AND [DoseValue] <= 10) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'drop' AND [DoseValue] <= 20) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'puff' AND [DoseValue] <= 20) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'unit' AND [DoseValue] <= 1000))");
                    table.CheckConstraint("CK_PrescriptionDrafts_Fingerprint", "[SafetyFingerprint] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([SafetyFingerprint]) = 64");
                    table.CheckConstraint("CK_PrescriptionDrafts_Frequency", "[Frequency] COLLATE Latin1_General_CS_AS IN ('Once','Once daily','Twice daily','Three times daily','Four times daily','Every 4 hours','Every 6 hours','Every 8 hours','Every 12 hours','At bedtime','As needed')");
                    table.CheckConstraint("CK_PrescriptionDrafts_Hash", "[CreateHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([CreateHash]) = 64");
                    table.CheckConstraint("CK_PrescriptionDrafts_Instructions", "[Instructions] IS NULL OR LEN(LTRIM(RTRIM([Instructions]))) > 0");
                    table.CheckConstraint("CK_PrescriptionDrafts_Key", "LEN(LTRIM(RTRIM([CreateKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [CreateKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PrescriptionDrafts_Medication", "LEN(LTRIM(RTRIM([Medication]))) >= 2 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Medication] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PrescriptionDrafts_Outcome", "(([Status] = 'Finalized' AND [FinalizedPrescriptionId] IS NOT NULL) OR ([Status] <> 'Finalized' AND [FinalizedPrescriptionId] IS NULL)) AND ([DiscardedReason] IS NULL OR ([Status] = 'Discarded' AND LEN(LTRIM(RTRIM([DiscardedReason]))) > 0))");
                    table.CheckConstraint("CK_PrescriptionDrafts_Quantity", "([QuantityValue] IS NULL AND [QuantityUnit] IS NULL) OR ([QuantityValue] IS NOT NULL AND [QuantityUnit] IS NOT NULL AND [QuantityValue] > 0 AND [QuantityValue] <= 1000 AND [QuantityUnit] COLLATE Latin1_General_CS_AS IN ('tablet','capsule','mL','g','tube','bottle','inhaler','unit'))");
                    table.CheckConstraint("CK_PrescriptionDrafts_Refills", "[Refills] IS NULL OR ([Refills] >= 0 AND [Refills] <= 12)");
                    table.CheckConstraint("CK_PrescriptionDrafts_Review", "[ReviewNote] IS NULL OR LEN(LTRIM(RTRIM([ReviewNote]))) > 0");
                    table.CheckConstraint("CK_PrescriptionDrafts_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Draft','Finalized','Discarded')");
                    table.CheckConstraint("CK_PrescriptionDrafts_Times", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND [UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.CheckConstraint("CK_PrescriptionDrafts_Unit", "[DoseUnit] COLLATE Latin1_General_CS_AS IN ('mg','mcg','g','mL','tablet','capsule','drop','puff','unit')");
                    table.ForeignKey(
                        name: "FK_PrescriptionDrafts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDrafts_Prescriptions_FinalizedPrescriptionId",
                        column: x => x.FinalizedPrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDrafts_Prescriptions_SupersedesPrescriptionId",
                        column: x => x.SupersedesPrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDrafts_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDrafts_UserAccounts_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrescriptionEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SupersededByPrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionEvents", x => x.Id);
                    table.CheckConstraint("CK_PrescriptionEvents_Reason", "(([EventType] IN ('Cancelled','Discontinued') AND [Reason] IS NOT NULL) OR ([EventType] NOT IN ('Cancelled','Discontinued') AND [Reason] IS NULL)) AND ([Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0)");
                    table.CheckConstraint("CK_PrescriptionEvents_Superseded", "(([EventType] = 'Superseded' AND [SupersededByPrescriptionId] IS NOT NULL) OR ([EventType] <> 'Superseded' AND [SupersededByPrescriptionId] IS NULL)) AND ([SupersededByPrescriptionId] IS NULL OR [SupersededByPrescriptionId] <> [PrescriptionId])");
                    table.CheckConstraint("CK_PrescriptionEvents_Time", "[OccurredAtUtc] >= '2000-01-01' AND [OccurredAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_PrescriptionEvents_Type", "[EventType] COLLATE Latin1_General_CS_AS IN ('Finalized','Cancelled','Discontinued','Superseded')");
                    table.ForeignKey(
                        name: "FK_PrescriptionEvents_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionEvents_Prescriptions_SupersededByPrescriptionId",
                        column: x => x.SupersededByPrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionEvents_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrescriptionDetails",
                columns: table => new
                {
                    PrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DraftId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersedesPrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    QuantityValue = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    QuantityUnit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Refills = table.Column<int>(type: "int", nullable: false),
                    SafetyFingerprint = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SafetySnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionDetails", x => x.PrescriptionId);
                    table.CheckConstraint("CK_PrescriptionDetails_Finalized", "[FinalizedAtUtc] >= '2000-01-01' AND [FinalizedAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_PrescriptionDetails_Fingerprint", "[SafetyFingerprint] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([SafetyFingerprint]) = 64");
                    table.CheckConstraint("CK_PrescriptionDetails_Instructions", "LEN(LTRIM(RTRIM([Instructions]))) > 0");
                    table.CheckConstraint("CK_PrescriptionDetails_Quantity", "[QuantityValue] > 0 AND [QuantityValue] <= 1000 AND [QuantityUnit] COLLATE Latin1_General_CS_AS IN ('tablet','capsule','mL','g','tube','bottle','inhaler','unit')");
                    table.CheckConstraint("CK_PrescriptionDetails_Refills", "[Refills] >= 0 AND [Refills] <= 12");
                    table.CheckConstraint("CK_PrescriptionDetails_Supersedes", "[SupersedesPrescriptionId] IS NULL OR [SupersedesPrescriptionId] <> [PrescriptionId]");
                    table.ForeignKey(
                        name: "FK_PrescriptionDetails_PrescriptionDrafts_DraftId",
                        column: x => x.DraftId,
                        principalTable: "PrescriptionDrafts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDetails_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDetails_Prescriptions_SupersedesPrescriptionId",
                        column: x => x.SupersedesPrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionDetails_UserAccounts_FinalizedByUserId",
                        column: x => x.FinalizedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDetails_DraftId",
                table: "PrescriptionDetails",
                column: "DraftId",
                unique: true,
                filter: "[DraftId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDetails_FinalizedByUserId",
                table: "PrescriptionDetails",
                column: "FinalizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDetails_SupersedesPrescriptionId",
                table: "PrescriptionDetails",
                column: "SupersedesPrescriptionId",
                unique: true,
                filter: "[SupersedesPrescriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDrafts_CreatedByUserId",
                table: "PrescriptionDrafts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDrafts_FinalizedPrescriptionId",
                table: "PrescriptionDrafts",
                column: "FinalizedPrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDrafts_PatientId_CreateKey",
                table: "PrescriptionDrafts",
                columns: new[] { "PatientId", "CreateKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDrafts_PatientId_Status_UpdatedAtUtc",
                table: "PrescriptionDrafts",
                columns: new[] { "PatientId", "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDrafts_SupersedesPrescriptionId",
                table: "PrescriptionDrafts",
                column: "SupersedesPrescriptionId",
                unique: true,
                filter: "[SupersedesPrescriptionId] IS NOT NULL AND [Status] = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionDrafts_UpdatedByUserId",
                table: "PrescriptionDrafts",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionEvents_ActorUserId",
                table: "PrescriptionEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionEvents_OneEnding",
                table: "PrescriptionEvents",
                column: "PrescriptionId",
                unique: true,
                filter: "[EventType] IN ('Cancelled','Discontinued','Superseded')");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionEvents_OneFinalized",
                table: "PrescriptionEvents",
                column: "PrescriptionId",
                unique: true,
                filter: "[EventType] = 'Finalized'");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionEvents_PrescriptionId_OccurredAtUtc",
                table: "PrescriptionEvents",
                columns: new[] { "PrescriptionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionEvents_SupersededByPrescriptionId",
                table: "PrescriptionEvents",
                column: "SupersededByPrescriptionId");

            // ---- ALV-016-C01 triggers (errors 51175 to 51181; 51182 and up are free). A draft is the only mutable record; every other row of the workflow is append-only. ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionDrafts_NoDelete] ON [PrescriptionDrafts] INSTEAD OF DELETE AS
BEGIN
    THROW 51175, 'A prescription draft is never deleted; an unwanted draft is discarded.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionDrafts_Guard] ON [PrescriptionDrafts] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] <> 'Draft')
        THROW 51176, 'A finalized or discarded prescription draft never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreateKey] <> d.[CreateKey] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[CreatedByUserId] <> d.[CreatedByUserId]
                  OR ISNULL(i.[SupersedesPrescriptionId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SupersedesPrescriptionId], '00000000-0000-0000-0000-000000000000'))
        THROW 51177, 'The patient, the creation and the amended prescription of a draft never change.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionDetails_Immutable] ON [PrescriptionDetails] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51178, 'The details of a finalized prescription are never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionDetails_Links] ON [PrescriptionDetails] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Prescriptions] p ON p.[Id] = i.[PrescriptionId] JOIN [Prescriptions] o ON o.[Id] = i.[SupersedesPrescriptionId] WHERE o.[PatientId] <> p.[PatientId])
        THROW 51179, 'An amendment must be for the same patient as the prescription it replaces.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Prescriptions] p ON p.[Id] = i.[PrescriptionId] JOIN [PrescriptionDrafts] d ON d.[Id] = i.[DraftId] WHERE d.[PatientId] <> p.[PatientId])
        THROW 51179, 'A prescription must be for the same patient as the draft it came from.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionEvents_Immutable] ON [PrescriptionEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51180, 'The events of a prescription are append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionEvents_Links] ON [PrescriptionEvents] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Prescriptions] p ON p.[Id] = i.[PrescriptionId] JOIN [Prescriptions] s ON s.[Id] = i.[SupersededByPrescriptionId] WHERE s.[PatientId] <> p.[PatientId])
        THROW 51181, 'A prescription can only be replaced by a prescription of the same patient.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while the workflow holds nothing: afterwards these tables are clinical history that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [PrescriptionDrafts]) OR EXISTS (SELECT 1 FROM [PrescriptionDetails]) OR EXISTS (SELECT 1 FROM [PrescriptionEvents])
    THROW 51178, 'Prescription workflow records exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "PrescriptionDetails");

            migrationBuilder.DropTable(
                name: "PrescriptionEvents");

            migrationBuilder.DropTable(
                name: "PrescriptionDrafts");
        }
    }
}
