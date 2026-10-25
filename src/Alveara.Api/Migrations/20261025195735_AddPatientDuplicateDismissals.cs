using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientDuplicateDismissals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatientDuplicateDismissals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstPatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SecondPatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PairKey = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false, computedColumnSql: "CASE WHEN [FirstPatientId] < [SecondPatientId] THEN CONVERT(char(36), [FirstPatientId]) + '|' + CONVERT(char(36), [SecondPatientId]) ELSE CONVERT(char(36), [SecondPatientId]) + '|' + CONVERT(char(36), [FirstPatientId]) END", stored: true),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    DismissedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DismissedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RestoredByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RestoredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientDuplicateDismissals", x => x.Id);
                    table.CheckConstraint("CK_PatientDuplicateDismissals_Distinct", "[FirstPatientId] <> [SecondPatientId]");
                    table.CheckConstraint("CK_PatientDuplicateDismissals_Reason", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Reason], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Reason] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PatientDuplicateDismissals_Restored", "([RestoredAtUtc] IS NULL AND [RestoredByUserId] IS NULL) OR ([RestoredAtUtc] IS NOT NULL AND [RestoredByUserId] IS NOT NULL AND [RestoredAtUtc] >= [DismissedAtUtc])");
                    table.CheckConstraint("CK_PatientDuplicateDismissals_Times", "[DismissedAtUtc] >= '2000-01-01' AND [DismissedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_PatientDuplicateDismissals_Patients_FirstPatientId",
                        column: x => x.FirstPatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientDuplicateDismissals_Patients_SecondPatientId",
                        column: x => x.SecondPatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientDuplicateDismissals_UserAccounts_DismissedByUserId",
                        column: x => x.DismissedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientDuplicateDismissals_UserAccounts_RestoredByUserId",
                        column: x => x.RestoredByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateDismissals_DismissedByUserId",
                table: "PatientDuplicateDismissals",
                column: "DismissedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateDismissals_FirstPatientId",
                table: "PatientDuplicateDismissals",
                column: "FirstPatientId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateDismissals_OneInForce",
                table: "PatientDuplicateDismissals",
                column: "PairKey",
                unique: true,
                filter: "[RestoredAtUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateDismissals_RestoredByUserId",
                table: "PatientDuplicateDismissals",
                column: "RestoredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientDuplicateDismissals_SecondPatientId",
                table: "PatientDuplicateDismissals",
                column: "SecondPatientId");

            // ALV-017-C01 (errors 51240 to 51243): a dismissal is never deleted and can only be restored once; a merged-away record cannot be dismissed.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientDuplicateDismissals_Guard] ON [PatientDuplicateDismissals] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51240, 'A dismissal of a suggested duplicate is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[RestoredAtUtc] IS NOT NULL)
        THROW 51241, 'A restored dismissal never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[FirstPatientId] <> d.[FirstPatientId] OR i.[SecondPatientId] <> d.[SecondPatientId] OR i.[Reason] <> d.[Reason]
                  OR i.[DismissedByUserId] <> d.[DismissedByUserId] OR i.[DismissedAtUtc] <> d.[DismissedAtUtc])
        THROW 51241, 'A dismissal keeps its pair, its reason and who made it and when; it can only be restored.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientDuplicateDismissals_Active] ON [PatientDuplicateDismissals] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Patients] p ON p.[Id] IN (i.[FirstPatientId], i.[SecondPatientId]) WHERE p.[MergedAtUtc] IS NOT NULL)
        THROW 51242, 'A record that was merged into another cannot be dismissed as a duplicate.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no dismissal exists
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [PatientDuplicateDismissals])
    THROW 51243, 'Duplicate dismissals exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.DropTable(
                name: "PatientDuplicateDismissals");
        }
    }
}
