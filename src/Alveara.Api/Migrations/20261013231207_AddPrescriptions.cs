using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPrescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Prescriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Medication = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DoseValue = table.Column<decimal>(type: "decimal(9,3)", precision: 9, scale: 3, nullable: false),
                    DoseUnit = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Frequency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AllergyOutcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    AllergyListState = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AllergyMatches = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrescriberUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                    table.CheckConstraint("CK_Prescriptions_Allergy", "[AllergyOutcome] COLLATE Latin1_General_CS_AS IN ('RecordedAllergyMatch','NoRecordedMatch','NotEstablished') AND [AllergyListState] COLLATE Latin1_General_CS_AS IN ('Established','Unconfirmed','NotEstablished') AND (([AllergyOutcome] = 'RecordedAllergyMatch' AND [AllergyMatches] IS NOT NULL AND LEN(LTRIM(RTRIM([AllergyMatches]))) > 0) OR ([AllergyOutcome] = 'NoRecordedMatch' AND [AllergyListState] = 'Established' AND [AllergyMatches] IS NULL) OR ([AllergyOutcome] = 'NotEstablished' AND [AllergyListState] <> 'Established' AND [AllergyMatches] IS NULL))");
                    table.CheckConstraint("CK_Prescriptions_Created", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_Prescriptions_Dose", "[DoseValue] > 0 AND (([DoseUnit] COLLATE Latin1_General_CS_AS = 'mg' AND [DoseValue] <= 5000) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'mcg' AND [DoseValue] <= 5000) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'g' AND [DoseValue] <= 10) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'mL' AND [DoseValue] <= 100) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'tablet' AND [DoseValue] <= 10) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'capsule' AND [DoseValue] <= 10) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'drop' AND [DoseValue] <= 20) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'puff' AND [DoseValue] <= 20) OR ([DoseUnit] COLLATE Latin1_General_CS_AS = 'unit' AND [DoseValue] <= 1000))");
                    table.CheckConstraint("CK_Prescriptions_Frequency", "[Frequency] COLLATE Latin1_General_CS_AS IN ('Once','Once daily','Twice daily','Three times daily','Four times daily','Every 4 hours','Every 6 hours','Every 8 hours','Every 12 hours','At bedtime','As needed')");
                    table.CheckConstraint("CK_Prescriptions_Hash", "[RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%' AND LEN([RequestHash]) = 64");
                    table.CheckConstraint("CK_Prescriptions_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Prescriptions_Medication", "LEN(LTRIM(RTRIM([Medication]))) >= 2 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Medication] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_Prescriptions_Review", "[ReviewNote] IS NULL OR LEN(LTRIM(RTRIM([ReviewNote]))) > 0");
                    table.CheckConstraint("CK_Prescriptions_ReviewRequired", "[AllergyOutcome] = 'NoRecordedMatch' OR [ReviewNote] IS NOT NULL");
                    table.CheckConstraint("CK_Prescriptions_Unit", "[DoseUnit] COLLATE Latin1_General_CS_AS IN ('mg','mcg','g','mL','tablet','capsule','drop','puff','unit')");
                    table.ForeignKey(
                        name: "FK_Prescriptions_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Prescriptions_UserAccounts_PrescriberUserId",
                        column: x => x.PrescriberUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PatientId_CreatedAtUtc",
                table: "Prescriptions",
                columns: new[] { "PatientId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PatientId_IdempotencyKey",
                table: "Prescriptions",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PrescriberUserId",
                table: "Prescriptions",
                column: "PrescriberUserId");

            // ---- STORY-016 trigger (error 51174; 51175 and up are free): a prescription is append-only. A correction is a new record, never a change to this one. ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Prescriptions_Immutable] ON [Prescriptions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51174, 'A prescription is append-only; it is never changed or deleted.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no prescription has been recorded: afterwards the table holds clinical history that a script must not drop
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Prescriptions])
    THROW 51174, 'A prescription exists, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "Prescriptions");
        }
    }
}
