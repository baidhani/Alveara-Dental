using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArchiveDefect : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArchiveDefectAtUtc",
                table: "BackupRecords",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchiveDefectCode",
                table: "BackupRecords",
                type: "nvarchar(max)",
                nullable: true);
            // Backfill: a backup already recorded as failed for an archive-intrinsic reason keeps that reason as its permanent defect.
            migrationBuilder.Sql(
                "UPDATE BackupRecords SET ArchiveDefectCode = VerificationFailureCode, ArchiveDefectAtUtc = COALESCE(VerifiedAtUtc, SYSDATETIMEOFFSET()), RestoreProvenAtUtc = NULL " +
                "WHERE VerificationStatus = 'VerificationFailed' AND VerificationFailureCode IN ('restore_validation_failed','deployment_asset_matches_manifest','deployment_settings_recorded','documents_complete','components_present','component_hashes_match','database_backup_damaged','manifest_missing','manifest_invalid','corrupt_or_tampered')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveDefectAtUtc",
                table: "BackupRecords");

            migrationBuilder.DropColumn(
                name: "ArchiveDefectCode",
                table: "BackupRecords");
        }
    }
}
