using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N022 A3 (owner decision D-1 B; reviews A3-STEP0-ADDENDUM-01/02): typed, nullable links instead of free text for new entries.
    /// - Clearances.DocumentId / ClearanceVersions.DocumentId -> Documents (FK, restrict); a clearance's document must belong to the clearance's patient (trigger, 51288).
    /// - PerioExamLinks.DiagnosisId -> Diagnoses (FK, restrict), only for LinkType 'Diagnosis' (CK); the diagnosis must belong to the link's patient (trigger, 51289).
    /// - Existing rows are untouched: the legacy DocumentReference / Reference text stays and is shown as legacy; nothing is converted or guessed.
    /// - Down refuses (51290) once any typed link exists, so no link is silently discarded.
    /// </summary>
    public partial class AddTypedClearanceDocumentAndPerioDiagnosisLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DiagnosisId",
                table: "PerioExamLinks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "ClearanceVersions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "Clearances",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerioExamLinks_DiagnosisId",
                table: "PerioExamLinks",
                column: "DiagnosisId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PerioExamLinks_DiagnosisIdType",
                table: "PerioExamLinks",
                sql: "[DiagnosisId] IS NULL OR [LinkType] = 'Diagnosis'");

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceVersions_DocumentId",
                table: "ClearanceVersions",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Clearances_DocumentId",
                table: "Clearances",
                column: "DocumentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clearances_Documents_DocumentId",
                table: "Clearances",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ClearanceVersions_Documents_DocumentId",
                table: "ClearanceVersions",
                column: "DocumentId",
                principalTable: "Documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PerioExamLinks_Diagnoses_DiagnosisId",
                table: "PerioExamLinks",
                column: "DiagnosisId",
                principalTable: "Diagnoses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Clearances_DocumentSamePatient] ON [Clearances] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] d ON d.[Id] = i.[DocumentId] WHERE d.[PatientId] <> i.[PatientId])
        THROW 51288, 'A clearance can only point to a document of the same patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PerioExamLinks_DiagnosisSamePatient] ON [PerioExamLinks] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Diagnoses] d ON d.[Id] = i.[DiagnosisId] WHERE d.[PatientId] <> i.[PatientId])
        THROW 51289, 'A periodontal chart can only be linked to a diagnosis of the same patient.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Clearances] WHERE [DocumentId] IS NOT NULL) OR EXISTS (SELECT 1 FROM [ClearanceVersions] WHERE [DocumentId] IS NOT NULL)
    OR EXISTS (SELECT 1 FROM [PerioExamLinks] WHERE [DiagnosisId] IS NOT NULL)
    THROW 51290, 'Typed clearance-document or periodontal-diagnosis links have been recorded, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_PerioExamLinks_DiagnosisSamePatient]");
            migrationBuilder.Sql("DROP TRIGGER [TR_Clearances_DocumentSamePatient]");

            migrationBuilder.DropForeignKey(
                name: "FK_Clearances_Documents_DocumentId",
                table: "Clearances");

            migrationBuilder.DropForeignKey(
                name: "FK_ClearanceVersions_Documents_DocumentId",
                table: "ClearanceVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_PerioExamLinks_Diagnoses_DiagnosisId",
                table: "PerioExamLinks");

            migrationBuilder.DropIndex(
                name: "IX_PerioExamLinks_DiagnosisId",
                table: "PerioExamLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PerioExamLinks_DiagnosisIdType",
                table: "PerioExamLinks");

            migrationBuilder.DropIndex(
                name: "IX_ClearanceVersions_DocumentId",
                table: "ClearanceVersions");

            migrationBuilder.DropIndex(
                name: "IX_Clearances_DocumentId",
                table: "Clearances");

            migrationBuilder.DropColumn(
                name: "DiagnosisId",
                table: "PerioExamLinks");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "ClearanceVersions");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "Clearances");
        }
    }
}
