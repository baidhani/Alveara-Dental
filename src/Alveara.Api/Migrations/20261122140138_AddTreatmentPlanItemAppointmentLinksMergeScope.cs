using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N026 S8 (U-022, owner decision 2026-11-22): TreatmentPlanItemAppointmentLinks (ALV-N023) holds a PatientId but was never given merge handling, so a merge moved the plan, its items and
    /// the appointments to the survivor and left the link rows on the merged-away record (and the append-only trigger would have refused to move them). The table now joins the established
    /// merge machinery exactly as AddClinicalRecordCopyIssues did:
    /// - TR_TPItemAppointmentLinks_AppendOnly (INSTEAD OF UPDATE, DELETE) keeps 51292 and gains the /*patient-merge*/ stand-aside that, inside a merge (session value alveara.patient_merge = 1),
    ///   applies only the PatientId change of the rows named (the AddPatientMerges INSTEAD OF shape);
    /// - TR_TreatmentPlanItemAppointmentLinks_MergeScope (AFTER INSERT, UPDATE, DELETE): inside a merge nothing but PatientId may change and no row may be added or removed (51234), every other
    ///   column listed; and the /*retired-record-guard*/ check: outside a merge a new link for a merged-away patient is refused (51235).
    /// TR_TPItemAppointmentLinks_SamePatient (51291) is AFTER INSERT only and is unchanged: a merge only updates, and moves the item, the appointment and the link together.
    /// No column or index changes. Down restores the ALV-N023 trigger and drops the merge-scope trigger (the rows themselves are untouched).
    /// </summary>
    public partial class AddTreatmentPlanItemAppointmentLinksMergeScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"CREATE OR ALTER TRIGGER [TR_TPItemAppointmentLinks_AppendOnly] ON [TreatmentPlanItemAppointmentLinks] INSTEAD OF UPDATE, DELETE AS
/*patient-merge*/ IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND ((EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR NOT (EXISTS (SELECT 1 FROM inserted) OR EXISTS (SELECT 1 FROM deleted))) BEGIN UPDATE t SET t.[PatientId] = i.[PatientId] FROM [TreatmentPlanItemAppointmentLinks] t JOIN inserted i ON t.[Id] = i.[Id]; RETURN; END /*end patient-merge*/
BEGIN
    THROW 51292, 'The appointment-link history of a treatment-plan item is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TreatmentPlanItemAppointmentLinks_MergeScope] ON [TreatmentPlanItemAppointmentLinks] AFTER INSERT, UPDATE, DELETE AS
BEGIN
/*retired-record-guard*/ IF ISNULL(CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')), 0) <> 1 AND EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)
       AND EXISTS (SELECT 1 FROM inserted i JOIN [Patients] p ON p.[Id] = i.[PatientId] WHERE p.[MergedAtUtc] IS NOT NULL)
        THROW 51235, 'A patient record that was merged into another takes no new records.', 1; /*end retired-record-guard*/
    IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1
       AND ((EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)) OR (NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR UPDATE([Id]) OR UPDATE([PlanId]) OR UPDATE([ItemId]) OR UPDATE([AppointmentId]) OR UPDATE([Sequence]) OR UPDATE([Action]) OR UPDATE([Reason]) OR UPDATE([RecordedByUserId]) OR UPDATE([RecordedAtUtc]))
        THROW 51234, 'During a patient merge only the patient id of an existing row may change.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER [TR_TreatmentPlanItemAppointmentLinks_MergeScope];");
            migrationBuilder.Sql(@"CREATE OR ALTER TRIGGER [TR_TPItemAppointmentLinks_AppendOnly] ON [TreatmentPlanItemAppointmentLinks] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51292, 'The appointment-link history of a treatment-plan item is append-only.', 1;
END");
        }
    }
}
