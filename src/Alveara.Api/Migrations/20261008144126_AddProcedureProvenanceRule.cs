using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProcedureProvenanceRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_ProcedureVersions_SourceText",
                table: "ProcedureVersions",
                sql: "([SourceName] IS NULL OR LEN(LTRIM(RTRIM([SourceName]))) > 0) AND ([SourceVersion] IS NULL OR LEN(LTRIM(RTRIM([SourceVersion]))) > 0)");

            // ALV-N005 R02 (review finding ALV-N005-R01-01): every CDT or external version names the source of its code set; a local version has none. A CHECK cannot see the
            // definition's code system, so this is a trigger, like the ordering rules. The edition (SourceVersion) stays optional for non-local systems.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ProcedureVersions_Provenance] ON [ProcedureVersions] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ProcedureDefinitions] d ON d.Id = i.ProcedureId
               WHERE d.CodeSystem COLLATE Latin1_General_CS_AS <> 'Local' AND (i.SourceName IS NULL OR LEN(LTRIM(RTRIM(i.SourceName))) = 0))
        THROW 51083, 'A CDT or external procedure must name the source of its code set.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ProcedureDefinitions] d ON d.Id = i.ProcedureId
               WHERE d.CodeSystem COLLATE Latin1_General_CS_AS = 'Local' AND (i.SourceName IS NOT NULL OR i.SourceVersion IS NOT NULL))
        THROW 51084, 'A local procedure has no external source.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_ProcedureVersions_Provenance]");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProcedureVersions_SourceText",
                table: "ProcedureVersions");
        }
    }
}
