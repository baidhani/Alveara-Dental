using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N026 S7: treatment-plan patient copy issues (owned through the plan: no PatientId, by the ownership decision). An issue is append-only (TR_TreatmentPlanCopyIssues_Immutable, 51307, refuses a
    /// change or a deletion). Down refuses (51308) once any issue exists, because issues are history.
    /// </summary>
    public partial class AddTreatmentPlanCopyIssues : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TreatmentPlanCopyIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ItemStateCutoffJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReferenceCodeAtIssue = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IssuedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersion = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    EnvelopeJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RenderBundleId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanCopyIssues", x => x.Id);
                    table.CheckConstraint("CK_TreatmentPlanCopyIssues_Revision", "[RevisionNumber] >= 1");
                    table.CheckConstraint("CK_TreatmentPlanCopyIssues_Template", "[TemplateVersion] = 'v2'");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanCopyIssues_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanCopyIssues_UserAccounts_IssuedByUserId",
                        column: x => x.IssuedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanCopyIssues_IssuedByUserId",
                table: "TreatmentPlanCopyIssues",
                column: "IssuedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanCopyIssues_PlanId_IdempotencyKey",
                table: "TreatmentPlanCopyIssues",
                columns: new[] { "PlanId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanCopyIssues_Immutable] ON [TreatmentPlanCopyIssues] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51307, 'A treatment plan copy issue is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [TreatmentPlanCopyIssues])
    THROW 51308, 'Treatment plan copy issues exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_TreatmentPlanCopyIssues_Immutable];");
            migrationBuilder.DropTable(
                name: "TreatmentPlanCopyIssues");
        }
    }
}
