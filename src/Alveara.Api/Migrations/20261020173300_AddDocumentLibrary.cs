using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentEvents_Kind",
                table: "DocumentEvents");

            migrationBuilder.AddColumn<Guid>(
                name: "EncounterId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InactivatedAtUtc",
                table: "Documents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InactivatedByUserId",
                table: "Documents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InactiveReason",
                table: "Documents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionKey",
                table: "Documents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Documents",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "ToothKey",
                table: "Documents",
                type: "nchar(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentIntegrity",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CheckedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentIntegrity", x => x.DocumentId);
                    table.CheckConstraint("CK_DocumentIntegrity_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Ok','Missing','Corrupt')");
                    table.CheckConstraint("CK_DocumentIntegrity_Time", "[CheckedAtUtc] >= '2000-01-01' AND [CheckedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_DocumentIntegrity_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentIntegrity_UserAccounts_CheckedByUserId",
                        column: x => x.CheckedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DocumentStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentStatusChanges", x => x.Id);
                    table.CheckConstraint("CK_DocumentStatusChanges_Reason", "([ToStatus] = 'Inactive' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0) OR ([ToStatus] = 'Active' AND [Reason] IS NULL)");
                    table.CheckConstraint("CK_DocumentStatusChanges_Status", "[ToStatus] COLLATE Latin1_General_CS_AS IN ('Active','Inactive')");
                    table.CheckConstraint("CK_DocumentStatusChanges_Time", "[AtUtc] >= '2000-01-01' AND [AtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_DocumentStatusChanges_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentStatusChanges_UserAccounts_ByUserId",
                        column: x => x.ByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_EncounterId",
                table: "Documents",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_InactivatedByUserId",
                table: "Documents",
                column: "InactivatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_PatientId_Status",
                table: "Documents",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_Inactive",
                table: "Documents",
                sql: "([Status] = 'Active' AND [InactiveReason] IS NULL AND [InactivatedByUserId] IS NULL AND [InactivatedAtUtc] IS NULL) OR ([Status] = 'Inactive' AND [InactiveReason] IS NOT NULL AND LEN(LTRIM(RTRIM([InactiveReason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [InactiveReason] COLLATE Latin1_General_BIN2) = 0 AND [InactivatedByUserId] IS NOT NULL AND [InactivatedAtUtc] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_Region",
                table: "Documents",
                sql: "[RegionKey] IS NULL OR ([RegionKey] COLLATE Latin1_General_CS_AS IN ('FullMouth','UpperArch','LowerArch','UpperRight','UpperLeft','LowerRight','LowerLeft','SoftTissue','Tmj') AND [ToothKey] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_Status",
                table: "Documents",
                sql: "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Inactive')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Documents_ToothKey",
                table: "Documents",
                sql: "[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48','51','52','53','54','55','61','62','63','64','65','71','72','73','74','75','81','82','83','84','85')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentEvents_Kind",
                table: "DocumentEvents",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Imported','ImportedAsDuplicate','Categorized','MetadataEdited','Opened','Inactivated','Reactivated')");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIntegrity_CheckedByUserId",
                table: "DocumentIntegrity",
                column: "CheckedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentIntegrity_Status",
                table: "DocumentIntegrity",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentStatusChanges_ByUserId",
                table: "DocumentStatusChanges",
                column: "ByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentStatusChanges_DocumentId_AtUtc",
                table: "DocumentStatusChanges",
                columns: new[] { "DocumentId", "AtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_Encounters_EncounterId",
                table: "Documents",
                column: "EncounterId",
                principalTable: "Encounters",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Documents_UserAccounts_InactivatedByUserId",
                table: "Documents",
                column: "InactivatedByUserId",
                principalTable: "UserAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ---- ALV-010-C01 triggers (errors 51192 and 51193; 51194 guards the rollback; 51195 and up are free). ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Documents_EncounterLinks] ON [Documents] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e ON e.[Id] = i.[EncounterId] WHERE e.[PatientId] <> i.[PatientId])
        THROW 51192, 'A document can only name an encounter of its own patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DocumentStatusChanges_Immutable] ON [DocumentStatusChanges] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51193, 'A document status change is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while the library holds nothing it would lose: afterwards these columns and rows are patient records that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [DocumentStatusChanges])
   OR EXISTS (SELECT 1 FROM [DocumentIntegrity])
   OR EXISTS (SELECT 1 FROM [Documents] WHERE [EncounterId] IS NOT NULL OR [ToothKey] IS NOT NULL OR [RegionKey] IS NOT NULL OR [Status] <> 'Active')
    THROW 51194, 'Document library data exists, so this migration cannot be rolled back.', 1;");
            // the trigger on the Documents table outlives the added columns unless it is dropped here (the status-change table's trigger goes with its table)
            migrationBuilder.Sql(@"DROP TRIGGER IF EXISTS [TR_Documents_EncounterLinks]");
            migrationBuilder.DropForeignKey(
                name: "FK_Documents_Encounters_EncounterId",
                table: "Documents");

            migrationBuilder.DropForeignKey(
                name: "FK_Documents_UserAccounts_InactivatedByUserId",
                table: "Documents");

            migrationBuilder.DropTable(
                name: "DocumentIntegrity");

            migrationBuilder.DropTable(
                name: "DocumentStatusChanges");

            migrationBuilder.DropIndex(
                name: "IX_Documents_EncounterId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_InactivatedByUserId",
                table: "Documents");

            migrationBuilder.DropIndex(
                name: "IX_Documents_PatientId_Status",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_Inactive",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_Region",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_Status",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Documents_ToothKey",
                table: "Documents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentEvents_Kind",
                table: "DocumentEvents");

            migrationBuilder.DropColumn(
                name: "EncounterId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "InactivatedAtUtc",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "InactivatedByUserId",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "InactiveReason",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "RegionKey",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "ToothKey",
                table: "Documents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentEvents_Kind",
                table: "DocumentEvents",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Imported','ImportedAsDuplicate','Categorized','MetadataEdited','Opened')");
        }
    }
}
