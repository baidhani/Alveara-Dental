using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N025 (U-015, owner decision 2026-11-19): restores the merged-patient guard (51235) on Appointments. ALV-N021 A2's migration M6
    /// (20261116145816_RefreshAppointmentMergeScope) re-created TR_Appointments_MergeScope from the STORY-017 text and so dropped the block that ALV-017-C01
    /// (20261025200612_GuardRetiredPatientRecords) had added: since M6 the database no longer refused a new appointment for a patient whose record was merged away.
    /// M6 is history and is not rewritten. Up puts the same guard - the same text between the same markers - back into TR_Appointments_MergeScope only, after the
    /// trigger's BEGIN, keeping every other rule of the current trigger (the 51234 merge-scope check over every column, CancelCategory included). It does nothing
    /// if the guard is already there. Down takes out exactly that block (the M6 state). No data is read or written.
    /// </summary>
    public partial class RestoreAppointmentRetiredRecordGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DECLARE @def nvarchar(max), @guard nvarchar(max), @pos int;
SELECT @def = m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id WHERE t.name = N'TR_Appointments_MergeScope';
IF @def IS NULL THROW 51235, 'TR_Appointments_MergeScope is missing; the migration cannot restore its guard.', 1;
IF @def NOT LIKE N'%/*retired-record-guard*/%'
BEGIN
    SET @guard = NCHAR(10) + N'/*retired-record-guard*/ IF ISNULL(CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')), 0) <> 1 AND EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT 1 FROM inserted i JOIN [Patients] p ON p.[Id] = i.[PatientId] WHERE p.[MergedAtUtc] IS NOT NULL)
        THROW 51235, ''A patient record that was merged into another takes no new records.'', 1; /*end retired-record-guard*/';
    SET @pos = CHARINDEX(N'BEGIN', @def);
    IF @pos = 0 OR LEFT(@def, 14) <> N'CREATE TRIGGER ' THROW 51235, 'TR_Appointments_MergeScope has a shape the migration does not know how to extend.', 1;
    SET @def = STUFF(STUFF(@def, @pos + 5, 0, @guard), 1, 6, N'ALTER');
    EXEC (@def);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DECLARE @def nvarchar(max), @a int, @b int;
SELECT @def = m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id WHERE t.name = N'TR_Appointments_MergeScope';
IF @def LIKE N'%/*retired-record-guard*/%'
BEGIN
    SET @a = CHARINDEX(N'/*retired-record-guard*/', @def);
    SET @b = CHARINDEX(N'/*end retired-record-guard*/', @def) + LEN(N'/*end retired-record-guard*/');
    SET @def = STUFF(STUFF(@def, @a - 1, @b - @a + 1, N''), 1, 6, N'ALTER');
    EXEC (@def);
END");
        }
    }
}
