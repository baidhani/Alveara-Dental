using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentClassificationAndManageDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Classification",
                table: "DocumentCategories",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000001"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000002"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000003"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000004"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000005"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000006"),
                column: "Classification",
                value: "NonClinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000007"),
                column: "Classification",
                value: "NonClinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000008"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.UpdateData(
                table: "DocumentCategories",
                keyColumn: "Id",
                keyValue: new Guid("d0c00000-0000-0000-0000-000000000009"),
                column: "Classification",
                value: "Clinical");

            migrationBuilder.AddCheckConstraint(
                name: "CK_DocumentCategories_Classification",
                table: "DocumentCategories",
                sql: "[Classification] IN (N'Clinical', N'NonClinical')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_DocumentCategories_Classification",
                table: "DocumentCategories");

            migrationBuilder.DropColumn(
                name: "Classification",
                table: "DocumentCategories");
        }
    }
}
