using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N025, the fourth forward migration (merge-integrity addendum, approved in MERGE-ADDENDUM-R02): repairs merge-guard regressions of two closed stories.
    /// - U-016: five columns were added without being listed in their table's patient-merge guard (51234: during a merge only patient ids may change):
    ///   Clearances.DocumentId, ClearanceVersions.DocumentId, PerioExamLinks.DiagnosisId (ALV-N022 A3); FinancialDocuments.ReplacementReason and
    ///   FinancialDocuments.ReplacesDocumentId (ALV-N024). Up adds the missing <c>UPDATE([column]) OR </c> term(s) to each trigger.
    /// - U-017: TR_Clearances_DocumentSamePatient (ALV-N022 A3, 51288) did not stand aside during a merge, which moves Clearances before Documents, so a patient with a
    ///   document-linked clearance could not be merged. Up adds the repository's standard stand-aside line after <c>SET NOCOUNT ON;</c>: an UPDATE inside a merge is let
    ///   through (an INSERT and any change outside a merge are still checked), and the merge's end-of-merge edge check (VerifyAsync) still refuses any clearance left
    ///   pointing at another patient's document.
    /// Every change is a TEXT EDIT of the trigger as it is now - never a re-create from a template (how U-015 lost a guard) - skipped when already present, refused when
    /// the trigger is missing or has an unknown shape. Down removes exactly the inserted text. No data is read or written.
    /// </summary>
    public partial class AddMergeTriggerIntegrityRepairs : Migration
    {
        private static readonly (string Trigger, string Column)[] MissingTerms =
        [
            ("TR_Clearances_MergeScope", "DocumentId"),
            ("TR_ClearanceVersions_MergeScope", "DocumentId"),
            ("TR_PerioExamLinks_MergeScope", "DiagnosisId"),
            ("TR_FinancialDocuments_MergeScope", "ReplacementReason"),
            ("TR_FinancialDocuments_MergeScope", "ReplacesDocumentId"),
        ];

        private const string StandAsideTrigger = "TR_Clearances_DocumentSamePatient";

        /// <summary>The standard stand-aside line (as AddPatientMerges gives every immutable-record trigger), inserted on its own line after SET NOCOUNT ON;.</summary>
        internal const string StandAsideLine =
            "IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND ((EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR NOT (EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted))) RETURN;";

        internal static string Term(string column) => $"UPDATE([{column}]) OR ";

        private static string Sql(string text) => text.Replace("'", "''");

        private static string AddTerm(string trigger, string column) => $@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[{trigger}]'));
IF @def IS NULL THROW 50000, '{trigger} is missing; the merge guard cannot be repaired.', 1;
IF CHARINDEX(N'UPDATE([{column}])', @def) = 0
BEGIN
    IF CHARINDEX(N'UPDATE([', @def) = 0 OR NOT (LEFT(@def, 14) = N'CREATE TRIGGER' OR LEFT(@def, 13) = N'ALTER TRIGGER')
        THROW 50000, '{trigger} has a shape this migration does not know how to extend.', 1;
    SET @def = STUFF(@def, CHARINDEX(N'UPDATE([', @def), 0, N'{Term(column)}');
    IF LEFT(@def, 6) = N'CREATE' SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END";

        private static string RemoveTerm(string trigger, string column) => $@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[{trigger}]'));
IF @def IS NOT NULL AND CHARINDEX(N'{Term(column)}', @def) > 0
BEGIN
    SET @def = REPLACE(@def, N'{Term(column)}', N''); IF LEFT(@def, 6) = N'CREATE' SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (trigger, column) in MissingTerms) migrationBuilder.Sql(AddTerm(trigger, column));

            migrationBuilder.Sql($@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[{StandAsideTrigger}]'));
DECLARE @line nvarchar(max) = NCHAR(10) + N'    ' + N'{Sql(StandAsideLine)}';
IF @def IS NULL THROW 50000, '{StandAsideTrigger} is missing; it cannot be repaired.', 1;
IF CHARINDEX(N'alveara.patient_merge', @def) = 0
BEGIN
    IF CHARINDEX(N'SET NOCOUNT ON;', @def) = 0 OR NOT (LEFT(@def, 14) = N'CREATE TRIGGER' OR LEFT(@def, 13) = N'ALTER TRIGGER')
        THROW 50000, '{StandAsideTrigger} has a shape this migration does not know how to extend.', 1;
    SET @def = STUFF(@def, CHARINDEX(N'SET NOCOUNT ON;', @def) + LEN(N'SET NOCOUNT ON;'), 0, @line);
    IF LEFT(@def, 6) = N'CREATE' SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[{StandAsideTrigger}]'));
DECLARE @line nvarchar(max) = NCHAR(10) + N'    ' + N'{Sql(StandAsideLine)}';
IF @def IS NOT NULL AND CHARINDEX(@line, @def) > 0
BEGIN
    SET @def = REPLACE(@def, @line, N''); IF LEFT(@def, 6) = N'CREATE' SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END");

            foreach (var (trigger, column) in MissingTerms) migrationBuilder.Sql(RemoveTerm(trigger, column));
        }
    }
}
