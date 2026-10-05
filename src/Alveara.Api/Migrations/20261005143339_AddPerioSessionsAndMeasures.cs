using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPerioSessionsAndMeasures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Plaque",
                table: "PerioReadings",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Suppuration",
                table: "PerioReadings",
                type: "bit",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PerioExamLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioExamLinks", x => x.Id);
                    table.CheckConstraint("CK_PerioExamLinks_LinkType", "[LinkType] COLLATE Latin1_General_CS_AS IN ('Diagnosis','TreatmentPlan','Encounter','HistoryEntry')");
                    table.CheckConstraint("CK_PerioExamLinks_ReferenceNotBlank", "LEN(LTRIM(RTRIM([Reference]))) > 0");
                    table.ForeignKey(
                        name: "FK_PerioExamLinks_PerioExams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "PerioExams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerioSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioSessions", x => x.Id);
                    table.CheckConstraint("CK_PerioSessions_ClosingStamp", "([Status] = 'Draft' AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL) OR ([Status] <> 'Draft' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_PerioSessions_ExamMatchesStatus", "([Status] = 'Finalized' AND [ExamId] IS NOT NULL) OR ([Status] <> 'Finalized' AND [ExamId] IS NULL)");
                    table.CheckConstraint("CK_PerioSessions_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Draft','Finalized','Abandoned')");
                    table.ForeignKey(
                        name: "FK_PerioSessions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PerioSessions_PerioExams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "PerioExams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerioToothRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Mobility = table.Column<byte>(type: "tinyint", nullable: true),
                    Furcation = table.Column<byte>(type: "tinyint", nullable: true),
                    Excluded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioToothRecords", x => x.Id);
                    table.CheckConstraint("CK_PerioToothRecords_ExcludedHasNoGrades", "[Excluded] = 0 OR ([Mobility] IS NULL AND [Furcation] IS NULL)");
                    table.CheckConstraint("CK_PerioToothRecords_Furcation", "[Furcation] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_PerioToothRecords_FurcationTooth", "[Furcation] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ('14','16','17','18','24','26','27','28','36','37','38','46','47','48')");
                    table.CheckConstraint("CK_PerioToothRecords_Mobility", "[Mobility] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_PerioToothRecords_ToothKey", "[ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48')");
                    table.ForeignKey(
                        name: "FK_PerioToothRecords_PerioExams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "PerioExams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerioSessionReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Site = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ProbingDepthMm = table.Column<byte>(type: "tinyint", nullable: false),
                    RecessionMm = table.Column<byte>(type: "tinyint", nullable: false),
                    Bleeding = table.Column<bool>(type: "bit", nullable: false),
                    Suppuration = table.Column<bool>(type: "bit", nullable: true),
                    Plaque = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioSessionReadings", x => x.Id);
                    table.CheckConstraint("CK_PerioSessionReadings_ProbingDepth", "[ProbingDepthMm] BETWEEN 0 AND 15");
                    table.CheckConstraint("CK_PerioSessionReadings_Recession", "[RecessionMm] BETWEEN 0 AND 15");
                    table.CheckConstraint("CK_PerioSessionReadings_Site", "[Site] COLLATE Latin1_General_CS_AS IN ('DB','B','MB','DL','L','ML')");
                    table.CheckConstraint("CK_PerioSessionReadings_ToothKey", "[ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48')");
                    table.ForeignKey(
                        name: "FK_PerioSessionReadings_PerioSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "PerioSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerioSessionTeeth",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Mobility = table.Column<byte>(type: "tinyint", nullable: true),
                    Furcation = table.Column<byte>(type: "tinyint", nullable: true),
                    Excluded = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioSessionTeeth", x => x.Id);
                    table.CheckConstraint("CK_PerioSessionTeeth_ExcludedHasNoGrades", "[Excluded] = 0 OR ([Mobility] IS NULL AND [Furcation] IS NULL)");
                    table.CheckConstraint("CK_PerioSessionTeeth_Furcation", "[Furcation] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_PerioSessionTeeth_FurcationTooth", "[Furcation] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ('14','16','17','18','24','26','27','28','36','37','38','46','47','48')");
                    table.CheckConstraint("CK_PerioSessionTeeth_Mobility", "[Mobility] BETWEEN 0 AND 3");
                    table.CheckConstraint("CK_PerioSessionTeeth_ToothKey", "[ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48')");
                    table.ForeignKey(
                        name: "FK_PerioSessionTeeth_PerioSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "PerioSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerioExamLinks_ExamId_LinkType_Reference",
                table: "PerioExamLinks",
                columns: new[] { "ExamId", "LinkType", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerioExamLinks_PatientId",
                table: "PerioExamLinks",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PerioSessionReadings_SessionId_ToothKey_Site",
                table: "PerioSessionReadings",
                columns: new[] { "SessionId", "ToothKey", "Site" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerioSessions_ExamId",
                table: "PerioSessions",
                column: "ExamId",
                unique: true,
                filter: "[ExamId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PerioSessions_OneDraftPerPatient",
                table: "PerioSessions",
                column: "PatientId",
                unique: true,
                filter: "[Status] = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "IX_PerioSessionTeeth_SessionId_ToothKey",
                table: "PerioSessionTeeth",
                columns: new[] { "SessionId", "ToothKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerioToothRecords_ExamId_ToothKey",
                table: "PerioToothRecords",
                columns: new[] { "ExamId", "ToothKey" },
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioToothRecords_Immutable] ON [PerioToothRecords] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51066, 'The whole-tooth records of a periodontal chart are never edited or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioSessions_NoDelete] ON [PerioSessions] INSTEAD OF DELETE AS
BEGIN
    THROW 51067, 'A periodontal chart session is never deleted; an unwanted draft is abandoned.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioSessions_ClosedIsFinal] ON [PerioSessions] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted WHERE [Status] <> 'Draft')
        THROW 51068, 'A finalized or abandoned chart session can no longer be changed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioSessionReadings_DraftOnly] ON [PerioSessionReadings] AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM (SELECT [SessionId] FROM inserted UNION SELECT [SessionId] FROM deleted) x JOIN [PerioSessions] s ON s.[Id] = x.[SessionId] WHERE s.[Status] <> 'Draft')
        THROW 51069, 'The entries of a finalized or abandoned chart session can no longer be changed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioSessionTeeth_DraftOnly] ON [PerioSessionTeeth] AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM (SELECT [SessionId] FROM inserted UNION SELECT [SessionId] FROM deleted) x JOIN [PerioSessions] s ON s.[Id] = x.[SessionId] WHERE s.[Status] <> 'Draft')
        THROW 51069, 'The entries of a finalized or abandoned chart session can no longer be changed.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioExamLinks_Immutable] ON [PerioExamLinks] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51070, 'The links of a periodontal chart are append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerioExamLinks");

            migrationBuilder.DropTable(
                name: "PerioSessionReadings");

            migrationBuilder.DropTable(
                name: "PerioSessionTeeth");

            migrationBuilder.DropTable(
                name: "PerioToothRecords");

            migrationBuilder.DropTable(
                name: "PerioSessions");

            migrationBuilder.DropColumn(
                name: "Plaque",
                table: "PerioReadings");

            migrationBuilder.DropColumn(
                name: "Suppuration",
                table: "PerioReadings");
        }
    }
}
