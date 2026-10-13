using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletionAdoption : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdoptionReason",
                table: "CompletionSets",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CompletionSets_AdoptionReason",
                table: "CompletionSets",
                sql: "[AdoptionReason] IS NULL OR (LEN(LTRIM(RTRIM([AdoptionReason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [AdoptionReason] COLLATE Latin1_General_BIN2) = 0)");

            // ALV-008-C02: an adoption is the completion of exactly one procedure (error number 51172; 51173 and up are free)
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletionSets_Adoption] ON [CompletionSets] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted WHERE AdoptionReason IS NOT NULL AND ItemCount <> 1)
        THROW 51172, 'An adoption records the completion of exactly one procedure.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no adoption has been recorded: afterwards the reason is part of the preserved clinical history and is not discarded by a script
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [CompletionSets] WHERE [AdoptionReason] IS NOT NULL)
    THROW 51172, 'An adopted completion exists, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_CompletionSets_Adoption]");
            migrationBuilder.DropCheckConstraint(
                name: "CK_CompletionSets_AdoptionReason",
                table: "CompletionSets");

            migrationBuilder.DropColumn(
                name: "AdoptionReason",
                table: "CompletionSets");
        }
    }
}
