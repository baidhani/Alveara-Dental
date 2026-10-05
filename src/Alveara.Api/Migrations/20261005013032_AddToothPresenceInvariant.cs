using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddToothPresenceInvariant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ALV-006-C01 R02: the database enforces, for every writer and at every moment, that a tooth is never BOTH absent and carrying a finding that says nothing about its presence:
            // no active finding whose condition has the Absent effect in state Existing or Completed beside an active finding whose condition has no effect on presence.
            // The trigger looks only at the rows the statement actually changed (a new row, or a changed state, status or condition), so a withdrawal - which can only make a tooth more
            // consistent - is never refused, and rows written before this migration are not re-judged. For each tooth it touches it first takes an application lock for the transaction,
            // named for the patient and the tooth: two writers on the same tooth queue, and the second sees the first's committed row. A lock not granted within 10 seconds is refused as 51063.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ToothFindings_ToothPresence] ON [ToothFindings] AFTER INSERT, UPDATE AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @changed TABLE ([Id] uniqueidentifier NOT NULL, [PatientId] uniqueidentifier NOT NULL, [ToothKey] nchar(2) NOT NULL, [Condition] nvarchar(32) COLLATE Latin1_General_CS_AS NOT NULL, [State] nvarchar(10) NOT NULL);
    INSERT INTO @changed ([Id], [PatientId], [ToothKey], [Condition], [State])
        SELECT i.[Id], i.[PatientId], i.[ToothKey], i.[Condition], i.[State] FROM inserted i
        WHERE i.[Status] = 'Active'
          AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[State] = i.[State] AND d.[Status] = i.[Status] AND d.[Condition] = i.[Condition]);
    IF NOT EXISTS (SELECT 1 FROM @changed) RETURN;

    DECLARE @patient uniqueidentifier, @tooth nchar(2), @resource nvarchar(255), @lock int;
    DECLARE teeth CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT [PatientId], [ToothKey] FROM @changed ORDER BY [PatientId], [ToothKey];
    OPEN teeth;
    FETCH NEXT FROM teeth INTO @patient, @tooth;
    WHILE @@FETCH_STATUS = 0
    BEGIN
        SET @resource = N'ToothPresence:' + CONVERT(nvarchar(36), @patient) + N':' + @tooth;
        EXEC @lock = sp_getapplock @Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
        IF @lock < 0 THROW 51063, 'Another change to this tooth is in progress; try again.', 1;

        -- (a) a finding that says nothing about presence was written on a tooth that is absent
        IF EXISTS (SELECT 1 FROM @changed c JOIN [ConditionTypes] ct ON ct.[Code] = c.[Condition]
                   WHERE c.[PatientId] = @patient AND c.[ToothKey] = @tooth AND ct.[ToothEffect] = 'None'
                     AND EXISTS (SELECT 1 FROM [ToothFindings] a JOIN [ConditionTypes] ca ON ca.[Code] = a.[Condition]
                                 WHERE a.[PatientId] = c.[PatientId] AND a.[ToothKey] = c.[ToothKey] AND a.[Status] = 'Active' AND a.[Id] <> c.[Id]
                                   AND ca.[ToothEffect] = 'Absent' AND a.[State] IN ('Existing', 'Completed')))
            THROW 51061, 'This tooth is recorded as missing, so no other finding can be recorded on it.', 1;

        -- (b) a finding made the tooth absent while other active findings that say nothing about presence stand on it
        IF EXISTS (SELECT 1 FROM @changed c JOIN [ConditionTypes] ct ON ct.[Code] = c.[Condition]
                   WHERE c.[PatientId] = @patient AND c.[ToothKey] = @tooth AND ct.[ToothEffect] = 'Absent' AND c.[State] IN ('Existing', 'Completed')
                     AND EXISTS (SELECT 1 FROM [ToothFindings] o JOIN [ConditionTypes] co ON co.[Code] = o.[Condition]
                                 WHERE o.[PatientId] = c.[PatientId] AND o.[ToothKey] = c.[ToothKey] AND o.[Status] = 'Active' AND o.[Id] <> c.[Id] AND co.[ToothEffect] = 'None'))
            THROW 51062, 'This tooth has other active findings, so it cannot be recorded as missing yet.', 1;

        FETCH NEXT FROM teeth INTO @patient, @tooth;
    END
    CLOSE teeth;
    DEALLOCATE teeth;
END");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_ToothFindings_ToothPresence]");

        }
    }
}
