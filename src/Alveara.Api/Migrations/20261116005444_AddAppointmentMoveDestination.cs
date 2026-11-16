using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N021 A2, migration M4 (owner decision 2026-11-15; an authorized supporting migration inside A2, beyond the briefing's M1-M3): additive only. A move's history event records where
    /// it took the appointment TO - the new start (reschedule), the new provider and the new operatory, each id with its display name at the time - in the same save as the move. Rows
    /// written before M4 keep null destinations and are not touched. An id always comes with a non-blank name, and only Rescheduled, ProviderReassigned and RoomChanged may carry one.
    /// Down refuses (51285) once any destination has been recorded, because appointment history is never discarded to make a rollback succeed; otherwise it drops exactly what Up added.
    /// </summary>
    public partial class AddAppointmentMoveDestination : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "NewOperatoryId",
                table: "AppointmentEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewOperatoryName",
                table: "AppointmentEvents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NewProviderName",
                table: "AppointmentEvents",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NewProviderProfileId",
                table: "AppointmentEvents",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NewStartUtc",
                table: "AppointmentEvents",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentEvents_Destination",
                table: "AppointmentEvents",
                sql: "([NewStartUtc] IS NULL AND [NewProviderProfileId] IS NULL AND [NewOperatoryId] IS NULL) OR [EventType] IN ('Rescheduled','ProviderReassigned','RoomChanged')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentEvents_NewOperatory",
                table: "AppointmentEvents",
                sql: "([NewOperatoryId] IS NULL AND [NewOperatoryName] IS NULL) OR ([NewOperatoryId] IS NOT NULL AND [NewOperatoryName] IS NOT NULL AND LEN(LTRIM(RTRIM([NewOperatoryName]))) > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentEvents_NewProvider",
                table: "AppointmentEvents",
                sql: "([NewProviderProfileId] IS NULL AND [NewProviderName] IS NULL) OR ([NewProviderProfileId] IS NOT NULL AND [NewProviderName] IS NOT NULL AND LEN(LTRIM(RTRIM([NewProviderName]))) > 0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [AppointmentEvents] WHERE [NewStartUtc] IS NOT NULL OR [NewProviderProfileId] IS NOT NULL OR [NewOperatoryId] IS NOT NULL)
    THROW 51285, 'Appointment move destinations have been recorded, so this migration cannot be rolled back.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentEvents_Destination",
                table: "AppointmentEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentEvents_NewOperatory",
                table: "AppointmentEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentEvents_NewProvider",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "NewOperatoryId",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "NewOperatoryName",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "NewProviderName",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "NewProviderProfileId",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "NewStartUtc",
                table: "AppointmentEvents");
        }
    }
}
