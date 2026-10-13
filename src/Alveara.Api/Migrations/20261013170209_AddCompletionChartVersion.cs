using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletionChartVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ToothFindingVersionNumber",
                table: "CompletedProcedures",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompletedProcedures_ChartVersion",
                table: "CompletedProcedures",
                sql: "[ToothFindingVersionNumber] IS NULL OR ([ToothFindingId] IS NOT NULL AND [ToothFindingVersionNumber] >= 1)");

            // ALV-008-C02 R02 (review finding ALV-008-C02-R01-01): a completion records the exact finding version it wrote or observed. A completion recorded before this column existed is given, ONCE and here,
            // the first active Completed version of its finding at or before the instant it was finalized. A finding only ever moves forward into Completed, so that is the version the completion wrote (or the
            // earliest it could have observed), and a later change at the same instant cannot displace it. The timestamp is only an upper bound; it never chooses among versions. A completion for which no such
            // version exists stays null, and reconciliation reports it. The table's immutability trigger is switched off for this one statement only.
            migrationBuilder.Sql("DISABLE TRIGGER [TR_CompletedProcedures_Immutable] ON [CompletedProcedures]");
            migrationBuilder.Sql(@"UPDATE c SET c.[ToothFindingVersionNumber] =
    (SELECT MIN(v.[VersionNumber]) FROM [ToothFindingVersions] v
     WHERE v.[FindingId] = c.[ToothFindingId] AND v.[State] = 'Completed' AND v.[Status] = 'Active' AND v.[OccurredAtUtc] <= c.[FinalizedAtUtc])
FROM [CompletedProcedures] c
WHERE c.[ToothFindingId] IS NOT NULL");
            migrationBuilder.Sql("ENABLE TRIGGER [TR_CompletedProcedures_Immutable] ON [CompletedProcedures]");

            // error 51173 (51174 and up are free): what a new completed procedure that names a chart finding must also name. The evidence must exist for that finding, show it as an active Completed state and not
            // post-date the completion; the history versions are themselves immutable, so what they show now is what they showed then.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletedProcedures_ChartEvidence] ON [CompletedProcedures] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i
               WHERE i.[ToothFindingId] IS NOT NULL
                 AND (i.[ToothFindingVersionNumber] IS NULL
                      OR NOT EXISTS (SELECT 1 FROM [ToothFindingVersions] v
                                     WHERE v.[FindingId] = i.[ToothFindingId] AND v.[VersionNumber] = i.[ToothFindingVersionNumber]
                                       AND v.[State] = 'Completed' AND v.[Status] = 'Active' AND v.[OccurredAtUtc] <= i.[FinalizedAtUtc])))
        THROW 51173, 'A completed procedure that names a chart finding must name the finding version, written or observed when it was completed, that shows the finding Completed.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // the stored version is derived evidence (the first active Completed version at or before the completion), so rolling back and forward again restores it by the same rule; nothing else is lost
            migrationBuilder.Sql("DROP TRIGGER [TR_CompletedProcedures_ChartEvidence]");
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompletedProcedures_ChartVersion",
                table: "CompletedProcedures");

            migrationBuilder.DropColumn(
                name: "ToothFindingVersionNumber",
                table: "CompletedProcedures");
        }
    }
}
