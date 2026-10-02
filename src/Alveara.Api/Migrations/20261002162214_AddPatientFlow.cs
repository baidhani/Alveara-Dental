using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FlowChangedAtUtc",
                table: "Appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FlowChangedByUserId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FlowState",
                table: "Appointments",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Scheduled");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_FlowNeedsScheduled",
                table: "Appointments",
                sql: "[FlowState] = 'Scheduled' OR [Status] = 'Scheduled'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_FlowState",
                table: "Appointments",
                sql: "[FlowState] IN ('Scheduled','CheckedIn','InTreatment','Completed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_FlowNeedsScheduled",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_FlowState",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FlowChangedAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FlowChangedByUserId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "FlowState",
                table: "Appointments");
        }
    }
}
