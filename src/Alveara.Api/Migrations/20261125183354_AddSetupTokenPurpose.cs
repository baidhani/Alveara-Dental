using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N030-C01 (brief 18.2, 18.6): SetupTokens.Purpose - 0 Setup (every existing row, by the default), 1 Recovery. Setup and recovery tokens never void or
    /// count against each other. No data step; nothing else changes.
    /// </summary>
    public partial class AddSetupTokenPurpose : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Purpose",
                table: "SetupTokens",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Purpose",
                table: "SetupTokens");
        }
    }
}
