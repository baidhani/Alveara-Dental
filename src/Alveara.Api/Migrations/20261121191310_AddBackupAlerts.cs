using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N026 (PD-25): backup alerts in the administrators' inbox. One more notification kind, backup.alert, with a source key and no message, patient or document (PHI-free); and an
    /// append-only acknowledgement per backup alert (TR_BackupAlertAcknowledgements_Immutable, 51303, refuses a change or a deletion). Down refuses (51304) once any backup.alert notification
    /// or acknowledgement exists, because both are history; on an empty set it restores the five-kind rules.
    /// </summary>
    public partial class AddBackupAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications");

            migrationBuilder.CreateTable(
                name: "BackupAlertAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BackupNotificationRecordId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcknowledgedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupAlertAcknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupAlertAcknowledgements_BackupNotifications_BackupNotificationRecordId",
                        column: x => x.BackupNotificationRecordId,
                        principalTable: "BackupNotifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BackupAlertAcknowledgements_UserAccounts_AcknowledgedByUserId",
                        column: x => x.AcknowledgedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received','followup.due','staff.inactivation_failed','appointment.provider_reassigned','backup.alert')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId]) OR ([Kind] = 'followup.due' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'staff.inactivation_failed' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'appointment.provider_reassigned' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'backup.alert' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_BackupAlertAcknowledgements_AcknowledgedByUserId",
                table: "BackupAlertAcknowledgements",
                column: "AcknowledgedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupAlertAcknowledgements_BackupNotificationRecordId",
                table: "BackupAlertAcknowledgements",
                column: "BackupNotificationRecordId",
                unique: true);

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_BackupAlertAcknowledgements_Immutable] ON [BackupAlertAcknowledgements] AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted)
        THROW 51303, 'A backup alert acknowledgement is history: it is never changed or deleted.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Notifications] WHERE [Kind] = 'backup.alert') OR EXISTS (SELECT 1 FROM [BackupAlertAcknowledgements])
    THROW 51304, 'Backup alerts or their acknowledgements exist, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_BackupAlertAcknowledgements_Immutable];");
            migrationBuilder.DropTable(
                name: "BackupAlertAcknowledgements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received','followup.due','staff.inactivation_failed','appointment.provider_reassigned')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId]) OR ([Kind] = 'followup.due' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'staff.inactivation_failed' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'appointment.provider_reassigned' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL)");
        }
    }
}
