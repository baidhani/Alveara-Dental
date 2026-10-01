using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditConcurrencyAndIdempotencyPrimitives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StaffProfiles",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "CorrelationId",
                table: "AuditLogEntries",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EntityType",
                table: "AuditLogEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "AuditLogEntries",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "IdempotencyReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CommandType = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IdempotencyReceipts_CommandType_IdempotencyKey",
                table: "IdempotencyReceipts",
                columns: new[] { "CommandType", "IdempotencyKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IdempotencyReceipts");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StaffProfiles");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "EntityType",
                table: "AuditLogEntries");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "AuditLogEntries");
        }
    }
}
