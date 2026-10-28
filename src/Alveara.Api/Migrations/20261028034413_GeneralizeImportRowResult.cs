using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class GeneralizeImportRowResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ImportBatchRows_Patients_ResultRecordId",
                table: "ImportBatchRows");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_ImportBatchRows_Patients_ResultRecordId",
                table: "ImportBatchRows",
                column: "ResultRecordId",
                principalTable: "Patients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
