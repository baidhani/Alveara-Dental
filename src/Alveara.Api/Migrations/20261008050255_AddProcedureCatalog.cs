using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProcedureCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProcedureDefinitions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CodeSystem = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false, collation: "Latin1_General_CS_AS"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, collation: "Latin1_General_CS_AS"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CurrentVersionNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureDefinitions", x => x.Id);
                    table.CheckConstraint("CK_ProcedureDefinitions_CdtShape", "[CodeSystem] COLLATE Latin1_General_CS_AS <> 'CDT' OR [Code] COLLATE Latin1_General_BIN2 LIKE 'D[0-9][0-9][0-9][0-9]'");
                    table.CheckConstraint("CK_ProcedureDefinitions_CodeShape", "LEN([Code]) >= 1 AND [Code] COLLATE Latin1_General_BIN2 NOT LIKE '%[^-A-Z0-9.]%' AND LEFT([Code], 1) COLLATE Latin1_General_BIN2 LIKE '[A-Z0-9]'");
                    table.CheckConstraint("CK_ProcedureDefinitions_CodeSystem", "[CodeSystem] COLLATE Latin1_General_CS_AS IN ('Local','CDT','External')");
                    table.CheckConstraint("CK_ProcedureDefinitions_CurrentVersion", "[CurrentVersionNumber] >= 1");
                    table.CheckConstraint("CK_ProcedureDefinitions_LocalNotCdt", "[CodeSystem] COLLATE Latin1_General_CS_AS <> 'Local' OR ([Code] COLLATE Latin1_General_BIN2 NOT LIKE 'D[0-9][0-9][0-9][0-9]' AND LEN([Code]) >= 2 AND LEN([Code]) <= 20 AND [Code] COLLATE Latin1_General_BIN2 NOT LIKE '%.%')");
                });

            migrationBuilder.CreateTable(
                name: "ProcedureEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureEvents", x => x.Id);
                    table.CheckConstraint("CK_ProcedureEvents_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Revised','Inactivated','Reactivated')");
                    table.CheckConstraint("CK_ProcedureEvents_Reason", "[ChangeType] COLLATE Latin1_General_CS_AS NOT IN ('Revised','Inactivated') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
                    table.CheckConstraint("CK_ProcedureEvents_Version", "([ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Revised') AND [VersionNumber] IS NOT NULL) OR ([ChangeType] COLLATE Latin1_General_CS_AS IN ('Inactivated','Reactivated') AND [VersionNumber] IS NULL)");
                    table.ForeignKey(
                        name: "FK_ProcedureEvents_ProcedureDefinitions_ProcedureId",
                        column: x => x.ProcedureId,
                        principalTable: "ProcedureDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcedureVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Dentition = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SourceName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    SourceVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidThrough = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureVersions", x => x.Id);
                    table.CheckConstraint("CK_ProcedureVersions_Category", "[Category] COLLATE Latin1_General_CS_AS IN ('Diagnostic','Preventive','Restorative','Endodontic','Periodontic','Prosthodontic','OralSurgery','Orthodontic','Adjunctive','Other')");
                    table.CheckConstraint("CK_ProcedureVersions_Dates", "[EffectiveFrom] >= '2000-01-01' AND ([ValidThrough] IS NULL OR [ValidThrough] >= [EffectiveFrom])");
                    table.CheckConstraint("CK_ProcedureVersions_Dentition", "[Dentition] COLLATE Latin1_General_CS_AS IN ('Permanent','Primary','Both')");
                    table.CheckConstraint("CK_ProcedureVersions_DentitionMatchesScope", "[Scope] COLLATE Latin1_General_CS_AS IN ('Tooth','ToothSurface') OR [Dentition] COLLATE Latin1_General_CS_AS = 'Both'");
                    table.CheckConstraint("CK_ProcedureVersions_Description", "LEN(LTRIM(RTRIM([Description]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Description] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_ProcedureVersions_Fee", "[Fee] >= 0 AND [Fee] <= 1000000.00 AND [Fee] = ROUND([Fee], 2)");
                    table.CheckConstraint("CK_ProcedureVersions_Scope", "[Scope] COLLATE Latin1_General_CS_AS IN ('WholeMouth','Arch','Quadrant','Tooth','ToothSurface')");
                    table.CheckConstraint("CK_ProcedureVersions_VersionNumber", "[VersionNumber] >= 1");
                    table.ForeignKey(
                        name: "FK_ProcedureVersions_ProcedureDefinitions_ProcedureId",
                        column: x => x.ProcedureId,
                        principalTable: "ProcedureDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureDefinitions_CodeSystem_Code",
                table: "ProcedureDefinitions",
                columns: new[] { "CodeSystem", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureDefinitions_IsActive",
                table: "ProcedureDefinitions",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureEvents_ProcedureId_EventNumber",
                table: "ProcedureEvents",
                columns: new[] { "ProcedureId", "EventNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureVersions_ProcedureId_EffectiveFrom",
                table: "ProcedureVersions",
                columns: new[] { "ProcedureId", "EffectiveFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcedureVersions_ProcedureId_VersionNumber",
                table: "ProcedureVersions",
                columns: new[] { "ProcedureId", "VersionNumber" },
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ProcedureDefinitions_NoDelete] ON [ProcedureDefinitions] INSTEAD OF DELETE AS
BEGIN
    THROW 51077, 'Procedures are never deleted; they are inactivated with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ProcedureDefinitions_Identity] ON [ProcedureDefinitions] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE i.CodeSystem <> d.CodeSystem COLLATE Latin1_General_CS_AS OR i.Code <> d.Code COLLATE Latin1_General_CS_AS
                  OR i.CreatedAtUtc <> d.CreatedAtUtc OR i.CreatedByUserId <> d.CreatedByUserId)
        THROW 51078, 'A procedure''s code system and code are its identity and are fixed once it exists; inactivate it and add the correct procedure.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ProcedureVersions_Immutable] ON [ProcedureVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51079, 'A version of a procedure is never changed or removed; a change is a new version.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ProcedureEvents_Immutable] ON [ProcedureEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51080, 'The history of a procedure is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ProcedureVersions_Order] ON [ProcedureVersions] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.VersionNumber > 1
               AND NOT EXISTS (SELECT 1 FROM [ProcedureVersions] p WHERE p.ProcedureId = i.ProcedureId AND p.VersionNumber = i.VersionNumber - 1))
        THROW 51081, 'Versions of a procedure are numbered one after another.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ProcedureVersions] p ON p.ProcedureId = i.ProcedureId AND p.VersionNumber = i.VersionNumber - 1
               WHERE i.EffectiveFrom < p.EffectiveFrom)
        THROW 51082, 'A new version cannot start before the version it follows; history is not rewritten.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcedureEvents");

            migrationBuilder.DropTable(
                name: "ProcedureVersions");

            migrationBuilder.DropTable(
                name: "ProcedureDefinitions");
        }
    }
}
