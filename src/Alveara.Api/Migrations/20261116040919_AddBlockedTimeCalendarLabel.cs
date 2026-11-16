using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N021 A2, migration M5 (owner decision 2026-11-15, F-052; an authorized supporting migration inside A2): additive only. A blocked time gets a calendar label from a controlled
    /// list (lunch, meeting, training, unavailable, other; case-sensitive), which is what the calendar shows instead of the private reason. Existing blocks keep a null label and are shown
    /// through the read-time rule (a recognised lunch reason as Lunch, anything else as "Blocked time"); no row is rewritten. Down refuses (51286) once any label has been recorded,
    /// because a configured calendar label is never discarded to make a rollback succeed; otherwise it drops exactly what Up added.
    /// </summary>
    public partial class AddBlockedTimeCalendarLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalendarLabel",
                table: "ProviderBlockedTimes",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProviderBlockedTimes_CalendarLabel",
                table: "ProviderBlockedTimes",
                sql: "[CalendarLabel] IS NULL OR [CalendarLabel] COLLATE Latin1_General_CS_AS IN ('lunch','meeting','training','unavailable','other')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ProviderBlockedTimes] WHERE [CalendarLabel] IS NOT NULL)
    THROW 51286, 'Blocked-time calendar labels have been recorded, so this migration cannot be rolled back.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProviderBlockedTimes_CalendarLabel",
                table: "ProviderBlockedTimes");

            migrationBuilder.DropColumn(
                name: "CalendarLabel",
                table: "ProviderBlockedTimes");
        }
    }
}
