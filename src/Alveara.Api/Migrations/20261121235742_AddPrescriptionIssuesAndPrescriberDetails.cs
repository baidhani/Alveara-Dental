using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N026 S6: prescription issues (owned through the prescription: no PatientId, by the ownership decision) and the optional prescriber details on a staff profile. An issue is append-only
    /// (TR_PrescriptionIssues_Immutable, 51305, refuses a change or a deletion). Down refuses (51306) once any issue exists, because issues are history; and once any prescriber detail is recorded.
    /// </summary>
    public partial class AddPrescriptionIssuesAndPrescriberDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CredentialLabel",
                table: "StaffProfiles",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseJurisdiction",
                table: "StaffProfiles",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LicenseNumber",
                table: "StaffProfiles",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Npi",
                table: "StaffProfiles",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PrescriptionIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrescriptionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssueKind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    StatusAtIssue = table.Column<string>(type: "nvarchar(14)", maxLength: 14, nullable: false),
                    EventCutoffId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    EnvelopeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RenderBundleId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrescriptionIssues", x => x.Id);
                    table.CheckConstraint("CK_PrescriptionIssues_Kind", "[IssueKind] COLLATE Latin1_General_CS_AS IN ('Print','RecordCopy')");
                    table.CheckConstraint("CK_PrescriptionIssues_Print", "[IssueKind] <> 'Print' OR [StatusAtIssue] = 'Active'");
                    table.CheckConstraint("CK_PrescriptionIssues_Template", "[TemplateVersion] = 'p2'");
                    table.ForeignKey(
                        name: "FK_PrescriptionIssues_PrescriptionEvents_EventCutoffId",
                        column: x => x.EventCutoffId,
                        principalTable: "PrescriptionEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionIssues_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrescriptionIssues_UserAccounts_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StaffProfiles_Npi",
                table: "StaffProfiles",
                sql: "[Npi] IS NULL OR ([Npi] NOT LIKE '%[^0-9]%' AND LEN([Npi]) = 10)");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionIssues_EventCutoffId",
                table: "PrescriptionIssues",
                column: "EventCutoffId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionIssues_IssuedByUserId",
                table: "PrescriptionIssues",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PrescriptionIssues_PrescriptionId_IdempotencyKey",
                table: "PrescriptionIssues",
                columns: new[] { "PrescriptionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PrescriptionIssues_Immutable] ON [PrescriptionIssues] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51305, 'A prescription issue is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [PrescriptionIssues]) OR EXISTS (SELECT 1 FROM [StaffProfiles] WHERE [CredentialLabel] IS NOT NULL OR [LicenseNumber] IS NOT NULL OR [LicenseJurisdiction] IS NOT NULL OR [Npi] IS NOT NULL)
    THROW 51306, 'Prescription issues or prescriber details exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_PrescriptionIssues_Immutable];");
            migrationBuilder.DropTable(
                name: "PrescriptionIssues");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StaffProfiles_Npi",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "CredentialLabel",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "LicenseJurisdiction",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "LicenseNumber",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "Npi",
                table: "StaffProfiles");
        }
    }
}
