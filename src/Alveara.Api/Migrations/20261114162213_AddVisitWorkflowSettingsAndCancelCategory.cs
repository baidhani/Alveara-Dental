using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N021 A2, migration M1 (briefing revision 4, section 7): additive only. Two practice settings for the visit workflow (confirmation required, mark ready required; both default
    /// true, including the existing settings row), the cancellation category of OD-19 (controlled list; only a cancelled appointment may carry one; earlier cancellations stay null and
    /// are reported as "Not categorised"), and a reason and category on appointment history events. Down refuses (51280) once any category or reason has been recorded, because
    /// appointment history is never discarded to make a rollback succeed; otherwise it drops exactly what Up added.
    /// </summary>
    public partial class AddVisitWorkflowSettingsAndCancelCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ConfirmationRequired",
                table: "PracticeSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "MarkReadyRequired",
                table: "PracticeSettings",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "CancelCategory",
                table: "Appointments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "AppointmentEvents",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AppointmentEvents",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_CancelCategory",
                table: "Appointments",
                sql: "[CancelCategory] IS NULL OR ([Status] = 'Cancelled' AND [CancelCategory] COLLATE Latin1_General_CS_AS IN ('patient','office','provider','weather','other'))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AppointmentEvents_Reason",
                table: "AppointmentEvents",
                sql: "[Reason] IS NULL OR LEN(LTRIM(RTRIM([Reason]))) > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Appointments] WHERE [CancelCategory] IS NOT NULL) OR EXISTS (SELECT 1 FROM [AppointmentEvents] WHERE [Reason] IS NOT NULL OR [Category] IS NOT NULL)
    THROW 51280, 'Cancellation categories or visit reasons have been recorded, so this migration cannot be rolled back.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_CancelCategory",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AppointmentEvents_Reason",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "ConfirmationRequired",
                table: "PracticeSettings");

            migrationBuilder.DropColumn(
                name: "MarkReadyRequired",
                table: "PracticeSettings");

            migrationBuilder.DropColumn(
                name: "CancelCategory",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "AppointmentEvents");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "AppointmentEvents");
        }
    }
}
