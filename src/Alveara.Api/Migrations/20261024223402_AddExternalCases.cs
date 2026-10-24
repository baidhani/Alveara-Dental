using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Kind",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Source",
                table: "FollowUpTasks");

            migrationBuilder.AddColumn<Guid>(
                name: "SourceLabCaseId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SourceReferralCaseId",
                table: "FollowUpTasks",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExternalParties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContactDetails = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalParties", x => x.Id);
                    table.CheckConstraint("CK_ExternalParties_Contact", "[ContactDetails] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([ContactDetails], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [ContactDetails] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [ContactDetails] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_ExternalParties_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('ReferralProvider','LabVendor')");
                    table.CheckConstraint("CK_ExternalParties_Name", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Name], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Name] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Name] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_ExternalParties_Times", "[CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND [UpdatedAtUtc] >= [CreatedAtUtc]");
                    table.ForeignKey(
                        name: "FK_ExternalParties_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LabCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    SentOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    TreatmentPlanItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Material = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Shade = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosureNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabCases", x => x.Id);
                    table.CheckConstraint("CK_LabCases_ClosureNote", "[ClosureNote] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([ClosureNote], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [ClosureNote] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [ClosureNote] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_LabCases_Dates", "([SentOn] IS NULL OR ([SentOn] >= '2000-01-01' AND [SentOn] < '2200-01-01')) AND ([ReceivedOn] IS NULL OR ([ReceivedOn] >= '2000-01-01' AND [ReceivedOn] < '2200-01-01')) AND ([DueOn] IS NULL OR ([DueOn] >= '2000-01-01' AND [DueOn] < '2200-01-01')) AND ([ReceivedOn] IS NULL OR [SentOn] IS NOT NULL) AND ([ReceivedOn] IS NULL OR [ReceivedOn] >= [SentOn]) AND [CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND [UpdatedAtUtc] >= [CreatedAtUtc] AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [CreatedAtUtc])");
                    table.CheckConstraint("CK_LabCases_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_LabCases_Material", "[Material] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Material], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Material] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Material] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_LabCases_Notes", "[Notes] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Notes], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Notes] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [Notes] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_LabCases_Shade", "[Shade] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Shade], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Shade] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Shade] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_LabCases_Shape", "(([Status] = 'Draft' AND [SentOn] IS NULL AND [ReceivedOn] IS NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] IN ('Sent','InProgress') AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] = 'Received' AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NOT NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] = 'Completed' AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL) OR ([Status] = 'Cancelled' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosureNote] IS NOT NULL))");
                    table.CheckConstraint("CK_LabCases_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Draft','Sent','InProgress','Received','Completed','Cancelled')");
                    table.CheckConstraint("CK_LabCases_Title", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Title], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Title] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Title] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_LabCases_ToothKey", "[ToothKey] IS NULL OR [ToothKey] COLLATE Latin1_General_CS_AS IN ('11','12','13','14','15','16','17','18','21','22','23','24','25','26','27','28','31','32','33','34','35','36','37','38','41','42','43','44','45','46','47','48','51','52','53','54','55','61','62','63','64','65','71','72','73','74','75','81','82','83','84','85')");
                    table.ForeignKey(
                        name: "FK_LabCases_ExternalParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "ExternalParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabCases_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabCases_TreatmentPlanItems_TreatmentPlanItemId",
                        column: x => x.TreatmentPlanItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabCases_UserAccounts_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LabCases_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReferralCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    SentOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReceivedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClosureNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReferralCases", x => x.Id);
                    table.CheckConstraint("CK_ReferralCases_ClosureNote", "[ClosureNote] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([ClosureNote], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [ClosureNote] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [ClosureNote] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_ReferralCases_Dates", "([SentOn] IS NULL OR ([SentOn] >= '2000-01-01' AND [SentOn] < '2200-01-01')) AND ([ReceivedOn] IS NULL OR ([ReceivedOn] >= '2000-01-01' AND [ReceivedOn] < '2200-01-01')) AND ([DueOn] IS NULL OR ([DueOn] >= '2000-01-01' AND [DueOn] < '2200-01-01')) AND ([ReceivedOn] IS NULL OR [SentOn] IS NOT NULL) AND ([ReceivedOn] IS NULL OR [ReceivedOn] >= [SentOn]) AND [CreatedAtUtc] >= '2000-01-01' AND [CreatedAtUtc] < '2200-01-01' AND [UpdatedAtUtc] >= [CreatedAtUtc] AND ([ClosedAtUtc] IS NULL OR [ClosedAtUtc] >= [CreatedAtUtc])");
                    table.CheckConstraint("CK_ReferralCases_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_ReferralCases_Notes", "[Notes] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Notes], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Notes] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [Notes] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_ReferralCases_Reason", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Reason], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Reason] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_ReferralCases_Shape", "(([Status] = 'Draft' AND [SentOn] IS NULL AND [ReceivedOn] IS NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] IN ('Sent','InProgress') AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] = 'Received' AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NOT NULL AND [ClosedAtUtc] IS NULL AND [ClosedByUserId] IS NULL AND [ClosureNote] IS NULL) OR ([Status] = 'Completed' AND [SentOn] IS NOT NULL AND [ReceivedOn] IS NOT NULL AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL) OR ([Status] = 'Cancelled' AND [ClosedAtUtc] IS NOT NULL AND [ClosedByUserId] IS NOT NULL AND [ClosureNote] IS NOT NULL))");
                    table.CheckConstraint("CK_ReferralCases_Status", "[Status] COLLATE Latin1_General_CS_AS IN ('Draft','Sent','Received','Completed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_ReferralCases_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralCases_ExternalParties_PartyId",
                        column: x => x.PartyId,
                        principalTable: "ExternalParties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralCases_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralCases_UserAccounts_ClosedByUserId",
                        column: x => x.ClosedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReferralCases_UserAccounts_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalCaseDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LabCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LinkedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalCaseDocuments", x => x.Id);
                    table.CheckConstraint("CK_ExternalCaseDocuments_Case", "([ReferralCaseId] IS NOT NULL AND [LabCaseId] IS NULL) OR ([ReferralCaseId] IS NULL AND [LabCaseId] IS NOT NULL)");
                    table.CheckConstraint("CK_ExternalCaseDocuments_Time", "[LinkedAtUtc] >= '2000-01-01' AND [LinkedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_ExternalCaseDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCaseDocuments_LabCases_LabCaseId",
                        column: x => x.LabCaseId,
                        principalTable: "LabCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCaseDocuments_ReferralCases_ReferralCaseId",
                        column: x => x.ReferralCaseId,
                        principalTable: "ReferralCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCaseDocuments_UserAccounts_LinkedByUserId",
                        column: x => x.LinkedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExternalCaseEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReferralCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LabCaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    ToStatus = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: true),
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalCaseEvents", x => x.Id);
                    table.CheckConstraint("CK_ExternalCaseEvents_Case", "([ReferralCaseId] IS NOT NULL AND [LabCaseId] IS NULL) OR ([ReferralCaseId] IS NULL AND [LabCaseId] IS NOT NULL)");
                    table.CheckConstraint("CK_ExternalCaseEvents_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Created','Edited','StatusChanged','DocumentLinked','DocumentUnlinked')");
                    table.CheckConstraint("CK_ExternalCaseEvents_Note", "[Note] IS NULL OR (LEN(REPLACE(REPLACE(REPLACE(REPLACE([Note], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Note] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(8) + NCHAR(11) + NCHAR(12) + NCHAR(14) + N'-' + NCHAR(31) + N']%', [Note] COLLATE Latin1_General_BIN2) = 0)");
                    table.CheckConstraint("CK_ExternalCaseEvents_Shape", "([Kind] = 'StatusChanged' AND [ToStatus] IS NOT NULL AND [DocumentId] IS NULL) OR ([Kind] IN ('DocumentLinked','DocumentUnlinked') AND [DocumentId] IS NOT NULL AND [FromStatus] IS NULL AND [ToStatus] IS NULL) OR ([Kind] IN ('Created','Edited') AND [DocumentId] IS NULL AND [FromStatus] IS NULL AND ([ToStatus] IS NULL OR [Kind] = 'Created'))");
                    table.CheckConstraint("CK_ExternalCaseEvents_Time", "[OccurredAtUtc] >= '2000-01-01' AND [OccurredAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_ExternalCaseEvents_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCaseEvents_LabCases_LabCaseId",
                        column: x => x.LabCaseId,
                        principalTable: "LabCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCaseEvents_ReferralCases_ReferralCaseId",
                        column: x => x.ReferralCaseId,
                        principalTable: "ReferralCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExternalCaseEvents_UserAccounts_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_SourceLabCaseId",
                table: "FollowUpTasks",
                column: "SourceLabCaseId",
                filter: "[SourceLabCaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_SourceReferralCaseId",
                table: "FollowUpTasks",
                column: "SourceReferralCaseId",
                filter: "[SourceReferralCaseId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Kind",
                table: "FollowUpTasks",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Manual','Recall','UnscheduledTreatment','ReferralFollowUp','LabCaseDue')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Source",
                table: "FollowUpTasks",
                sql: "([Kind] = 'Manual' AND [SourcePlanItemId] IS NULL AND [SourceReferralCaseId] IS NULL AND [SourceLabCaseId] IS NULL AND [SourceKey] IS NULL) OR ([Kind] = 'Recall' AND [SourcePlanItemId] IS NULL AND [SourceReferralCaseId] IS NULL AND [SourceLabCaseId] IS NULL AND [SourceKey] IS NOT NULL) OR ([Kind] = 'UnscheduledTreatment' AND [SourcePlanItemId] IS NOT NULL AND [SourceReferralCaseId] IS NULL AND [SourceLabCaseId] IS NULL AND [SourceKey] IS NOT NULL) OR ([Kind] = 'ReferralFollowUp' AND [SourcePlanItemId] IS NULL AND [SourceReferralCaseId] IS NOT NULL AND [SourceLabCaseId] IS NULL AND [SourceKey] IS NOT NULL) OR ([Kind] = 'LabCaseDue' AND [SourcePlanItemId] IS NULL AND [SourceReferralCaseId] IS NULL AND [SourceLabCaseId] IS NOT NULL AND [SourceKey] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseDocuments_DocumentId",
                table: "ExternalCaseDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseDocuments_LabCaseId_DocumentId",
                table: "ExternalCaseDocuments",
                columns: new[] { "LabCaseId", "DocumentId" },
                unique: true,
                filter: "[LabCaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseDocuments_LinkedByUserId",
                table: "ExternalCaseDocuments",
                column: "LinkedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseDocuments_ReferralCaseId_DocumentId",
                table: "ExternalCaseDocuments",
                columns: new[] { "ReferralCaseId", "DocumentId" },
                unique: true,
                filter: "[ReferralCaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseEvents_ActorUserId",
                table: "ExternalCaseEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseEvents_DocumentId",
                table: "ExternalCaseEvents",
                column: "DocumentId",
                filter: "[DocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseEvents_LabCaseId_OccurredAtUtc",
                table: "ExternalCaseEvents",
                columns: new[] { "LabCaseId", "OccurredAtUtc" },
                filter: "[LabCaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalCaseEvents_ReferralCaseId_OccurredAtUtc",
                table: "ExternalCaseEvents",
                columns: new[] { "ReferralCaseId", "OccurredAtUtc" },
                filter: "[ReferralCaseId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalParties_CreatedByUserId",
                table: "ExternalParties",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalParties_Kind_Name",
                table: "ExternalParties",
                columns: new[] { "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_ClosedByUserId",
                table: "LabCases",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_CreatedByUserId",
                table: "LabCases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_PartyId",
                table: "LabCases",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_PatientId_IdempotencyKey",
                table: "LabCases",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_PatientId_Status",
                table: "LabCases",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_Status_DueOn",
                table: "LabCases",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_LabCases_TreatmentPlanItemId",
                table: "LabCases",
                column: "TreatmentPlanItemId",
                filter: "[TreatmentPlanItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_ClosedByUserId",
                table: "ReferralCases",
                column: "ClosedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_CreatedByUserId",
                table: "ReferralCases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_EncounterId",
                table: "ReferralCases",
                column: "EncounterId",
                filter: "[EncounterId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_PartyId",
                table: "ReferralCases",
                column: "PartyId");

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_PatientId_IdempotencyKey",
                table: "ReferralCases",
                columns: new[] { "PatientId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_PatientId_Status",
                table: "ReferralCases",
                columns: new[] { "PatientId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReferralCases_Status_DueOn",
                table: "ReferralCases",
                columns: new[] { "Status", "DueOn" });

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUpTasks_LabCases_SourceLabCaseId",
                table: "FollowUpTasks",
                column: "SourceLabCaseId",
                principalTable: "LabCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUpTasks_ReferralCases_SourceReferralCaseId",
                table: "FollowUpTasks",
                column: "SourceReferralCaseId",
                principalTable: "ReferralCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ALTER the follow-up triggers for the case links; CREATE the guards of the new tables (errors 51220 to 51224)
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Guard] ON [FollowUpTasks] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51210, 'A follow-up task is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] <> 'Open')
        THROW 51210, 'A completed or cancelled follow-up task is final and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[Kind] <> d.[Kind]
                  OR ISNULL(i.[CreatedByUserId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[CreatedByUserId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourcePlanItemId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SourcePlanItemId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourceReferralCaseId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SourceReferralCaseId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourceLabCaseId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SourceLabCaseId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourceKey], N'') <> ISNULL(d.[SourceKey], N''))
        THROW 51210, 'A follow-up task keeps its patient, creator, creation time, kind, source and recall link.', 1;
END");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Links] ON [FollowUpTasks] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[AssigneeUserId]
               WHERE u.[IsDisabled] = 1 AND i.[Status] = 'Open'
                 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[AssigneeUserId] = i.[AssigneeUserId]))
        THROW 51211, 'An open follow-up task is assigned to an enabled account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [FollowUpTasks] p ON p.[Id] = i.[RecallOfTaskId] WHERE p.[PatientId] <> i.[PatientId])
        THROW 51211, 'A recall belongs to the patient of the task it follows.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Appointments] a ON a.[Id] = i.[AppointmentId] WHERE a.[PatientId] <> i.[PatientId])
        THROW 51211, 'A follow-up task links an appointment of its own patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] x ON x.[Id] = i.[SourcePlanItemId] WHERE x.[PatientId] <> i.[PatientId])
        THROW 51211, 'A follow-up task names a treatment-plan item of its own patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ReferralCases] c ON c.[Id] = i.[SourceReferralCaseId] WHERE c.[PatientId] <> i.[PatientId])
       OR EXISTS (SELECT 1 FROM inserted i JOIN [LabCases] c ON c.[Id] = i.[SourceLabCaseId] WHERE c.[PatientId] <> i.[PatientId])
        THROW 51224, 'A follow-up task names a referral or lab case of its own patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ExternalParties_Guard] ON [ExternalParties] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51220, 'An outside party is never deleted; make it inactive instead.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[Kind] <> d.[Kind] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[CreatedByUserId] <> d.[CreatedByUserId])
        THROW 51220, 'An outside party keeps its kind, creator and creation time.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ReferralCases_Guard] ON [ReferralCases] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51221, 'A referral case is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] IN ('Completed','Cancelled'))
        THROW 51221, 'A completed or cancelled referral case is final and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreatedByUserId] <> d.[CreatedByUserId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[IdempotencyKey] <> d.[IdempotencyKey])
        THROW 51221, 'A referral case keeps its patient, creator, creation time and idempotency key.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[PartyId] <> d.[PartyId] AND d.[Status] <> 'Draft')
        THROW 51221, 'A referral case keeps its outside party once it has been sent.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ReferralCases_Links] ON [ReferralCases] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ExternalParties] p ON p.[Id] = i.[PartyId] WHERE p.[Kind] <> 'ReferralProvider')
        THROW 51222, 'A referral case names an outside party of the matching kind.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ExternalParties] p ON p.[Id] = i.[PartyId]
               WHERE p.[IsActive] = 0 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[PartyId] = i.[PartyId]))
        THROW 51222, 'A new referral case names an active outside party.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e ON e.[Id] = i.[EncounterId] WHERE e.[PatientId] <> i.[PatientId])
        THROW 51222, 'A referral names an encounter of its own patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_LabCases_Guard] ON [LabCases] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51221, 'A lab case is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] IN ('Completed','Cancelled'))
        THROW 51221, 'A completed or cancelled lab case is final and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreatedByUserId] <> d.[CreatedByUserId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[IdempotencyKey] <> d.[IdempotencyKey])
        THROW 51221, 'A lab case keeps its patient, creator, creation time and idempotency key.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE i.[PartyId] <> d.[PartyId] AND d.[Status] <> 'Draft')
        THROW 51221, 'A lab case keeps its outside party once it has been sent.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_LabCases_Links] ON [LabCases] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ExternalParties] p ON p.[Id] = i.[PartyId] WHERE p.[Kind] <> 'LabVendor')
        THROW 51222, 'A lab case names an outside party of the matching kind.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ExternalParties] p ON p.[Id] = i.[PartyId]
               WHERE p.[IsActive] = 0 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[PartyId] = i.[PartyId]))
        THROW 51222, 'A new lab case names an active outside party.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] x ON x.[Id] = i.[TreatmentPlanItemId] WHERE x.[PatientId] <> i.[PatientId])
        THROW 51222, 'A lab case names a treatment-plan item of its own patient.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ExternalCaseEvents_Immutable] ON [ExternalCaseEvents] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51223, 'A referral or lab case event is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_ExternalCaseDocuments_Links] ON [ExternalCaseDocuments] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Documents] d ON d.[Id] = i.[DocumentId]
               LEFT JOIN [ReferralCases] r ON r.[Id] = i.[ReferralCaseId] LEFT JOIN [LabCases] l ON l.[Id] = i.[LabCaseId]
               WHERE d.[PatientId] <> COALESCE(r.[PatientId], l.[PatientId]))
        THROW 51222, 'A case links a document of its own patient.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no referral, lab case, party, case event or document link exists and no task chases a case
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [ExternalParties]) OR EXISTS (SELECT 1 FROM [ReferralCases]) OR EXISTS (SELECT 1 FROM [LabCases])
   OR EXISTS (SELECT 1 FROM [ExternalCaseEvents]) OR EXISTS (SELECT 1 FROM [ExternalCaseDocuments])
   OR EXISTS (SELECT 1 FROM [FollowUpTasks] WHERE [Kind] IN ('ReferralFollowUp','LabCaseDue'))
    THROW 51225, 'Referral and lab case data exists, so this migration cannot be rolled back.', 1;");
            // the follow-up triggers go back to the work-queue versions before the columns they mention are dropped
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Guard] ON [FollowUpTasks] AFTER UPDATE, DELETE AS
BEGIN
    IF EXISTS (SELECT 1 FROM deleted) AND NOT EXISTS (SELECT 1 FROM inserted)
        THROW 51210, 'A follow-up task is never deleted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id] WHERE d.[Status] <> 'Open')
        THROW 51210, 'A completed or cancelled follow-up task is final and never changes.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.[Id] = i.[Id]
               WHERE i.[PatientId] <> d.[PatientId] OR i.[CreatedAtUtc] <> d.[CreatedAtUtc] OR i.[Kind] <> d.[Kind]
                  OR ISNULL(i.[CreatedByUserId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[CreatedByUserId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[RecallOfTaskId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourcePlanItemId], '00000000-0000-0000-0000-000000000000') <> ISNULL(d.[SourcePlanItemId], '00000000-0000-0000-0000-000000000000')
                  OR ISNULL(i.[SourceKey], N'') <> ISNULL(d.[SourceKey], N''))
        THROW 51210, 'A follow-up task keeps its patient, creator, creation time, kind, source and recall link.', 1;
END");
            migrationBuilder.Sql(@"ALTER TRIGGER [TR_FollowUpTasks_Links] ON [FollowUpTasks] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [UserAccounts] u ON u.[Id] = i.[AssigneeUserId]
               WHERE u.[IsDisabled] = 1 AND i.[Status] = 'Open'
                 AND NOT EXISTS (SELECT 1 FROM deleted d WHERE d.[Id] = i.[Id] AND d.[AssigneeUserId] = i.[AssigneeUserId]))
        THROW 51211, 'An open follow-up task is assigned to an enabled account.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [FollowUpTasks] p ON p.[Id] = i.[RecallOfTaskId] WHERE p.[PatientId] <> i.[PatientId])
        THROW 51211, 'A recall belongs to the patient of the task it follows.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Appointments] a ON a.[Id] = i.[AppointmentId] WHERE a.[PatientId] <> i.[PatientId])
        THROW 51211, 'A follow-up task links an appointment of its own patient.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] x ON x.[Id] = i.[SourcePlanItemId] WHERE x.[PatientId] <> i.[PatientId])
        THROW 51211, 'A follow-up task names a treatment-plan item of its own patient.', 1;
END");
            migrationBuilder.DropForeignKey(
                name: "FK_FollowUpTasks_LabCases_SourceLabCaseId",
                table: "FollowUpTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_FollowUpTasks_ReferralCases_SourceReferralCaseId",
                table: "FollowUpTasks");

            migrationBuilder.DropTable(
                name: "ExternalCaseDocuments");

            migrationBuilder.DropTable(
                name: "ExternalCaseEvents");

            migrationBuilder.DropTable(
                name: "LabCases");

            migrationBuilder.DropTable(
                name: "ReferralCases");

            migrationBuilder.DropTable(
                name: "ExternalParties");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_SourceLabCaseId",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_SourceReferralCaseId",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Kind",
                table: "FollowUpTasks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FollowUpTasks_Source",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "SourceLabCaseId",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "SourceReferralCaseId",
                table: "FollowUpTasks");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Kind",
                table: "FollowUpTasks",
                sql: "[Kind] COLLATE Latin1_General_CS_AS IN ('Manual','Recall','UnscheduledTreatment')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FollowUpTasks_Source",
                table: "FollowUpTasks",
                sql: "([Kind] = 'Manual' AND [SourcePlanItemId] IS NULL AND [SourceKey] IS NULL) OR ([Kind] = 'Recall' AND [SourcePlanItemId] IS NULL AND [SourceKey] IS NOT NULL) OR ([Kind] = 'UnscheduledTreatment' AND [SourcePlanItemId] IS NOT NULL AND [SourceKey] IS NOT NULL)");
        }
    }
}
