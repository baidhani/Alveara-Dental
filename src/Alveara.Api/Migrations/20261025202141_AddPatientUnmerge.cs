using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientUnmerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PatientMerges_SourcePatientId",
                table: "PatientMerges");

            migrationBuilder.AddColumn<bool>(
                name: "HasLedger",
                table: "PatientMerges",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PatientMergeMovedRows",
                columns: table => new
                {
                    MergeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ColumnName = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RowKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientMergeMovedRows", x => new { x.MergeId, x.TableName, x.ColumnName, x.RowKey });
                    table.CheckConstraint("CK_PatientMergeMovedRows_Names", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([TableName], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND LEN(REPLACE(REPLACE(REPLACE(REPLACE([ColumnName], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND LEN(REPLACE(REPLACE(REPLACE(REPLACE([RowKey], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0");
                    table.ForeignKey(
                        name: "FK_PatientMergeMovedRows_PatientMerges_MergeId",
                        column: x => x.MergeId,
                        principalTable: "PatientMerges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientUnmerges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnmergeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UnmergedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UnmergedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RowsMovedBack = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientUnmerges", x => x.Id);
                    table.CheckConstraint("CK_PatientUnmerges_Key", "LEN(LTRIM(RTRIM([UnmergeKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [UnmergeKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PatientUnmerges_Reason", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Reason], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Reason] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PatientUnmerges_Rows", "[RowsMovedBack] >= 0");
                    table.CheckConstraint("CK_PatientUnmerges_Time", "[UnmergedAtUtc] >= '2000-01-01' AND [UnmergedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_PatientUnmerges_PatientMerges_MergeId",
                        column: x => x.MergeId,
                        principalTable: "PatientMerges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientUnmerges_UserAccounts_UnmergedByUserId",
                        column: x => x.UnmergedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientMerges_SourcePatientId",
                table: "PatientMerges",
                column: "SourcePatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientUnmerges_MergeId",
                table: "PatientUnmerges",
                column: "MergeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientUnmerges_UnmergedByUserId",
                table: "PatientUnmerges",
                column: "UnmergedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientUnmerges_UnmergeKey",
                table: "PatientUnmerges",
                column: "UnmergeKey",
                unique: true);

            // ALV-017-C01 (errors 51236 to 51238, 51244): the ledger and the undo records are append-only; an undo is recorded only for a merge that still holds; a record is merged away once at a time (again only after its
            // merge was undone); and a merged record can come back only through an undone merge.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientMergeMovedRows_Immutable] ON [PatientMergeMovedRows] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51236, 'The ledger of a merge is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientUnmerges_Immutable] ON [PatientUnmerges] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51237, 'The record of an undone merge is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientUnmerges_Active] ON [PatientUnmerges] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [PatientMerges] m ON m.[Id] = i.[MergeId] JOIN [Patients] p ON p.[Id] = m.[SourcePatientId] WHERE p.[MergedAtUtc] IS NULL OR p.[MergedAtUtc] <> m.[MergedAtUtc])
        THROW 51238, 'Only a merge that still holds can be undone.', 1;
END");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_PatientMerges_Survivor] ON [PatientMerges] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [PatientMerges] m ON m.[SourcePatientId] = i.[SurvivorPatientId] WHERE NOT EXISTS (SELECT 1 FROM [PatientUnmerges] u WHERE u.[MergeId] = m.[Id]))
        THROW 51231, 'A patient that was merged into another record cannot be the survivor of a merge.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [PatientMerges] m ON m.[SourcePatientId] = i.[SourcePatientId] AND m.[Id] <> i.[Id] WHERE NOT EXISTS (SELECT 1 FROM [PatientUnmerges] u WHERE u.[MergeId] = m.[Id]))
        THROW 51244, 'A patient record is merged away once at a time: undo the earlier merge first.', 1;
END");
            migrationBuilder.Sql(@"DECLARE @cols nvarchar(max), @sql nvarchar(max);
SELECT @cols = STRING_AGG(CAST(N'UPDATE(' + QUOTENAME(c.name) + N')' AS nvarchar(max)), N' OR ') WITHIN GROUP (ORDER BY c.column_id)
  FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'[Patients]') AND c.is_computed = 0 AND c.system_type_id <> 189
   AND c.name NOT IN (N'IsActive', N'MergedAtUtc', N'UpdatedAtUtc', N'UpdatedByUserId');
SET @sql = N'ALTER TRIGGER [TR_Patients_Merged] ON [Patients] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[MergedAtUtc] IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [PatientMerges] m WHERE m.[SourcePatientId] = i.[Id] AND m.[MergedAtUtc] = i.[MergedAtUtc]))
        THROW 51232, ''A patient is marked merged only by a merge lineage with the same time.'', 1;
    IF CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')) = 1 AND ((EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR NOT (EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted))) RETURN;
    /* a merged record comes back only through an undone merge, only with the unmerge session value, and only its active flag, merged mark and who and when change */
    IF ISNULL(CONVERT(int, SESSION_CONTEXT(N''alveara.patient_unmerge'')), 0) = 1 AND EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)
       AND NOT (' + @cols + N')
       AND NOT EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
                       WHERE d.[MergedAtUtc] IS NOT NULL AND (i.[MergedAtUtc] IS NOT NULL OR i.[IsActive] = 0
                             OR NOT EXISTS (SELECT 1 FROM [PatientMerges] m JOIN [PatientUnmerges] u ON u.[MergeId] = m.[Id] WHERE m.[SourcePatientId] = i.[Id] AND m.[MergedAtUtc] = d.[MergedAtUtc])))
        RETURN;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.[MergedAtUtc] IS NOT NULL)
        THROW 51233, ''A patient record that was merged into another never changes.'', 1;
END';
EXEC (@sql);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no merge has a ledger and none was undone
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [PatientMergeMovedRows]) OR EXISTS (SELECT 1 FROM [PatientUnmerges])
    THROW 51245, 'Merge ledgers or undone merges exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_PatientMerges_Survivor] ON [PatientMerges] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [PatientMerges] m ON m.[SourcePatientId] = i.[SurvivorPatientId])
        THROW 51231, 'A patient that was merged into another record cannot be the survivor of a merge.', 1;
END");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_Patients_Merged] ON [Patients] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[MergedAtUtc] IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [PatientMerges] m WHERE m.[SourcePatientId] = i.[Id] AND m.[MergedAtUtc] = i.[MergedAtUtc]))
        THROW 51232, 'A patient is marked merged only by a merge lineage with the same time.', 1;
    IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND ((EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR NOT (EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted))) RETURN;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.[MergedAtUtc] IS NOT NULL)
        THROW 51233, 'A patient record that was merged into another never changes.', 1;
END");
            migrationBuilder.DropTable(
                name: "PatientMergeMovedRows");

            migrationBuilder.DropTable(
                name: "PatientUnmerges");

            migrationBuilder.DropIndex(
                name: "IX_PatientMerges_SourcePatientId",
                table: "PatientMerges");

            migrationBuilder.DropColumn(
                name: "HasLedger",
                table: "PatientMerges");

            migrationBuilder.CreateIndex(
                name: "IX_PatientMerges_SourcePatientId",
                table: "PatientMerges",
                column: "SourcePatientId",
                unique: true);
        }
    }
}
