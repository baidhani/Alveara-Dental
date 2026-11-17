using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N022 A2-R02 (review A2-R01 CR-1; OD-06 as amended 2026-11-17): the database refuses a NEW diagnosis linked to a Finalized encounter, so a finalize that commits
    /// between the service's check and the insert cannot let one through.
    ///
    /// - INSERT only: a correction or withdrawal of an already-recorded diagnosis is an UPDATE and is not affected; existing rows are not touched or re-checked.
    /// - The encounter row is read WITH (UPDLOCK, HOLDLOCK), a locking read of the latest committed version held to the end of the insert's transaction: a finalize that
    ///   is in flight makes the insert wait and then refuse; a finalize that comes second waits until the diagnosis is committed (recorded while the encounter was a draft).
    /// - Completed-procedure attribution to a Finalized encounter is NOT guarded (ALV-008-C01 D6). Signed drafts are not Finalized and are unaffected (OD-13).
    /// - The same-patient and immutable-link protections stay in TR_Diagnoses_Links (51073/51074), unchanged.
    /// - 51287 is mapped by DiagnosisService to 409 encounter_finalized; no SQL text reaches the caller.
    /// No table, column or data change, so Down simply removes the trigger.
    /// </summary>
    public partial class AddDiagnosisFinalizedEncounterGuard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Diagnoses_EncounterNotFinalized] ON [Diagnoses] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e WITH (UPDLOCK, HOLDLOCK) ON e.[Id] = i.[EncounterId] WHERE e.[Status] = N'Finalized')
        THROW 51287, 'A finalized encounter accepts no new diagnosis. Record it in a new draft encounter.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_Diagnoses_EncounterNotFinalized]");
        }
    }
}
