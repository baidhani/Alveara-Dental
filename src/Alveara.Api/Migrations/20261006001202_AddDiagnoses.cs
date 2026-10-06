using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnoses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Diagnoses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TreatmentPlanReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    WithdrawnByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    WithdrawnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Diagnoses", x => x.Id);
                    table.CheckConstraint("CK_Diagnoses_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                    table.CheckConstraint("CK_Diagnoses_Label", "LEN(LTRIM(RTRIM([Label]))) > 0");
                    table.CheckConstraint("CK_Diagnoses_LabelLine", "PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Label] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Diagnoses_PlanReference", "[TreatmentPlanReference] IS NULL OR LEN(LTRIM(RTRIM([TreatmentPlanReference]))) > 0");
                    table.CheckConstraint("CK_Diagnoses_PlanReferenceLine", "[TreatmentPlanReference] IS NULL OR PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [TreatmentPlanReference] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Diagnoses_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");
                    table.CheckConstraint("CK_Diagnoses_ToothKey", "[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48','51','52','53','54','55','61','62','63','64','65','71','72','73','74','75','81','82','83','84','85')");
                    table.CheckConstraint("CK_Diagnoses_WithdrawnStamp", "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) OR ([Status] = 'Active' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_Diagnoses_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Diagnoses_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DiagnosisVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagnosisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    TreatmentPlanReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosisVersions", x => x.Id);
                    table.CheckConstraint("CK_DiagnosisVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','Corrected','Withdrawn')");
                    table.CheckConstraint("CK_DiagnosisVersions_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");
                    table.ForeignKey(
                        name: "FK_DiagnosisVersions_Diagnoses_DiagnosisId",
                        column: x => x.DiagnosisId,
                        principalTable: "Diagnoses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_EncounterId",
                table: "Diagnoses",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_PatientId_IdempotencyKey",
                table: "Diagnoses",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Diagnoses_PatientId_Status",
                table: "Diagnoses",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosisVersions_DiagnosisId_VersionNumber",
                table: "DiagnosisVersions",
                columns: new[] { "DiagnosisId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosisVersions_PatientId",
                table: "DiagnosisVersions",
                column: "PatientId");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Diagnoses_NoDelete] ON [Diagnoses] INSTEAD OF DELETE AS
BEGIN
    THROW 51071, 'A diagnosis is never deleted; a wrong entry is corrected or withdrawn with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DiagnosisVersions_Immutable] ON [DiagnosisVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51072, 'The history of a diagnosis is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Diagnoses_Links] ON [Diagnoses] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[EncounterId] <> d.[EncounterId] OR i.[IdempotencyKey] <> d.[IdempotencyKey] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc])
        THROW 51073, 'The patient, the encounter and the origin of a diagnosis never change.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e ON e.[Id] = i.[EncounterId] WHERE e.[PatientId] <> i.[PatientId])
        THROW 51074, 'A diagnosis can only be linked to an encounter of the same patient.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosisVersions");

            migrationBuilder.DropTable(
                name: "Diagnoses");
        }
    }
}
