using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N021 A2, migration M6 (owner decision 2026-11-16; an authorized supporting migration inside A2): repairs a gap left by M1. The patient-merge guard
    /// TR_Appointments_MergeScope (STORY-017, 20261025052956_AddPatientMerges) lists every non-patient column of Appointments so that, inside a merge, only PatientId
    /// may change. M1 added Appointments.CancelCategory without re-creating the trigger, so a merge-time change of the cancel category was not refused (51234).
    /// Up re-creates the trigger with exactly the same text the merge migration generates, from the table's current columns (CancelCategory included). Down
    /// re-creates it from the current columns minus CancelCategory - the pre-M6 trigger - so M1's Down can still drop the column. No data is read or written,
    /// so there is nothing for a rollback to discard and no refusal guard is needed.
    /// </summary>
    public partial class RefreshAppointmentMergeScope : Migration
    {
        private static string Recreate(string excludedColumn) => $@"DECLARE @cols nvarchar(max), @sql nvarchar(max);
SELECT @cols = STRING_AGG(CAST(N'UPDATE(' + QUOTENAME(c.name) + N')' AS nvarchar(max)), N' OR ') WITHIN GROUP (ORDER BY c.column_id)
  FROM sys.columns c WHERE c.object_id = OBJECT_ID(N'[Appointments]') AND c.is_computed = 0 AND c.system_type_id <> 189 AND c.name NOT IN (N'PatientId', N'{excludedColumn}');
IF OBJECT_ID(N'[TR_Appointments_MergeScope]', N'TR') IS NOT NULL DROP TRIGGER [TR_Appointments_MergeScope];
SET @sql = N'CREATE TRIGGER [TR_Appointments_MergeScope] ON [Appointments] AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')) = 1
       AND ((EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)) OR (NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR ' + @cols + N')
        THROW 51234, ''During a patient merge only the patient id of an existing row may change.'', 1;
END';
EXEC (@sql);";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Recreate(excludedColumn: "PatientId"));   // nothing extra excluded: every column, CancelCategory too

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Recreate(excludedColumn: "CancelCategory"));
    }
}
