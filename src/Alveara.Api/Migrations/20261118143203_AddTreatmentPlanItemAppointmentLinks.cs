using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// ALV-N023 (owner decisions DR-1, OQ-1; briefing revision 2 approved by review BRIEFING-R02): the append-only table TreatmentPlanItemAppointmentLinks (which real appointment books an
    /// accepted plan item), with a same-patient guard (51291) and an append-only guard (51292); the treatment-plan event change types gain AppointmentLinked, AppointmentUnlinked (reason
    /// required), TargetDateSet and TargetDateCleared. No existing row is changed. Down is refused (51293) once any link exists, so no link history is lost.
    /// </summary>
    public partial class AddTreatmentPlanItemAppointmentLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents");

            migrationBuilder.CreateTable(
                name: "TreatmentPlanItemAppointmentLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentPlanItemAppointmentLinks", x => x.Id);
                    table.CheckConstraint("CK_TPItemAppointmentLinks_Action", "[Action] COLLATE Latin1_General_CS_AS IN ('Linked','Unlinked')");
                    table.CheckConstraint("CK_TPItemAppointmentLinks_Reason", "([Action] = 'Unlinked' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([Action] = 'Linked' AND [Reason] IS NULL)");
                    table.CheckConstraint("CK_TPItemAppointmentLinks_Sequence", "[Sequence] >= 1");
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItemAppointmentLinks_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItemAppointmentLinks_TreatmentPlanItems_ItemId",
                        column: x => x.ItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentPlanItemAppointmentLinks_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','ItemAdded','ItemWithdrawn','Renamed','Withdrawn','Revised','ReferenceCodeSet','ItemAccepted','ItemDeclined','PlanAccepted','PlanDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted','AppointmentLinked','AppointmentUnlinked','TargetDateSet','TargetDateCleared')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents",
                sql: "([ChangeType] IN ('ItemAdded','ItemWithdrawn','ItemAccepted','ItemDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted','AppointmentLinked','AppointmentUnlinked','TargetDateSet','TargetDateCleared') AND [ItemId] IS NOT NULL) OR ([ChangeType] NOT IN ('ItemAdded','ItemWithdrawn','ItemAccepted','ItemDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted','AppointmentLinked','AppointmentUnlinked','TargetDateSet','TargetDateCleared') AND [ItemId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] NOT IN ('ItemWithdrawn','Withdrawn','ItemDeclined','PlanDeclined','Revised','AppointmentUnlinked') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemAppointmentLinks_AppointmentId",
                table: "TreatmentPlanItemAppointmentLinks",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemAppointmentLinks_ItemId_Sequence",
                table: "TreatmentPlanItemAppointmentLinks",
                columns: new[] { "ItemId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentPlanItemAppointmentLinks_PlanId",
                table: "TreatmentPlanItemAppointmentLinks",
                column: "PlanId");

            // ALV-N023 (briefing revision 2, section 4.2): the link must name an appointment of the item's own patient (51291); link rows are never edited or deleted (51292).
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TPItemAppointmentLinks_SamePatient] ON [TreatmentPlanItemAppointmentLinks] AFTER INSERT AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i
               JOIN [TreatmentPlanItems] it ON it.[Id] = i.[ItemId]
               JOIN [Appointments] a ON a.[Id] = i.[AppointmentId]
               WHERE it.[PatientId] <> i.[PatientId] OR a.[PatientId] <> i.[PatientId] OR it.[PlanId] <> i.[PlanId])
        THROW 51291, 'A treatment-plan item can only be booked by an appointment of the same patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_TPItemAppointmentLinks_AppendOnly] ON [TreatmentPlanItemAppointmentLinks] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51292, 'The appointment-link history of a treatment-plan item is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [TreatmentPlanItemAppointmentLinks])
    THROW 51293, 'Treatment-plan items have been linked to appointments, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_TPItemAppointmentLinks_AppendOnly]");
            migrationBuilder.Sql("DROP TRIGGER [TR_TPItemAppointmentLinks_SamePatient]");

            migrationBuilder.DropTable(
                name: "TreatmentPlanItemAppointmentLinks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_ChangeType",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','ItemAdded','ItemWithdrawn','Renamed','Withdrawn','Revised','ReferenceCodeSet','ItemAccepted','ItemDeclined','PlanAccepted','PlanDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Item",
                table: "TreatmentPlanEvents",
                sql: "([ChangeType] IN ('ItemAdded','ItemWithdrawn','ItemAccepted','ItemDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted') AND [ItemId] IS NOT NULL) OR ([ChangeType] NOT IN ('ItemAdded','ItemWithdrawn','ItemAccepted','ItemDeclined','ItemScheduled','ItemUnscheduled','ItemCompleted') AND [ItemId] IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TreatmentPlanEvents_Reason",
                table: "TreatmentPlanEvents",
                sql: "[ChangeType] NOT IN ('ItemWithdrawn','Withdrawn','ItemDeclined','PlanDeclined','Revised') OR ([Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0)");
        }
    }
}
