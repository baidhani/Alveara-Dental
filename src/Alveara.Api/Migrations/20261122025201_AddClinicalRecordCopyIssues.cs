using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N026 S8: clinical record copy issues, directly patient-owned (they have no parent record), so the table joins the established merge machinery exactly as the tables of
    /// AddPatientMerges and GuardRetiredPatientRecords did, written out here for one table:
    /// - TR_ClinicalRecordCopyIssues_Immutable (INSTEAD OF UPDATE, DELETE): append-only (51309), with the /*patient-merge*/ stand-aside that, inside a merge (session value alveara.patient_merge = 1),
    ///   applies only the PatientId change of the rows named (the AddPatientMerges INSTEAD OF shape);
    /// - TR_ClinicalRecordCopyIssues_MergeScope (AFTER INSERT, UPDATE, DELETE): inside a merge nothing but PatientId may change and no row may be added or removed (51234), every other column listed;
    ///   and the /*retired-record-guard*/ check: outside a merge a new copy for a merged-away patient is refused (51235).
    /// Down refuses (51309 is the table's; 51310 here) once any copy exists, because issues are history.
    /// </summary>
    public partial class AddClinicalRecordCopyIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClinicalRecordCopyIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GeneratedAsOfUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    RenderBundleId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalRecordCopyIssues", x => x.Id);
                    table.CheckConstraint("CK_ClinicalRecordCopyIssues_Template", "[TemplateVersion] = 'v1'");
                    table.ForeignKey(
                        name: "FK_ClinicalRecordCopyIssues_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ClinicalRecordCopyIssues_UserAccounts_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordCopyIssues_IssuedByUserId",
                table: "ClinicalRecordCopyIssues",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalRecordCopyIssues_PatientId_IdempotencyKey",
                table: "ClinicalRecordCopyIssues",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ClinicalRecordCopyIssues_Immutable] ON [ClinicalRecordCopyIssues] INSTEAD OF UPDATE, DELETE AS
/*patient-merge*/ IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND ((EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR NOT (EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted))) BEGIN UPDATE t SET t.[PatientId] = i.[PatientId] FROM [ClinicalRecordCopyIssues] t JOIN inserted i ON t.[Id] = i.[Id]; RETURN; END /*end patient-merge*/
BEGIN
    THROW 51309, 'A clinical record copy issue is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ClinicalRecordCopyIssues_MergeScope] ON [ClinicalRecordCopyIssues] AFTER INSERT, UPDATE, DELETE AS
BEGIN
/*retired-record-guard*/ IF ISNULL(CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')), 0) <> 1 AND EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT 1 FROM inserted i JOIN [Patients] p ON p.[Id] = i.[PatientId] WHERE p.[MergedAtUtc] IS NOT NULL)
        THROW 51235, 'A patient record that was merged into another takes no new records.', 1; /*end retired-record-guard*/
    IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1
       AND ((EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)) OR (NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR UPDATE([Id]) OR UPDATE([GeneratedAsOfUtc]) OR UPDATE([IssuedByUserId]) OR UPDATE([TemplateVersion]) OR UPDATE([RenderBundleId]) OR UPDATE([ContentHash]) OR UPDATE([IdempotencyKey]) OR UPDATE([CorrelationId]))
        THROW 51234, 'During a patient merge only the patient id of an existing row may change.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ClinicalRecordCopyIssues])
    THROW 51310, 'Clinical record copy issues exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_ClinicalRecordCopyIssues_MergeScope];");
            migrationBuilder.Sql("DROP TRIGGER [TR_ClinicalRecordCopyIssues_Immutable];");
            migrationBuilder.DropTable(
                name: "ClinicalRecordCopyIssues");
        }
    }
}
