using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDataExportHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DataExportHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubjectRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IncludesDocumentFiles = table.Column<bool>(type: "bit", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Result = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PatientCount = table.Column<int>(type: "int", nullable: false),
                    FileCount = table.Column<int>(type: "int", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: true),
                    ProblemCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataExportHistory", x => x.Id);
                    table.CheckConstraint("CK_DataExportHistory_Finished", "([Result] = 'Started' AND [FinishedAtUtc] IS NULL) OR ([Result] <> 'Started' AND [FinishedAtUtc] IS NOT NULL)");
                    table.CheckConstraint("CK_DataExportHistory_Hash", "[Sha256] IS NULL OR (LEN([Sha256]) = 64 AND [Sha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_BIN2)");
                    table.CheckConstraint("CK_DataExportHistory_Kind", "[Kind] IN ('Package', 'ReadableRecord')");
                    table.CheckConstraint("CK_DataExportHistory_ReadableRecord", "[Kind] <> 'ReadableRecord' OR ([Scope] = 'OnePatient' AND [IncludesDocumentFiles] = 0)");
                    table.CheckConstraint("CK_DataExportHistory_Result", "[Result] IN ('Started', 'Complete', 'Partial', 'Failed')");
                    table.CheckConstraint("CK_DataExportHistory_Scope", "[Scope] IN ('OnePatient', 'AllPatients')");
                    table.CheckConstraint("CK_DataExportHistory_Subject", "([Scope] = 'OnePatient' AND [SubjectRecordId] IS NOT NULL) OR ([Scope] = 'AllPatients' AND [SubjectRecordId] IS NULL)");
                    table.CheckConstraint("CK_DataExportHistory_Times", "[RequestedAtUtc] >= '2000-01-01' AND [RequestedAtUtc] < '2200-01-01' AND ([FinishedAtUtc] IS NULL OR ([FinishedAtUtc] >= [RequestedAtUtc] AND [FinishedAtUtc] < '2200-01-01'))");
                    table.CheckConstraint("CK_DataExportHistory_Totals", "[PatientCount] >= 0 AND [FileCount] >= 0 AND [SizeBytes] >= 0 AND [ProblemCount] >= 0");
                    table.ForeignKey(
                        name: "FK_DataExportHistory_UserAccounts_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DataExportHistory_ExportId",
                table: "DataExportHistory",
                column: "ExportId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DataExportHistory_RequestedAtUtc",
                table: "DataExportHistory",
                column: "RequestedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_DataExportHistory_RequestedByUserId",
                table: "DataExportHistory",
                column: "RequestedByUserId");

            // ALV-018-C01 (errors 51250 to 51252): an export is recorded once and never deleted; what it recorded never changes; only a row that is still Started may change, into a finished state with its totals.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_DataExportHistory_Guard] ON [DataExportHistory] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51250, 'An export history row is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Result] <> 'Started')
        THROW 51252, 'A finished export history row never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[ExportId] <> d.[ExportId] OR i.[Kind] <> d.[Kind] OR i.[Scope] <> d.[Scope]
                  OR ISNULL(i.[SubjectRecordId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SubjectRecordId], '00000000-0000-0000-0000-000000000000')
                  OR i.[IncludesDocumentFiles] <> d.[IncludesDocumentFiles] OR i.[RequestedByUserId] <> d.[RequestedByUserId] OR i.[RequestedAtUtc] <> d.[RequestedAtUtc])
        THROW 51251, 'An export history row keeps what it recorded: who asked, when, what and for whom.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no export has been recorded
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [DataExportHistory])
    THROW 51253, 'Export history exists, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "DataExportHistory");
        }
    }
}
