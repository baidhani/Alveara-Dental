using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <summary>
    /// ALV-N021 A2, migration M3 (OD-22 to OD-31): the append-only VisitCheckoutOutcomes table (see VisitCheckoutModel for its checks). Triggers: an outcome is never updated or deleted
    /// (51282); a correction must supersede an earlier outcome of the same appointment and item (51283). Down refuses (51284) while any outcome exists, because checkout decisions are
    /// history and are never discarded to make a rollback succeed.
    /// </summary>
    public partial class AddVisitCheckoutOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisitCheckoutOutcomes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OutcomeCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LinkedLedgerEntryNumber = table.Column<int>(type: "int", nullable: true),
                    LinkedAppointmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SupersedesId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisitCheckoutOutcomes", x => x.Id);
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_Key", "LEN([IdempotencyKey]) > 0");
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_Ledger", "([OutcomeCode] IN ('payment_recorded','waived') AND [LinkedLedgerEntryNumber] IS NOT NULL AND [LinkedLedgerEntryNumber] > 0) OR ([OutcomeCode] NOT IN ('payment_recorded','waived') AND [LinkedLedgerEntryNumber] IS NULL)");
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_LinkedAppointment", "[LinkedAppointmentId] IS NULL OR ([ItemCode] = 'follow_up' AND [LinkedAppointmentId] <> [AppointmentId])");
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_Pair", "[ItemCode] COLLATE Latin1_General_CS_AS IN ('departure','follow_up','financial') AND (([ItemCode] = 'departure' AND [OutcomeCode] COLLATE Latin1_General_CS_AS IN ('patient_left')) OR ([ItemCode] = 'follow_up' AND [OutcomeCode] COLLATE Latin1_General_CS_AS IN ('not_clinically_required','patient_declined','deferred')) OR ([ItemCode] = 'financial' AND [OutcomeCode] COLLATE Latin1_General_CS_AS IN ('payment_recorded','waived','payment_plan','insurance_to_bill','send_statement','waiver_requested')))");
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_Reason", "([Reason] IS NULL OR LEN(REPLACE(REPLACE(REPLACE(REPLACE([Reason], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0) AND ([OutcomeCode] NOT IN ('deferred','waiver_requested') OR [Reason] IS NOT NULL)");
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_Supersedes", "[SupersedesId] IS NULL OR [SupersedesId] <> [Id]");
                    table.CheckConstraint("CK_VisitCheckoutOutcomes_Time", "[RecordedAtUtc] >= '2000-01-01' AND [RecordedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_VisitCheckoutOutcomes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitCheckoutOutcomes_Appointments_LinkedAppointmentId",
                        column: x => x.LinkedAppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitCheckoutOutcomes_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VisitCheckoutOutcomes_VisitCheckoutOutcomes_SupersedesId",
                        column: x => x.SupersedesId,
                        principalTable: "VisitCheckoutOutcomes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisitCheckoutOutcomes_ActorUserId_IdempotencyKey",
                table: "VisitCheckoutOutcomes",
                columns: new[] { "ActorUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisitCheckoutOutcomes_AppointmentId_ItemCode_RecordedAtUtc",
                table: "VisitCheckoutOutcomes",
                columns: new[] { "AppointmentId", "ItemCode", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VisitCheckoutOutcomes_LinkedAppointmentId",
                table: "VisitCheckoutOutcomes",
                column: "LinkedAppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_VisitCheckoutOutcomes_SupersedesId",
                table: "VisitCheckoutOutcomes",
                column: "SupersedesId",
                unique: true,
                filter: "[SupersedesId] IS NOT NULL");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_VisitCheckoutOutcomes_Immutable] ON [VisitCheckoutOutcomes] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51282, 'A checkout outcome is never changed or deleted; a correction is a new outcome that supersedes it.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_VisitCheckoutOutcomes_Supersedes] ON [VisitCheckoutOutcomes] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [VisitCheckoutOutcomes] o ON o.[Id] = i.[SupersedesId]
               WHERE o.[AppointmentId] <> i.[AppointmentId] OR o.[ItemCode] <> i.[ItemCode] OR o.[RecordedAtUtc] > i.[RecordedAtUtc])
        THROW 51283, 'A checkout correction supersedes an earlier outcome of the same visit and item.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [VisitCheckoutOutcomes])
    THROW 51284, 'Checkout outcomes have been recorded, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql("DROP TRIGGER [TR_VisitCheckoutOutcomes_Supersedes];");
            migrationBuilder.Sql("DROP TRIGGER [TR_VisitCheckoutOutcomes_Immutable];");

            migrationBuilder.DropTable(
                name: "VisitCheckoutOutcomes");
        }
    }
}
