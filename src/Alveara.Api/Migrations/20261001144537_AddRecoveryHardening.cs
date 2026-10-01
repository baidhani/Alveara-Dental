using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRecoveryHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "BackupRecordId",
                table: "RestoreDrills",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "ArchiveFileName",
                table: "RestoreDrills",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveSha256",
                table: "RestoreDrills",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceKind",
                table: "RestoreDrills",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "History");

            migrationBuilder.CreateTable(
                name: "DeploymentInvariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PracticeTimeZoneId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DataProtectionApplicationName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeploymentInvariants", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeploymentInvariants");

            migrationBuilder.DropColumn(
                name: "ArchiveFileName",
                table: "RestoreDrills");

            migrationBuilder.DropColumn(
                name: "ArchiveSha256",
                table: "RestoreDrills");

            migrationBuilder.DropColumn(
                name: "SourceKind",
                table: "RestoreDrills");

            migrationBuilder.AlterColumn<Guid>(
                name: "BackupRecordId",
                table: "RestoreDrills",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
