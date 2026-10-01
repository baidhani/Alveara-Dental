using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupAndRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupNotifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BackupRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Delivery = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeliveryFailureCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupNotifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InitiatedByUserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DestinationDirectory = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    IncludedAssetClasses = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SchemaMigration = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    AppVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RecoveryKeyFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VerificationStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    VerifiedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerificationFailureCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ScheduleEnabled = table.Column<bool>(type: "bit", nullable: false),
                    ScheduleIntervalHours = table.Column<int>(type: "int", nullable: false),
                    RetentionCount = table.Column<int>(type: "int", nullable: false),
                    DestinationDirectory = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecoveryPublicKeyPem = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecoveryKeyFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RecoveryKeyConfiguredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RequiredSuccessfulVerifications = table.Column<int>(type: "int", nullable: false),
                    VerificationCadenceDays = table.Column<int>(type: "int", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RestoreDrills",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BackupRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InitiatedByUserAccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FailureCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TargetDatabase = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TargetDirectory = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ValidationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TargetRemoved = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestoreDrills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RestoreDrills_BackupRecords_BackupRecordId",
                        column: x => x.BackupRecordId,
                        principalTable: "BackupRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupNotifications_CreatedAtUtc",
                table: "BackupNotifications",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_IdempotencyKey",
                table: "BackupRecords",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupRecords_StartedAtUtc",
                table: "BackupRecords",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreDrills_BackupRecordId",
                table: "RestoreDrills",
                column: "BackupRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_RestoreDrills_StartedAtUtc",
                table: "RestoreDrills",
                column: "StartedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupNotifications");

            migrationBuilder.DropTable(
                name: "BackupSettings");

            migrationBuilder.DropTable(
                name: "RestoreDrills");

            migrationBuilder.DropTable(
                name: "BackupRecords");
        }
    }
}
