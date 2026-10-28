using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DryRunAtUtc",
                table: "ImportBatches",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MappingJson",
                table: "ImportBatches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImportBatchRows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ErrorsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    MatchedPatientIdsJson = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    DuplicateOfRow = table.Column<int>(type: "int", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatchRows", x => x.Id);
                    table.CheckConstraint("CK_ImportBatchRows_Number", "[RowNumber] >= 2");
                    table.CheckConstraint("CK_ImportBatchRows_Repeat", "[DuplicateOfRow] IS NULL OR ([DuplicateOfRow] >= 2 AND [DuplicateOfRow] < [RowNumber])");
                    table.CheckConstraint("CK_ImportBatchRows_Status", "[Status] IN ('Valid', 'Rejected', 'Unresolved', 'Skipped', 'Accepted')");
                    table.ForeignKey(
                        name: "FK_ImportBatchRows_ImportBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatchRows_BatchId_Status",
                table: "ImportBatchRows",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_ImportBatchRows_Row",
                table: "ImportBatchRows",
                columns: new[] { "BatchId", "RowNumber" },
                unique: true);

            // ALV-018-C02 (errors 51264 to 51266): a row result is never deleted; its batch and number never change; a row of a finished batch (Committed, Failed or Cancelled) never changes.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ImportBatchRows_Guard] ON [ImportBatchRows] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51264, 'An import batch row is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[BatchId] <> d.[BatchId] OR i.[RowNumber] <> d.[RowNumber])
        THROW 51265, 'An import batch row keeps its batch and its row number.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ImportBatches] b ON b.[Id] = i.[BatchId] WHERE b.[Status] IN ('Committed', 'Failed', 'Cancelled'))
        THROW 51266, 'The rows of a finished import batch never change.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no row result has been recorded
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ImportBatchRows])
    THROW 51267, 'Import batch rows exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "ImportBatchRows");

            migrationBuilder.DropColumn(
                name: "DryRunAtUtc",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "MappingJson",
                table: "ImportBatches");
        }
    }
}
