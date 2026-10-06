using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosisStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DiagnosisVersions_ChangeType",
                table: "DiagnosisVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DiagnosisVersions_Status",
                table: "DiagnosisVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_Status",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_WithdrawnStamp",
                table: "Diagnoses");

            migrationBuilder.AlterColumn<string>(
                name: "ChangeType",
                table: "DiagnosisVersions",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(10)",
                oldMaxLength: 10);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "DiagnosisVersions",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodingSystem",
                table: "DiagnosisVersions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionKey",
                table: "DiagnosisVersions",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "DiagnosisVersions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "SourceNote",
                table: "DiagnosisVersions",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Diagnoses",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodingSystem",
                table: "Diagnoses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionKey",
                table: "Diagnoses",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "Diagnoses",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<string>(
                name: "SourceNote",
                table: "Diagnoses",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiagnosisLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DiagnosisId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TargetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosisLinks", x => x.Id);
                    table.CheckConstraint("CK_DiagnosisLinks_LinkType", "[LinkType] COLLATE Latin1_General_CS_AS IN ('Finding','PerioExam')");
                    table.ForeignKey(
                        name: "FK_DiagnosisLinks_Diagnoses_DiagnosisId",
                        column: x => x.DiagnosisId,
                        principalTable: "Diagnoses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiagnosisVersions_ChangeType",
                table: "DiagnosisVersions",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','Corrected','Withdrawn','Amended','Resolved','Reactivated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiagnosisVersions_Status",
                table: "DiagnosisVersions",
                sql: "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Resolved','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_CodeChars",
                table: "Diagnoses",
                sql: "[Code] IS NULL OR REPLACE([Code], N'-', N'') COLLATE Latin1_General_BIN2 NOT LIKE N'%[^A-Za-z0-9._]%'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_CodingPair",
                table: "Diagnoses",
                sql: "([CodingSystem] IS NULL AND [Code] IS NULL) OR ([CodingSystem] IS NOT NULL AND [Code] IS NOT NULL AND LEN(LTRIM(RTRIM([Code]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_CodingSystem",
                table: "Diagnoses",
                sql: "[CodingSystem] IS NULL OR [CodingSystem] COLLATE Latin1_General_CS_AS IN ('ICD-10-CM','SNODENT','Local')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_Region",
                table: "Diagnoses",
                sql: "[RegionKey] IS NULL OR ([RegionKey] COLLATE Latin1_General_CS_AS IN ('FullMouth','UpperArch','LowerArch','UpperRight','UpperLeft','LowerRight','LowerLeft','SoftTissue','Tmj') AND [ToothKey] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_Source",
                table: "Diagnoses",
                sql: "[Source] COLLATE Latin1_General_CS_AS IN ('Manual','Imported','Mapped')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_SourceNote",
                table: "Diagnoses",
                sql: "[SourceNote] IS NULL OR ([Source] <> 'Manual' AND LEN(LTRIM(RTRIM([SourceNote]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [SourceNote] COLLATE Latin1_General_BIN2) = 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_Status",
                table: "Diagnoses",
                sql: "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Resolved','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_WithdrawnStamp",
                table: "Diagnoses",
                sql: "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) OR ([Status] IN ('Active','Resolved') AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosisLinks_DiagnosisId_LinkType_TargetId",
                table: "DiagnosisLinks",
                columns: new[] { "DiagnosisId", "LinkType", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosisLinks_PatientId",
                table: "DiagnosisLinks",
                column: "PatientId");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DiagnosisLinks_Immutable] ON [DiagnosisLinks] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51075, 'A link from a diagnosis is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DiagnosisLinks_SamePatient] ON [DiagnosisLinks] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Diagnoses] d ON d.[Id] = i.[DiagnosisId] WHERE d.[PatientId] <> i.[PatientId])
        THROW 51076, 'A link must carry the patient of its diagnosis.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[LinkType] = 'Finding'
               AND NOT EXISTS (SELECT 1 FROM [ToothFindings] f WHERE f.[Id] = i.[TargetId] AND f.[PatientId] = i.[PatientId]))
        THROW 51076, 'A diagnosis can only be linked to a finding of the same patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[LinkType] = 'PerioExam'
               AND NOT EXISTS (SELECT 1 FROM [PerioExams] x WHERE x.[Id] = i.[TargetId] AND x.[PatientId] = i.[PatientId]))
        THROW 51076, 'A diagnosis can only be linked to a periodontal chart of the same patient.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_DiagnosisLinks_SamePatient]");
            migrationBuilder.Sql("DROP TRIGGER [TR_DiagnosisLinks_Immutable]");

            migrationBuilder.DropTable(
                name: "DiagnosisLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DiagnosisVersions_ChangeType",
                table: "DiagnosisVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DiagnosisVersions_Status",
                table: "DiagnosisVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_CodeChars",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_CodingPair",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_CodingSystem",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_Region",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_Source",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_SourceNote",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_Status",
                table: "Diagnoses");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Diagnoses_WithdrawnStamp",
                table: "Diagnoses");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "DiagnosisVersions");

            migrationBuilder.DropColumn(
                name: "CodingSystem",
                table: "DiagnosisVersions");

            migrationBuilder.DropColumn(
                name: "RegionKey",
                table: "DiagnosisVersions");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "DiagnosisVersions");

            migrationBuilder.DropColumn(
                name: "SourceNote",
                table: "DiagnosisVersions");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Diagnoses");

            migrationBuilder.DropColumn(
                name: "CodingSystem",
                table: "Diagnoses");

            migrationBuilder.DropColumn(
                name: "RegionKey",
                table: "Diagnoses");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "Diagnoses");

            migrationBuilder.DropColumn(
                name: "SourceNote",
                table: "Diagnoses");

            migrationBuilder.AlterColumn<string>(
                name: "ChangeType",
                table: "DiagnosisVersions",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(12)",
                oldMaxLength: 12);

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiagnosisVersions_ChangeType",
                table: "DiagnosisVersions",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','Corrected','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DiagnosisVersions_Status",
                table: "DiagnosisVersions",
                sql: "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_Status",
                table: "Diagnoses",
                sql: "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Diagnoses_WithdrawnStamp",
                table: "Diagnoses",
                sql: "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) OR ([Status] = 'Active' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
        }
    }
}
