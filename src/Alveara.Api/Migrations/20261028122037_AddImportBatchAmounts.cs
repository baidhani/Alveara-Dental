using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddImportBatchAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AcceptedAmount",
                table: "ImportBatches",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ValidAmount",
                table: "ImportBatches",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_ImportBatches_Amounts",
                table: "ImportBatches",
                sql: "[ValidAmount] >= 0 AND [AcceptedAmount] >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ImportBatches_Amounts",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "AcceptedAmount",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "ValidAmount",
                table: "ImportBatches");
        }
    }
}
