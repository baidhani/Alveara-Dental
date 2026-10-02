using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_FlowState",
                table: "Appointments");

            migrationBuilder.AddColumn<bool>(
                name: "RequiredAtCheckIn",
                table: "FormTemplates",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "VisitOperatoryId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VisitProviderProfileId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_FlowState_StartUtc",
                table: "Appointments",
                columns: new[] { "FlowState", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_VisitOperatoryId",
                table: "Appointments",
                column: "VisitOperatoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_VisitProviderProfileId",
                table: "Appointments",
                column: "VisitProviderProfileId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_FlowState",
                table: "Appointments",
                sql: "[FlowState] COLLATE Latin1_General_CS_AS IN ('Scheduled','Confirmed','CheckedIn','Ready','Seated','InTreatment','CheckedOut','Completed')");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_Operatories_VisitOperatoryId",
                table: "Appointments",
                column: "VisitOperatoryId",
                principalTable: "Operatories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_ProviderProfiles_VisitProviderProfileId",
                table: "Appointments",
                column: "VisitProviderProfileId",
                principalTable: "ProviderProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back re-narrows CK_Appointments_FlowState to STORY-011's four states. If any appointment is already Confirmed, Ready, Seated or
            // CheckedOut the constraint cannot be added and this fails loudly - intended: those visits must be resolved by hand, never silently rewritten.
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_Operatories_VisitOperatoryId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_ProviderProfiles_VisitProviderProfileId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_FlowState_StartUtc",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_VisitOperatoryId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_VisitProviderProfileId",
                table: "Appointments");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_FlowState",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "RequiredAtCheckIn",
                table: "FormTemplates");

            migrationBuilder.DropColumn(
                name: "VisitOperatoryId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "VisitProviderProfileId",
                table: "Appointments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_FlowState",
                table: "Appointments",
                sql: "[FlowState] IN ('Scheduled','CheckedIn','InTreatment','Completed')");
        }
    }
}
