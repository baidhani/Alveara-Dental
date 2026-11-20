using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N025 slice 5 (PD-20, BR-01, SES-1; the last of the forward migrations): the patient-access trail's tables.
    /// - <c>AuditSessions</c>: the audit-only lifecycle of a signed-in session. CHECKs: <c>EndKind</c> is SignedOut, Expired or Revoked, and <c>EndedAtUtc</c> and
    ///   <c>EndKind</c> are both null or both set. Guard 51301 (TR_AuditSessions_Preserve): a row whose <c>EndedAtUtc</c> is set never changes again, the identifying columns
    ///   (SessionId, UserId, IssuedAtUtc, ExpiresAtUtc) never change, and no row is deleted.
    /// - <c>PatientRecordOpens</c>: the first open of a patient's record in a session, keyed (SessionId, PatientId); append-only, guard 51299 (TR_PatientRecordOpens_AppendOnly).
    ///   It has no foreign key to Patients and is not a merge-moved table: it records who opened which record, as it was.
    /// Down is refused (51300) once either table holds a row - it never deletes recorded access; otherwise it removes both tables and their guards.
    /// </summary>
    public partial class AddPatientAccessAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditSessions",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IssuedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EndKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    EndAuditWritten = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditSessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "PatientRecordOpens",
                columns: table => new
                {
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstOpenedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientRecordOpens", x => new { x.SessionId, x.PatientId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditSessions_EndedAtUtc_ExpiresAtUtc",
                table: "AuditSessions",
                columns: new[] { "EndedAtUtc", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditSessions_UserId",
                table: "AuditSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientRecordOpens_PatientId",
                table: "PatientRecordOpens",
                column: "PatientId");

            migrationBuilder.Sql(@"ALTER TABLE [AuditSessions] ADD CONSTRAINT [CK_AuditSessions_EndKind] CHECK ([EndKind] IS NULL OR [EndKind] IN (N'SignedOut', N'Expired', N'Revoked'));
ALTER TABLE [AuditSessions] ADD CONSTRAINT [CK_AuditSessions_EndPair] CHECK (([EndedAtUtc] IS NULL AND [EndKind] IS NULL) OR ([EndedAtUtc] IS NOT NULL AND [EndKind] IS NOT NULL));");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_AuditSessions_Preserve] ON [AuditSessions] AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.[EndedAtUtc] IS NOT NULL)
        THROW 51301, 'A closed session lifecycle record never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[SessionId] = i.[SessionId]
               WHERE i.[UserId] <> d.[UserId] OR i.[IssuedAtUtc] <> d.[IssuedAtUtc] OR i.[ExpiresAtUtc] <> d.[ExpiresAtUtc])
       OR (SELECT COUNT(*) FROM inserted) <> (SELECT COUNT(*) FROM inserted i JOIN deleted d ON d.[SessionId] = i.[SessionId])
        THROW 51301, 'The identity of a session lifecycle record never changes.', 1;
END");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_AuditSessions_NoDelete] ON [AuditSessions] INSTEAD OF DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51301, 'A session lifecycle record is never deleted.', 1;
END");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientRecordOpens_AppendOnly] ON [PatientRecordOpens] INSTEAD OF UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51299, 'The record-open trail is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [AuditSessions]) OR EXISTS (SELECT 1 FROM [PatientRecordOpens])
    THROW 51300, 'Session or record-open audit rows exist; rolling back would delete recorded access, so it is refused.', 1;");

            migrationBuilder.DropTable(
                name: "AuditSessions");

            migrationBuilder.DropTable(
                name: "PatientRecordOpens");
        }
    }
}
