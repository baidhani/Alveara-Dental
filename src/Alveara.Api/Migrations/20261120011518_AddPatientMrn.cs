using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N025 slice 3 (PD-21; brief sections 5 and 6, first of the three forward migrations; MRN rollback addendum approved in ADDENDUM-R01): the patient's
    /// Medical Record Number, with a durable archive so a rollback never loses or frees a number.
    ///
    /// Up:
    /// 1. creates <c>PatientMrnArchive</c> (PatientId PK -> Patients, unique Mrn) and its guard 51302 (no update, no delete) when they are missing - a rollback leaves them;
    /// 2. adds <c>Patients.Mrn</c> (nullable for now);
    /// 3. RESTORES every archived number to its patient first;
    /// 4. numbers the rest, ordered by (<c>CreatedAtUtc</c>, <c>Id</c>): each the UTC seconds of its <c>CreatedAtUtc</c>, moved forward past the previous one and past
    ///    every number already taken by a patient or the archive (with an empty archive this is exactly the approved backfill: max(own seconds, previous + 1));
    /// 5. NOT NULL, the unique index <c>IX_Patients_Mrn</c>, guard 51298 (an MRN never changes) and <c>TR_Patients_ArchiveMrn</c> (each new patient's number is
    ///    archived in the inserting statement's own transaction);
    /// 6. archives the numbers of step 4, so the archive holds every number the database has ever given.
    /// Steps 3-4 switch the patient triggers off for those statements only (TR_Patients_Merged refuses any change to a merged row, 51233) and on again right after.
    ///
    /// 7. adds <c>UPDATE([Mrn])</c> to the patient-merge guard TR_Patients_MergeScope (51234: during a merge only patient ids may change), by a text edit of the
    ///    CURRENT trigger - never a re-create from a template, which is how U-015 lost a guard - so every other rule of that trigger stays as it is.
    /// Down (one transaction): refuses with 51302 when an archive row disagrees with the patients (another number for the patient, or the number on another patient);
    /// archives every (PatientId, Mrn) not yet archived; then removes both triggers, the index and the column. The archive table, its keys and its guard are KEPT:
    /// after Down the schema is deliberately not byte-for-byte the pre-migration one (disclosed, addendum 3.5). 51300 is no longer used here.
    /// </summary>
    public partial class AddPatientMrn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF OBJECT_ID(N'[PatientMrnArchive]', N'U') IS NULL
BEGIN
    CREATE TABLE [PatientMrnArchive] (
        [PatientId] uniqueidentifier NOT NULL CONSTRAINT [PK_PatientMrnArchive] PRIMARY KEY,
        [Mrn] bigint NOT NULL,
        [ArchivedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [UX_PatientMrnArchive_Mrn] UNIQUE ([Mrn]),
        CONSTRAINT [FK_PatientMrnArchive_Patients] FOREIGN KEY ([PatientId]) REFERENCES [Patients] ([Id]));
END
IF OBJECT_ID(N'[TR_PatientMrnArchive_Preserve]', N'TR') IS NULL
    EXEC (N'CREATE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive] INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51302, ''An archived patient record number (MRN) is never changed or removed.'', 1;
END');");

            migrationBuilder.AddColumn<long>(name: "Mrn", table: "Patients", type: "bigint", nullable: true);

            migrationBuilder.Sql(@"DISABLE TRIGGER ALL ON [Patients];
UPDATE p SET p.[Mrn] = a.[Mrn] FROM [Patients] p JOIN [PatientMrnArchive] a ON a.[PatientId] = p.[Id];
DECLARE @id uniqueidentifier, @secs bigint, @prev bigint = NULL, @next bigint;
DECLARE fresh CURSOR LOCAL FAST_FORWARD FOR
    SELECT [Id], DATEDIFF_BIG(SECOND, CAST('0001-01-01' AS datetime2), CAST(SWITCHOFFSET([CreatedAtUtc], '+00:00') AS datetime2))
    FROM [Patients] WHERE [Mrn] IS NULL ORDER BY [CreatedAtUtc], [Id];
OPEN fresh;
FETCH NEXT FROM fresh INTO @id, @secs;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @next = CASE WHEN @prev IS NULL OR @secs > @prev THEN @secs ELSE @prev + 1 END;
    WHILE EXISTS (SELECT 1 FROM [PatientMrnArchive] WHERE [Mrn] = @next) OR EXISTS (SELECT 1 FROM [Patients] WHERE [Mrn] = @next)
        SET @next = @next + 1;
    UPDATE [Patients] SET [Mrn] = @next WHERE [Id] = @id;
    SET @prev = @next;
    FETCH NEXT FROM fresh INTO @id, @secs;
END
CLOSE fresh;
DEALLOCATE fresh;
ENABLE TRIGGER ALL ON [Patients];");

            migrationBuilder.AlterColumn<long>(name: "Mrn", table: "Patients", type: "bigint", nullable: false, oldClrType: typeof(long), oldType: "bigint", oldNullable: true);

            migrationBuilder.CreateIndex(name: "IX_Patients_Mrn", table: "Patients", column: "Mrn", unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Patients_MrnImmutable] ON [Patients] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF UPDATE([Mrn]) AND EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[Mrn] <> d.[Mrn])
        THROW 51298, 'A patient''s record number (MRN) never changes.', 1;
END");

            // owner decision 2026-11-19: 51298 is absolute and answers first - also inside a merge session, where the merge-scope term UPDATE([Mrn]) would refuse too
            migrationBuilder.Sql("EXEC sp_settriggerorder @triggername = N'TR_Patients_MrnImmutable', @order = N'First', @stmttype = N'UPDATE';");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Patients_ArchiveMrn] ON [Patients] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO [PatientMrnArchive] ([PatientId], [Mrn], [ArchivedAtUtc])
    SELECT i.[Id], i.[Mrn], SYSDATETIMEOFFSET() FROM inserted i
    WHERE NOT EXISTS (SELECT 1 FROM [PatientMrnArchive] a WHERE a.[PatientId] = i.[Id]);
END");

            migrationBuilder.Sql(@"INSERT INTO [PatientMrnArchive] ([PatientId], [Mrn], [ArchivedAtUtc])
SELECT p.[Id], p.[Mrn], SYSDATETIMEOFFSET() FROM [Patients] p
WHERE NOT EXISTS (SELECT 1 FROM [PatientMrnArchive] a WHERE a.[PatientId] = p.[Id]);");

            migrationBuilder.Sql(@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[TR_Patients_MergeScope]'));
IF @def IS NULL THROW 50000, 'TR_Patients_MergeScope is missing; the MRN cannot be added to it.', 1;
IF CHARINDEX(N'UPDATE([Mrn])', @def) = 0
BEGIN
    IF CHARINDEX(N'UPDATE([', @def) = 0 OR LEFT(@def, 14) <> N'CREATE TRIGGER'
        THROW 50000, 'TR_Patients_MergeScope has a shape this migration does not know how to extend.', 1;
    SET @def = STUFF(@def, CHARINDEX(N'UPDATE([', @def), 0, N'UPDATE([Mrn]) OR ');
    SET @def = STUFF(@def, 1, 6, N'ALTER');
    EXEC (@def);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Patients] p JOIN [PatientMrnArchive] a ON a.[PatientId] = p.[Id] WHERE a.[Mrn] <> p.[Mrn])
   OR EXISTS (SELECT 1 FROM [Patients] p JOIN [PatientMrnArchive] a ON a.[Mrn] = p.[Mrn] WHERE a.[PatientId] <> p.[Id])
    THROW 51302, 'The archived record numbers (MRNs) do not match the patients; rolling back would overwrite one, so it is refused.', 1;
INSERT INTO [PatientMrnArchive] ([PatientId], [Mrn], [ArchivedAtUtc])
SELECT p.[Id], p.[Mrn], SYSDATETIMEOFFSET() FROM [Patients] p
WHERE NOT EXISTS (SELECT 1 FROM [PatientMrnArchive] a WHERE a.[PatientId] = p.[Id]);");

            migrationBuilder.Sql(@"DECLARE @def nvarchar(max) = (SELECT m.[definition] FROM sys.sql_modules m WHERE m.[object_id] = OBJECT_ID(N'[TR_Patients_MergeScope]'));
IF @def IS NOT NULL AND CHARINDEX(N'UPDATE([Mrn]) OR ', @def) > 0
BEGIN
    SET @def = STUFF(REPLACE(@def, N'UPDATE([Mrn]) OR ', N''), 1, 6, N'ALTER');
    EXEC (@def);
END");

            migrationBuilder.Sql("DROP TRIGGER [TR_Patients_ArchiveMrn];");

            migrationBuilder.Sql("DROP TRIGGER [TR_Patients_MrnImmutable];");

            migrationBuilder.DropIndex(name: "IX_Patients_Mrn", table: "Patients");

            migrationBuilder.DropColumn(name: "Mrn", table: "Patients");
        }
    }
}
