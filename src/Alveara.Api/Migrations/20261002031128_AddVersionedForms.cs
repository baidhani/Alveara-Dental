using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionedForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormTemplateVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormTemplateVersions_FormTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientForms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ResponsesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StartedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VoidedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    VoidReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientForms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientForms_FormTemplateVersions_TemplateVersionId",
                        column: x => x.TemplateVersionId,
                        principalTable: "FormTemplateVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientForms_FormTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "FormTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientForms_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientFormEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TemplateVersionNumber = table.Column<int>(type: "int", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientFormEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientFormEvents_PatientForms_PatientFormId",
                        column: x => x.PatientFormId,
                        principalTable: "PatientForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignedFormSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientFormId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TemplateVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TemplateVersionNumber = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponsesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SignerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SignerRelationship = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SignerRelationshipNote = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SignatureMethod = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SignatureText = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Attestation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    SignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CapturedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SnapshotSchemaVersion = table.Column<int>(type: "int", nullable: false),
                    SnapshotHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignedFormSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignedFormSnapshots_PatientForms_PatientFormId",
                        column: x => x.PatientFormId,
                        principalTable: "PatientForms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SignedFormSnapshots_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplates_Key",
                table: "FormTemplates",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormTemplateVersions_TemplateId_VersionNumber",
                table: "FormTemplateVersions",
                columns: new[] { "TemplateId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientFormEvents_PatientFormId_OccurredAtUtc",
                table: "PatientFormEvents",
                columns: new[] { "PatientFormId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientForms_PatientId_StartedAtUtc",
                table: "PatientForms",
                columns: new[] { "PatientId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PatientForms_PatientId_TemplateId",
                table: "PatientForms",
                columns: new[] { "PatientId", "TemplateId" },
                unique: true,
                filter: "[Status] = 'Draft'");

            migrationBuilder.CreateIndex(
                name: "IX_PatientForms_TemplateId",
                table: "PatientForms",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientForms_TemplateVersionId",
                table: "PatientForms",
                column: "TemplateVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SignedFormSnapshots_PatientFormId",
                table: "SignedFormSnapshots",
                column: "PatientFormId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignedFormSnapshots_PatientId_SignedAtUtc",
                table: "SignedFormSnapshots",
                columns: new[] { "PatientId", "SignedAtUtc" });

            // ALV-N010: a signed snapshot and a published template version are append-only at the database itself, not just in
            // the application - an UPDATE or DELETE from any path (a script, a console, a bug) is refused.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_SignedFormSnapshots_Immutable] ON [SignedFormSnapshots] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51010, 'Signed form snapshots are immutable.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_FormTemplateVersions_Immutable] ON [FormTemplateVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51011, 'Published form template versions are immutable.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatientFormEvents");

            migrationBuilder.DropTable(
                name: "SignedFormSnapshots");

            migrationBuilder.DropTable(
                name: "PatientForms");

            migrationBuilder.DropTable(
                name: "FormTemplateVersions");

            migrationBuilder.DropTable(
                name: "FormTemplates");
        }
    }
}
