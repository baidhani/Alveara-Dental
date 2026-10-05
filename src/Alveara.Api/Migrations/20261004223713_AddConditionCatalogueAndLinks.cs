using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConditionCatalogueAndLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ToothFindingVersions_ChangeType",
                table: "ToothFindingVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ToothFindings_Condition",
                table: "ToothFindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ToothFindings_SurfaceMatchesCondition",
                table: "ToothFindings");

            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "ToothFindingVersions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(12)",
                oldMaxLength: 12);

            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "ToothFindings",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                collation: "Latin1_General_CS_AS",
                oldClrType: typeof(string),
                oldType: "nvarchar(12)",
                oldMaxLength: 12);

            migrationBuilder.AddColumn<string>(
                name: "ConditionScope",
                table: "ToothFindings",
                type: "nvarchar(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ConditionTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false, collation: "Latin1_General_CS_AS"),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    AppliesTo = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ToothEffect = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionTypes", x => x.Id);
                    table.UniqueConstraint("AK_ConditionTypes_Code", x => x.Code);
                    table.CheckConstraint("CK_ConditionTypes_AppliesTo", "[AppliesTo] COLLATE Latin1_General_CS_AS IN ('Permanent','Primary','Both')");
                    table.CheckConstraint("CK_ConditionTypes_CodeShape", "LEN([Code]) >= 2 AND [Code] COLLATE Latin1_General_BIN NOT LIKE '%[^A-Za-z0-9]%' AND LEFT([Code], 1) COLLATE Latin1_General_BIN LIKE '[A-Z]'");
                    table.CheckConstraint("CK_ConditionTypes_LabelNotBlank", "LEN(LTRIM(RTRIM([Label]))) > 0");
                    table.CheckConstraint("CK_ConditionTypes_Scope", "[Scope] COLLATE Latin1_General_CS_AS IN ('Surface','WholeTooth')");
                    table.CheckConstraint("CK_ConditionTypes_ToothEffect", "[ToothEffect] COLLATE Latin1_General_CS_AS IN ('None','Absent','Replacement')");
                });

            migrationBuilder.CreateTable(
                name: "ToothFindingLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToothFindingLinks", x => x.Id);
                    table.CheckConstraint("CK_ToothFindingLinks_LinkType", "[LinkType] COLLATE Latin1_General_CS_AS IN ('Diagnosis','TreatmentPlan','Procedure')");
                    table.CheckConstraint("CK_ToothFindingLinks_ReferenceNotBlank", "LEN(LTRIM(RTRIM([Reference]))) > 0");
                    table.ForeignKey(
                        name: "FK_ToothFindingLinks_ToothFindings_FindingId",
                        column: x => x.FindingId,
                        principalTable: "ToothFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ConditionTypeEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConditionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EventNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConditionTypeEvents", x => x.Id);
                    table.CheckConstraint("CK_ConditionTypeEvents_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Retired','Reactivated')");
                    table.ForeignKey(
                        name: "FK_ConditionTypeEvents_ConditionTypes_ConditionTypeId",
                        column: x => x.ConditionTypeId,
                        principalTable: "ConditionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "ConditionTypes",
                columns: new[] { "Id", "AppliesTo", "Code", "CreatedAtUtc", "CreatedByUserId", "IsActive", "Label", "Scope", "ToothEffect", "UpdatedAtUtc", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("c0de0001-0000-4000-8000-000000000001"), "Both", "Caries", new DateTimeOffset(new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Caries", "Surface", "None", null, null },
                    { new Guid("c0de0001-0000-4000-8000-000000000002"), "Both", "Restoration", new DateTimeOffset(new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Restoration", "Surface", "None", null, null },
                    { new Guid("c0de0001-0000-4000-8000-000000000003"), "Both", "Crown", new DateTimeOffset(new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Crown", "WholeTooth", "None", null, null },
                    { new Guid("c0de0001-0000-4000-8000-000000000004"), "Both", "Missing", new DateTimeOffset(new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Missing tooth", "WholeTooth", "Absent", null, null },
                    { new Guid("c0de0001-0000-4000-8000-000000000005"), "Permanent", "Implant", new DateTimeOffset(new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Implant", "WholeTooth", "Replacement", null, null },
                    { new Guid("c0de0001-0000-4000-8000-000000000006"), "Both", "RootCanal", new DateTimeOffset(new DateTime(2026, 10, 4, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, "Root canal", "WholeTooth", "None", null, null }
                });

            // ALV-006-C01: findings recorded before the catalogue existed carry the scope their condition always had (the six conditions' scope is the same as the seeded catalogue's);
            // this runs after the catalogue is seeded and before the checks and the foreign key that need it.
            migrationBuilder.Sql("UPDATE [ToothFindings] SET [ConditionScope] = CASE WHEN [Condition] IN ('Caries', 'Restoration') THEN 'Surface' ELSE 'WholeTooth' END");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ToothFindingVersions_ChangeType",
                table: "ToothFindingVersions",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','StateChanged','Withdrawn','Linked')");

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindings_Condition",
                table: "ToothFindings",
                column: "Condition");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ToothFindings_ConditionScope",
                table: "ToothFindings",
                sql: "[ConditionScope] COLLATE Latin1_General_CS_AS IN ('Surface','WholeTooth')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ToothFindings_SurfaceMatchesScope",
                table: "ToothFindings",
                sql: "([ConditionScope] = 'Surface' AND [Surface] IS NOT NULL) OR ([ConditionScope] = 'WholeTooth' AND [Surface] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ConditionTypeEvents_ConditionTypeId_EventNumber",
                table: "ConditionTypeEvents",
                columns: new[] { "ConditionTypeId", "EventNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindingLinks_FindingId_LinkType_Reference",
                table: "ToothFindingLinks",
                columns: new[] { "FindingId", "LinkType", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ToothFindingLinks_PatientId",
                table: "ToothFindingLinks",
                column: "PatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_ToothFindings_ConditionTypes_Condition",
                table: "ToothFindings",
                column: "Condition",
                principalTable: "ConditionTypes",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            // ALV-006-C01: the database refuses, independently of the application, to delete a condition type, to change what one means once it exists (its code, label, scope, dentition and tooth
            // effect - so a finding recorded last year can never be re-described), to change or delete its history, and to change or delete a link from a finding.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ConditionTypes_NoDelete] ON [ConditionTypes] INSTEAD OF DELETE AS
BEGIN
    THROW 51057, 'Condition types are never deleted; they are retired with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ConditionTypeEvents_Immutable] ON [ConditionTypeEvents] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51058, 'The history of a condition type is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ToothFindingLinks_Immutable] ON [ToothFindingLinks] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51059, 'Links from a finding are append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ConditionTypes_Definition] ON [ConditionTypes] AFTER UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id = i.Id
               WHERE i.Code <> d.Code COLLATE Latin1_General_CS_AS OR i.Label <> d.Label COLLATE Latin1_General_CS_AS OR i.Scope <> d.Scope COLLATE Latin1_General_CS_AS
                  OR i.AppliesTo <> d.AppliesTo COLLATE Latin1_General_CS_AS OR i.ToothEffect <> d.ToothEffect COLLATE Latin1_General_CS_AS)
        THROW 51060, 'A condition type''s code, label, scope, dentition and tooth effect are fixed once it is created; retire it and create another.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ToothFindings_ConditionTypes_Condition",
                table: "ToothFindings");

            migrationBuilder.DropTable(
                name: "ConditionTypeEvents");

            migrationBuilder.DropTable(
                name: "ToothFindingLinks");

            migrationBuilder.DropTable(
                name: "ConditionTypes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ToothFindingVersions_ChangeType",
                table: "ToothFindingVersions");

            migrationBuilder.DropIndex(
                name: "IX_ToothFindings_Condition",
                table: "ToothFindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ToothFindings_ConditionScope",
                table: "ToothFindings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ToothFindings_SurfaceMatchesScope",
                table: "ToothFindings");

            migrationBuilder.DropColumn(
                name: "ConditionScope",
                table: "ToothFindings");

            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "ToothFindingVersions",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Condition",
                table: "ToothFindings",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldCollation: "Latin1_General_CS_AS");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ToothFindingVersions_ChangeType",
                table: "ToothFindingVersions",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Recorded','StateChanged','Withdrawn')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ToothFindings_Condition",
                table: "ToothFindings",
                sql: "[Condition] COLLATE Latin1_General_CS_AS IN ('Caries','Restoration','Crown','Missing','Implant','RootCanal')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ToothFindings_SurfaceMatchesCondition",
                table: "ToothFindings",
                sql: "([Condition] COLLATE Latin1_General_CS_AS IN ('Caries','Restoration') AND [Surface] IS NOT NULL) OR ([Condition] COLLATE Latin1_General_CS_AS NOT IN ('Caries','Restoration') AND [Surface] IS NULL)");
        }
    }
}
