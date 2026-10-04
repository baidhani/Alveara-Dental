using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientSafety : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clearances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReasonKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestedFrom = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DocumentReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosingReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clearances", x => x.Id);
                    table.CheckConstraint("CK_Clearances_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Medical','Dental')");
                    table.CheckConstraint("CK_Clearances_ReasonNotBlank", "LEN(LTRIM(RTRIM([Reason]))) > 0");
                    table.CheckConstraint("CK_Clearances_Stamps", "([Status] = 'Requested' AND [ReceivedAtUtc] IS NULL AND [ClosedAtUtc] IS NULL) OR ([Status] = 'Received' AND [ReceivedAtUtc] IS NOT NULL AND [ReceivedByUserId] IS NOT NULL AND [ClosedAtUtc] IS NULL) OR ([Status] = 'Resolved' AND [ReceivedAtUtc] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosingReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ClosingReason]))) > 0) OR ([Status] = 'Cancelled' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosingReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ClosingReason]))) > 0)");
                    table.CheckConstraint("CK_Clearances_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Requested','Received','Resolved','Cancelled')");
                    table.ForeignKey(
                        name: "FK_Clearances_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SafetyAlerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SourceItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolvedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResolutionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyAlerts", x => x.Id);
                    table.CheckConstraint("CK_SafetyAlerts_Category", "[Category] COLLATE Latin1_General_CS_AS IN ('Condition','Pregnancy','Anticoagulant','AdverseReaction','Custom')");
                    table.CheckConstraint("CK_SafetyAlerts_ResolvedStamp", "([Status] = 'Resolved' AND [ResolvedAtUtc] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL AND [ResolutionReason] IS NOT NULL AND LEN(LTRIM(RTRIM([ResolutionReason]))) > 0) OR ([Status] = 'Active' AND [ResolvedAtUtc] IS NULL AND [ResolvedByUserId] IS NULL AND [ResolutionReason] IS NULL)");
                    table.CheckConstraint("CK_SafetyAlerts_Severity", "[Severity] COLLATE Latin1_General_CS_AS IN ('Critical','High','Moderate','Low')");
                    table.CheckConstraint("CK_SafetyAlerts_SourceNotBlank", "LEN(LTRIM(RTRIM([SourceNote]))) > 0");
                    table.CheckConstraint("CK_SafetyAlerts_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Active','Resolved')");
                    table.CheckConstraint("CK_SafetyAlerts_TitleNotBlank", "LEN(LTRIM(RTRIM([Title]))) > 0");
                    table.ForeignKey(
                        name: "FK_SafetyAlerts_ClinicalRecordItems_SourceItemId",
                        column: x => x.SourceItemId,
                        principalTable: "ClinicalRecordItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SafetyAlerts_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClearanceVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClearanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedFrom = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DocumentReference = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearanceVersions", x => x.Id);
                    table.CheckConstraint("CK_ClearanceVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Requested','Received','DocumentAttached','Resolved','Cancelled')");
                    table.ForeignKey(
                        name: "FK_ClearanceVersions_Clearances_ClearanceId",
                        column: x => x.ClearanceId,
                        principalTable: "Clearances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SafetyAlertAcknowledgements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    AcknowledgedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyAlertAcknowledgements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SafetyAlertAcknowledgements_SafetyAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "SafetyAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SafetyAlertVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AlertId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SafetyAlertVersions", x => x.Id);
                    table.CheckConstraint("CK_SafetyAlertVersions_ChangeType", "[ChangeType] COLLATE Latin1_General_CS_AS IN ('Created','Changed','Resolved','Reopened')");
                    table.ForeignKey(
                        name: "FK_SafetyAlertVersions_SafetyAlerts_AlertId",
                        column: x => x.AlertId,
                        principalTable: "SafetyAlerts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clearances_PatientId_Kind_ReasonKey",
                table: "Clearances",
                columns: new[] { "PatientId", "Kind", "ReasonKey" },
                unique: true,
                filter: "[Status] IN ('Requested','Received')");

            migrationBuilder.CreateIndex(
                name: "IX_Clearances_PatientId_Status",
                table: "Clearances",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClearanceVersions_ClearanceId_VersionNumber",
                table: "ClearanceVersions",
                columns: new[] { "ClearanceId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SafetyAlertAcknowledgements_AlertId_UserId_Revision",
                table: "SafetyAlertAcknowledgements",
                columns: new[] { "AlertId", "UserId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SafetyAlertAcknowledgements_PatientId",
                table: "SafetyAlertAcknowledgements",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyAlerts_PatientId_Category_Title",
                table: "SafetyAlerts",
                columns: new[] { "PatientId", "Category", "Title" },
                unique: true,
                filter: "[Status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyAlerts_PatientId_Status",
                table: "SafetyAlerts",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SafetyAlerts_SourceItemId",
                table: "SafetyAlerts",
                column: "SourceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SafetyAlertVersions_AlertId_VersionNumber",
                table: "SafetyAlertVersions",
                columns: new[] { "AlertId", "VersionNumber" },
                unique: true);


            // ALV-N011: the database refuses, independently of the application, to delete a safety alert or a clearance (they are resolved or cancelled, with history), to change
            // the history of either, and to change or delete an acknowledgement.
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_SafetyAlerts_NoDelete] ON [SafetyAlerts] INSTEAD OF DELETE AS
BEGIN
    THROW 51050, 'Safety alerts are never deleted; they are resolved with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_SafetyAlertVersions_Immutable] ON [SafetyAlertVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51051, 'The history of a safety alert is append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_SafetyAlertAcks_Immutable] ON [SafetyAlertAcknowledgements] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51052, 'Safety alert acknowledgements are append-only.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Clearances_NoDelete] ON [Clearances] INSTEAD OF DELETE AS
BEGIN
    THROW 51053, 'Clearances are never deleted; they are resolved or cancelled with a reason.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ClearanceVersions_Immutable] ON [ClearanceVersions] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51054, 'The history of a clearance is append-only.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearanceVersions");

            migrationBuilder.DropTable(
                name: "SafetyAlertAcknowledgements");

            migrationBuilder.DropTable(
                name: "SafetyAlertVersions");

            migrationBuilder.DropTable(
                name: "Clearances");

            migrationBuilder.DropTable(
                name: "SafetyAlerts");
        }
    }
}
