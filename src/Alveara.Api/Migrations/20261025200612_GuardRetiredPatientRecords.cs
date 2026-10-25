using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-017-C01 (error 51235): a patient record that was merged into another takes no new records. No table changes: the merge-scope trigger of every table that holds a patient id gets one more check, run outside a merge
    /// (the session value alveara.patient_merge is not 1): a new row naming a patient whose record was merged away is refused. The check sits between the markers /*retired-record-guard*/ and /*end retired-record-guard*/ so
    /// Down takes out exactly what Up put in. A table that only refers to a patient without owning a PatientId column (the Patients table, messages and notifications) is left as it is.
    /// </summary>
    public partial class GuardRetiredPatientRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DECLARE @name sysname, @def nvarchar(max), @guard nvarchar(max), @pos int;
SET @guard = NCHAR(10) + N'/*retired-record-guard*/ IF ISNULL(CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')), 0) <> 1 AND EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT 1 FROM inserted i JOIN [Patients] p ON p.[Id] = i.[PatientId] WHERE p.[MergedAtUtc] IS NOT NULL)
        THROW 51235, ''A patient record that was merged into another takes no new records.'', 1; /*end retired-record-guard*/';
DECLARE guard CURSOR LOCAL STATIC READ_ONLY FOR
    SELECT t.name, m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id
    WHERE t.parent_class = 1 AND t.name LIKE N'TR[_]%[_]MergeScope' AND EXISTS (SELECT 1 FROM sys.columns c WHERE c.object_id = t.parent_id AND c.name = N'PatientId') AND m.definition NOT LIKE N'%/*retired-record-guard*/%';
OPEN guard; FETCH NEXT FROM guard INTO @name, @def;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @pos = CHARINDEX(N'BEGIN', @def);
    IF @pos = 0 OR LEFT(@def, 14) <> N'CREATE TRIGGER ' THROW 51235, 'A merge-scope trigger has a shape the migration does not know how to extend.', 1;
    SET @def = STUFF(STUFF(@def, @pos + 5, 0, @guard), 1, 6, N'ALTER');
    EXEC (@def);
    FETCH NEXT FROM guard INTO @name, @def;
END
CLOSE guard; DEALLOCATE guard;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DECLARE @name sysname, @def nvarchar(max), @a int, @b int;
DECLARE unguard CURSOR LOCAL STATIC READ_ONLY FOR
    SELECT t.name, m.definition FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id
    WHERE t.parent_class = 1 AND t.name LIKE N'TR[_]%[_]MergeScope' AND m.definition LIKE N'%/*retired-record-guard*/%';
OPEN unguard; FETCH NEXT FROM unguard INTO @name, @def;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @a = CHARINDEX(N'/*retired-record-guard*/', @def);
    SET @b = CHARINDEX(N'/*end retired-record-guard*/', @def) + LEN(N'/*end retired-record-guard*/');
    SET @def = STUFF(STUFF(@def, @a - 1, @b - @a + 1, N''), 1, 6, N'ALTER');
    EXEC (@def);
    FETCH NEXT FROM unguard INTO @name, @def;
END
CLOSE unguard; DEALLOCATE unguard;");
        }
    }
}
