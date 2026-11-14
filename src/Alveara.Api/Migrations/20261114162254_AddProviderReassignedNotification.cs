using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N021 A2, migration M2 (OD-15; ALV-N020 pattern): one more notification kind, appointment.provider_reassigned, with a source key and no message, patient or document (PHI-free).
    /// Additive: only the two check constraints change. Down refuses (51281) once any such notification exists, because notifications are immutable history (TR_Notifications_NoDelete);
    /// on an empty set it restores the four-kind rules.
    /// </summary>
    public partial class AddProviderReassignedNotification : Migration
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
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('document.duplicate_added','message.received','followup.due','staff.inactivation_failed','appointment.provider_reassigned')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Shape",
                table: "Notifications",
                sql: "([Kind] = 'message.received' AND [MessageId] IS NOT NULL AND [SourceKey] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'document.duplicate_added' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NOT NULL AND [RelatedOtherDocumentId] IS NOT NULL AND [RelatedDocumentId] <> [RelatedOtherDocumentId]) OR ([Kind] = 'followup.due' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NOT NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'staff.inactivation_failed' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL) OR ([Kind] = 'appointment.provider_reassigned' AND [MessageId] IS NULL AND [SourceKey] IS NOT NULL AND [RelatedPatientId] IS NULL AND [RelatedDocumentId] IS NULL AND [RelatedOtherDocumentId] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [Notifications] WHERE [Kind] = 'appointment.provider_reassigned')
    THROW 51281, 'Provider-reassignment notifications exist, so this migration cannot be rolled back.', 1;");

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
    }
}
