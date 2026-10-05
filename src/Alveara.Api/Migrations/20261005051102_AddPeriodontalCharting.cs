using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPeriodontalCharting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PerioExams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReadingCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioExams", x => x.Id);
                    table.CheckConstraint("CK_PerioExams_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0");
                    table.CheckConstraint("CK_PerioExams_ReadingCount", "[ReadingCount] BETWEEN 1 AND 192");
                    table.ForeignKey(
                        name: "FK_PerioExams_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PerioReadings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExamId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Site = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ProbingDepthMm = table.Column<byte>(type: "tinyint", nullable: false),
                    RecessionMm = table.Column<byte>(type: "tinyint", nullable: false),
                    Bleeding = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerioReadings", x => x.Id);
                    table.CheckConstraint("CK_PerioReadings_ProbingDepth", "[ProbingDepthMm] BETWEEN 0 AND 15");
                    table.CheckConstraint("CK_PerioReadings_Recession", "[RecessionMm] BETWEEN 0 AND 15");
                    table.CheckConstraint("CK_PerioReadings_Site", "[Site] COLLATE Latin1_General_CS_AS IN ('DB','B','MB','DL','L','ML')");
                    table.CheckConstraint("CK_PerioReadings_ToothKey", "[ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48')");
                    table.ForeignKey(
                        name: "FK_PerioReadings_PerioExams_ExamId",
                        column: x => x.ExamId,
                        principalTable: "PerioExams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PerioExams_PatientId_IdempotencyKey",
                table: "PerioExams",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerioExams_PatientId_RecordedAtUtc",
                table: "PerioExams",
                columns: new[] { "PatientId", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PerioReadings_ExamId_ToothKey_Site",
                table: "PerioReadings",
                columns: new[] { "ExamId", "ToothKey", "Site" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerioReadings_ToothKey",
                table: "PerioReadings",
                column: "ToothKey");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioExams_Immutable] ON [PerioExams] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51064, 'A periodontal chart is never edited or deleted; a correction is recorded as a new chart.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioReadings_Immutable] ON [PerioReadings] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51065, 'The readings of a periodontal chart are never edited or deleted.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PerioReadings");

            migrationBuilder.DropTable(
                name: "PerioExams");
        }
    }
}
