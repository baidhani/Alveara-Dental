using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceLabel = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Policy = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FileBlobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileSha256 = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    ColumnCount = table.Column<int>(type: "int", nullable: false),
                    ColumnNamesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AttemptedRows = table.Column<int>(type: "int", nullable: false),
                    AcceptedRows = table.Column<int>(type: "int", nullable: false),
                    RejectedRows = table.Column<int>(type: "int", nullable: false),
                    SkippedRows = table.Column<int>(type: "int", nullable: false),
                    UnresolvedRows = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                    table.CheckConstraint("CK_ImportBatches_Counters", "[AttemptedRows] >= 0 AND [AcceptedRows] >= 0 AND [RejectedRows] >= 0 AND [SkippedRows] >= 0 AND [UnresolvedRows] >= 0 AND [AcceptedRows] + [RejectedRows] + [SkippedRows] + [UnresolvedRows] <= [AttemptedRows] AND [AttemptedRows] <= [RowCount]");
                    table.CheckConstraint("CK_ImportBatches_Hash", "LEN([FileSha256]) = 64 AND [FileSha256] NOT LIKE '%[^0-9a-f]%' COLLATE Latin1_General_BIN2");
                    table.CheckConstraint("CK_ImportBatches_Kind", "[SourceKind] IN ('Patients', 'Appointments', 'OpeningBalances')");
                    table.CheckConstraint("CK_ImportBatches_Label", "LEN(LTRIM(RTRIM([SourceLabel]))) > 0");
                    table.CheckConstraint("CK_ImportBatches_Policy", "[Policy] IN ('AllOrNothing', 'CommitValidRows')");
                    table.CheckConstraint("CK_ImportBatches_Sizes", "[FileSizeBytes] > 0 AND [RowCount] > 0 AND [ColumnCount] > 0");
                    table.CheckConstraint("CK_ImportBatches_Status", "[Status] IN ('Uploaded', 'DryRunComplete', 'Committing', 'Committed', 'Failed', 'Cancelled')");
                    table.CheckConstraint("CK_ImportBatches_Times", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND ([UpdatedAtUtc] IS NULL OR ([UpdatedAtUtc] >= [CreatedAtUtc] AND [UpdatedAtUtc] < '2200-01-01'))");
                    table.ForeignKey(
                        name: "FK_ImportBatches_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CreatedAtUtc",
                table: "ImportBatches",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_CreatedByUserId",
                table: "ImportBatches",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatches_OpenFile",
                table: "ImportBatches",
                columns: new[] { "SourceKind", "FileSha256" },
                unique: true,
                filter: "[Status] IN ('Uploaded', 'DryRunComplete', 'Committing')");

            // ALV-018-C02 (errors 51260 to 51262): a batch is recorded once and never deleted; what it recorded about its file and its maker never changes; a finished batch (Committed, Failed or Cancelled) never changes.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ImportBatches_Guard] ON [ImportBatches] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51260, 'An import batch is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] IN ('Committed', 'Failed', 'Cancelled'))
        THROW 51262, 'A finished import batch never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[SourceKind] <> d.[SourceKind] OR i.[SourceLabel] <> d.[SourceLabel] OR i.[FileBlobId] <> d.[FileBlobId] OR i.[FileSha256] <> d.[FileSha256]
                  OR i.[FileSizeBytes] <> d.[FileSizeBytes] OR i.[RowCount] <> d.[RowCount] OR i.[ColumnCount] <> d.[ColumnCount] OR i.[ColumnNamesJson] <> d.[ColumnNamesJson]
                  OR i.[CreatedByUserId] <> d.[CreatedByUserId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc])
        THROW 51261, 'An import batch keeps what it recorded about its file and who brought it.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no import batch has been recorded
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ImportBatches])
    THROW 51263, 'Import batches exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "ImportBatches");
        }
    }
}
