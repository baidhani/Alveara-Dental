using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPracticeConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StaffProfiles_UserAccountId",
                table: "StaffProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProviderProfiles_StaffProfileId",
                table: "ProviderProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "StaffProfiles",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "StaffProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "JobTitle",
                table: "StaffProfiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Specialty",
                table: "ProviderProfiles",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ProviderProfiles",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "ProviderProfiles",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateTable(
                name: "AppointmentTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DefaultDurationMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppointmentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PracticeLocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PracticeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    AddressLine = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderBlockedTimes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderBlockedTimes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderBlockedTimes_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProviderWeeklyAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartLocal = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndLocal = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderWeeklyAvailabilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProviderWeeklyAvailabilities_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Operatories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operatories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operatories_PracticeLocations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "PracticeLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_DisplayName",
                table: "StaffProfiles",
                column: "DisplayName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_UserAccountId",
                table: "StaffProfiles",
                column: "UserAccountId",
                unique: true,
                filter: "[UserAccountId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderProfiles_StaffProfileId",
                table: "ProviderProfiles",
                column: "StaffProfileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppointmentTypes_Name",
                table: "AppointmentTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Operatories_LocationId_Name",
                table: "Operatories",
                columns: new[] { "LocationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PracticeLocations_Name",
                table: "PracticeLocations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PracticeLocations_SingleActive",
                table: "PracticeLocations",
                column: "IsActive",
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderBlockedTimes_ProviderProfileId_StartUtc",
                table: "ProviderBlockedTimes",
                columns: new[] { "ProviderProfileId", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderWeeklyAvailabilities_ProviderProfileId_DayOfWeek",
                table: "ProviderWeeklyAvailabilities",
                columns: new[] { "ProviderProfileId", "DayOfWeek" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppointmentTypes");

            migrationBuilder.DropTable(
                name: "Operatories");

            migrationBuilder.DropTable(
                name: "PracticeSettings");

            migrationBuilder.DropTable(
                name: "ProviderBlockedTimes");

            migrationBuilder.DropTable(
                name: "ProviderWeeklyAvailabilities");

            migrationBuilder.DropTable(
                name: "PracticeLocations");

            migrationBuilder.DropIndex(
                name: "IX_StaffProfiles_DisplayName",
                table: "StaffProfiles");

            migrationBuilder.DropIndex(
                name: "IX_StaffProfiles_UserAccountId",
                table: "StaffProfiles");

            migrationBuilder.DropIndex(
                name: "IX_ProviderProfiles_StaffProfileId",
                table: "ProviderProfiles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "JobTitle",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ProviderProfiles");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "ProviderProfiles");

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "StaffProfiles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Specialty",
                table: "ProviderProfiles",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(80)",
                oldMaxLength: 80);

            migrationBuilder.CreateIndex(
                name: "IX_StaffProfiles_UserAccountId",
                table: "StaffProfiles",
                column: "UserAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ProviderProfiles_StaffProfileId",
                table: "ProviderProfiles",
                column: "StaffProfileId");
        }
    }
}
