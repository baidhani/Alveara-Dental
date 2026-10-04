using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOdontogram : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToothFindings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Surface = table.Column<string>(type: "nchar(1)", fixedLength: true, maxLength: 1, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    State = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
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
                    table.PrimaryKey("PK_ToothFindings", x => x.Id);
                    table.CheckConstraint("CK_ToothFindings_Condition", "[Condition] COLLATE Latin1_General_CS_AS IN ('Caries','Restoration','Crown','Missing','Implant','RootCanal')");
                    table.CheckConstraint("CK_ToothFindings_State", "[State] COLLATE Latin1_General_CS_AS IN ('Existing','Diagnosed','Planned','Completed')");
                    table.CheckConstraint("CK_ToothFindings_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Withdrawn')");
                    table.CheckConstraint("CK_ToothFindings_Surface", "[Surface] IS NULL OR [Surface] COLLATE Latin1_General_CS_AS IN ('M','O','I','D','B','F','L')");
                    table.CheckConstraint("CK_ToothFindings_SurfaceExistsOnTooth", "[Surface] IS NULL OR [Surface] COLLATE Latin1_General_CS_AS IN ('M','D','L') OR ([Surface] COLLATE Latin1_General_CS_AS IN ('I','F') AND CAST(SUBSTRING([ToothKey], 2, 1) AS int) <= 3) OR ([Surface] COLLATE Latin1_General_CS_AS IN ('O','B') AND CAST(SUBSTRING([ToothKey], 2, 1) AS int) >= 4)");
                    table.CheckConstraint("CK_ToothFindings_SurfaceMatchesCondition", "([Condition] COLLATE Latin1_General_CS_AS IN ('Caries','Restoration') AND [Surface] IS NOT NULL) OR ([Condition] COLLATE Latin1_General_CS_AS NOT IN ('Caries','Restoration') AND [Surface] IS NULL)");
                    table.CheckConstraint("CK_ToothFindings_ToothKey", "[ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48','51','52','53','54','55','61','62','63','64','65','71','72','73','74','75','81','82','83','84','85')");
                    table.CheckConstraint("CK_ToothFindings_WithdrawnStamp", "([Status] = 'Withdrawn' AND [WithdrawnAtUtc] IS NOT NULL AND [WithdrawnByUserId] IS NOT NULL AND [WithdrawnReason] IS NOT NULL AND LEN(LTRIM(RTRIM([WithdrawnReason]))) > 0) OR ([Status] = 'Active' AND [WithdrawnAtUtc] IS NULL AND [WithdrawnByUserId] IS NULL AND [WithdrawnReason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_ToothFindings_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ToothFindingVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: false),
                    Surface = table.Column<string>(type: "nchar(1)", fixedLength: true, maxLength: 1, nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    State = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToothFindingVersions", x => x.Id);
                    table.CheckConstraint("CK_ToothFindingVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','StateChanged','Withdrawn')");
                    table.ForeignKey(
                        name: "FK_ToothFindingVersions_ToothFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "ToothFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindings_PatientId_Status",
                table: "ToothFindings",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindings_PatientId_ToothKey_Surface_Condition",
                table: "ToothFindings",
                columns: new[] { "PatientId", "ToothKey", "Surface", "Condition" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindingVersions_FindingId_VersionNumber",
                table: "ToothFindingVersions",
                columns: new[] { "FindingId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindingVersions_PatientId",
                table: "ToothFindingVersions",
                column: "PatientId");

            // STORY-006: the database refuses, independently of the application, to delete a tooth finding (it is withdrawn with a reason) and to change its history.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ToothFindings_NoDelete] ON [ToothFindings] INSTEAD OF DELETE AS
BEGIN
    THROW 51055, 'Tooth findings are never deleted; they are withdrawn with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ToothFindingVersions_Immutable] ON [ToothFindingVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51056, 'The history of a tooth finding is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToothFindingVersions");

            migrationBuilder.DropTable(
                name: "ToothFindings");
        }
    }
}
