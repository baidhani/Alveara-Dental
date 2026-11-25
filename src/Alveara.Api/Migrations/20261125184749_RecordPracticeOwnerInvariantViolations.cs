using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N030-C01 (brief 18.7, R08-3): the data step. Records - never repairs - an active Practice Owner who breaks the owner invariant, with one
    /// <c>PracticeOwnerInvariantViolation</c> audit row whose id is derived from the state, inserted only if absent (see
    /// <see cref="Alveara.Api.Architecture.Identity.OwnerInvariantAudit"/>, which holds the one shared statement). No schema change. Down does nothing: audit rows are
    /// never deleted.
    /// </summary>
    public partial class RecordPracticeOwnerInvariantViolations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Alveara.Api.Architecture.Identity.OwnerInvariantAudit.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // nothing to undo: the step changed no data but the append-only audit log
        }
    }
}
