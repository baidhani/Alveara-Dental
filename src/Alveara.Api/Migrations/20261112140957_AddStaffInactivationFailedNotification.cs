using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N020 (F-021; migration decision Option A): one more notification kind, staff.inactivation_failed, with a source key and no message, patient or document.
    /// Additive: only the two check constraints change; no table, column, index or trigger. Down refuses (51275) once any such notification exists, because
    /// notifications are immutable history (TR_Notifications_NoDelete) and are never deleted to make a rollback succeed; on an empty table it restores the
    /// three-kind rules.
    /// </summary>
    public partial class AddStaffInactivationFailedNotification : Migration
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

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received','followup.due','staff.inactivation_failed')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId]) OR ([Kind] = 'followup.due' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'staff.inactivation_failed' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Notifications] WHERE [Kind] = 'staff.inactivation_failed')
    THROW 51275, 'Staff-inactivation failure notifications exist, so this migration cannot be rolled back.', 1;");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Kind",
                table: "Notifications",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received','followup.due')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId]) OR ([Kind] = 'followup.due' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL)");
        }
    }
}
