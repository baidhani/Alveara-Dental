using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompletedProcedures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompletionSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanRevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ItemCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompletionSets", x => x.Id);
                    table.CheckConstraint("CK_CompletionSets_Counts", "[ItemCount] >= 1 AND [PlanRevisionNumber] >= 1");
                    table.CheckConstraint("CK_CompletionSets_Hash", "LEN([RequestHash]) = 64 AND [RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_CompletionSets_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.ForeignKey(
                        name: "FK_CompletionSets_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletionSets_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompletedProcedures",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlanRevisionNumber = table.Column<int>(type: "int", nullable: false),
                    ProcedureVersionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToothKey = table.Column<string>(type: "nchar(2)", fixedLength: true, maxLength: 2, nullable: true),
                    Surface = table.Column<string>(type: "nchar(1)", fixedLength: true, maxLength: 1, nullable: true),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ToothFindingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ChartOutcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FinalizedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FinalizedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompletedProcedures", x => x.Id);
                    table.CheckConstraint("CK_CompletedProcedures_Chart", "[ChartOutcome] COLLATE Latin1_General_CS_AS IN ('Updated','AlreadyCompleted','NotApplicable') AND (([ChartOutcome] = 'NotApplicable' AND [ToothFindingId] IS NULL) OR ([ChartOutcome] <> 'NotApplicable' AND [ToothFindingId] IS NOT NULL))");
                    table.CheckConstraint("CK_CompletedProcedures_Performed", "[PerformedAtUtc] >= '2000-01-01' AND [PerformedAtUtc] < '2200-01-01'");
                    table.CheckConstraint("CK_CompletedProcedures_Revision", "[PlanRevisionNumber] >= 1");
                    table.CheckConstraint("CK_CompletedProcedures_Surface", "[Surface] IS NULL OR [ToothKey] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_CompletionSets_SetId",
                        column: x => x.SetId,
                        principalTable: "CompletionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_ProcedureVersions_ProcedureVersionId",
                        column: x => x.ProcedureVersionId,
                        principalTable: "ProcedureVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_ToothFindings_ToothFindingId",
                        column: x => x.ToothFindingId,
                        principalTable: "ToothFindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_TreatmentPlanItems_PlanItemId",
                        column: x => x.PlanItemId,
                        principalTable: "TreatmentPlanItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedures_TreatmentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TreatmentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CompletedProcedureAmendments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompletedProcedureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AmendmentNumber = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RequestHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ProviderProfileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PerformedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AmendedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AmendedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompletedProcedureAmendments", x => x.Id);
                    table.CheckConstraint("CK_CompletedProcedureAmendments_Changes", "[ProviderProfileId] IS NOT NULL OR [PerformedAtUtc] IS NOT NULL OR [EncounterId] IS NOT NULL");
                    table.CheckConstraint("CK_CompletedProcedureAmendments_Hash", "LEN([RequestHash]) = 64 AND [RequestHash] COLLATE Latin1_General_BIN2 NOT LIKE '%[^0-9a-f]%'");
                    table.CheckConstraint("CK_CompletedProcedureAmendments_Key", "LEN(LTRIM(RTRIM([IdempotencyKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [IdempotencyKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_CompletedProcedureAmendments_Number", "[AmendmentNumber] >= 1");
                    table.CheckConstraint("CK_CompletedProcedureAmendments_Performed", "[PerformedAtUtc] IS NULL OR ([PerformedAtUtc] >= '2000-01-01' AND [PerformedAtUtc] < '2200-01-01')");
                    table.CheckConstraint("CK_CompletedProcedureAmendments_Reason", "LEN(LTRIM(RTRIM([Reason]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0");
                    table.ForeignKey(
                        name: "FK_CompletedProcedureAmendments_CompletedProcedures_CompletedProcedureId",
                        column: x => x.CompletedProcedureId,
                        principalTable: "CompletedProcedures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedureAmendments_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedureAmendments_Patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompletedProcedureAmendments_ProviderProfiles_ProviderProfileId",
                        column: x => x.ProviderProfileId,
                        principalTable: "ProviderProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedureAmendments_CompletedProcedureId_AmendmentNumber",
                table: "CompletedProcedureAmendments",
                columns: new[] { "CompletedProcedureId", "AmendmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedureAmendments_CompletedProcedureId_IdempotencyKey",
                table: "CompletedProcedureAmendments",
                columns: new[] { "CompletedProcedureId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedureAmendments_EncounterId",
                table: "CompletedProcedureAmendments",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedureAmendments_PatientId",
                table: "CompletedProcedureAmendments",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedureAmendments_ProviderProfileId",
                table: "CompletedProcedureAmendments",
                column: "ProviderProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_EncounterId",
                table: "CompletedProcedures",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_PatientId_PerformedAtUtc",
                table: "CompletedProcedures",
                columns: new[] { "PatientId", "PerformedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_PlanId",
                table: "CompletedProcedures",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_PlanItemId",
                table: "CompletedProcedures",
                column: "PlanItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_ProcedureVersionId",
                table: "CompletedProcedures",
                column: "ProcedureVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_ProviderProfileId",
                table: "CompletedProcedures",
                column: "ProviderProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_SetId",
                table: "CompletedProcedures",
                column: "SetId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletedProcedures_ToothFindingId",
                table: "CompletedProcedures",
                column: "ToothFindingId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionSets_CorrelationId",
                table: "CompletionSets",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionSets_PatientId",
                table: "CompletionSets",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "IX_CompletionSets_PlanId_IdempotencyKey",
                table: "CompletionSets",
                columns: new[] { "PlanId", "IdempotencyKey" },
                unique: true);

            // ---- ALV-008-C01 triggers: what no single-row check can see (error numbers 51144-51156; 51157 and up are free) ----
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletionSets_Immutable] ON [CompletionSets] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51144, 'A completion set is append-only; it is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletedProcedures_Immutable] ON [CompletedProcedures] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51145, 'A completed procedure is finalized when it is created; correct it with an amendment, never by changing or deleting it.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletedProcedureAmendments_Immutable] ON [CompletedProcedureAmendments] INSTEAD OF UPDATE, DELETE AS
BEGIN
    THROW 51146, 'An amendment is append-only; record a new amendment instead.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletionSets_Links] ON [CompletionSets] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE p.PatientId <> i.PatientId)
        THROW 51156, 'A completion set carries the patient of its plan.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletedProcedures_Links] ON [CompletedProcedures] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [CompletionSets] s ON s.Id = i.SetId JOIN [TreatmentPlanItems] it ON it.Id = i.PlanItemId
               WHERE s.PlanId <> i.PlanId OR s.PatientId <> i.PatientId OR s.PlanRevisionNumber <> i.PlanRevisionNumber OR it.PlanId <> i.PlanId OR it.PatientId <> i.PatientId)
        THROW 51147, 'A completed procedure carries the plan, patient and revision of its completion set and of its plan item.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] it ON it.Id = i.PlanItemId
               WHERE it.ProcedureVersionId <> i.ProcedureVersionId OR ISNULL(it.ToothKey, '') <> ISNULL(i.ToothKey, '') OR ISNULL(it.Surface, '') <> ISNULL(i.Surface, ''))
        THROW 51148, 'A completed procedure carries the catalog version, tooth and surface of its plan item.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e ON e.Id = i.EncounterId WHERE e.PatientId <> i.PatientId)
        THROW 51149, 'The encounter of a completed procedure must be the patient''s own.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [ToothFindings] f ON f.Id = i.ToothFindingId
               WHERE f.PatientId <> i.PatientId OR f.ToothKey <> i.ToothKey OR i.ToothKey IS NULL OR (i.Surface IS NOT NULL AND ISNULL(f.Surface, '') <> i.Surface))
        THROW 51149, 'The chart finding of a completed procedure must be the patient''s own, on the procedure''s tooth and surface.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlanItems] it ON it.Id = i.PlanItemId
               WHERE it.WithdrawnAtUtc IS NOT NULL OR NOT EXISTS (SELECT 1 FROM [TreatmentPlanItemStates] s WHERE s.ItemId = i.PlanItemId AND s.Completion = 'Completed'))
        THROW 51150, 'Only a completed plan procedure that has not been withdrawn can be recorded as a completed procedure.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [TreatmentPlans] p ON p.Id = i.PlanId WHERE i.PerformedAtUtc < p.CreatedAtUtc)
        THROW 51153, 'A procedure cannot be performed before its plan was created.', 1;
    IF EXISTS (SELECT 1 FROM inserted i
               CROSS APPLY (SELECT MAX(s.RecordedAtUtc) AS AcceptedAt FROM [TreatmentPlanItemStates] s
                            WHERE s.ItemId = i.PlanItemId AND s.Acceptance = 'Accepted' AND s.Completion = 'NotStarted'
                              AND NOT EXISTS (SELECT 1 FROM [TreatmentPlanItemStates] q WHERE q.ItemId = s.ItemId AND q.Sequence = s.Sequence - 1 AND q.Acceptance = 'Accepted')) a
               WHERE a.AcceptedAt IS NOT NULL AND i.PerformedAtUtc < a.AcceptedAt)
        THROW 51154, 'A procedure cannot be performed before it was accepted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_CompletedProcedureAmendments_Links] ON [CompletedProcedureAmendments] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [CompletedProcedures] cp ON cp.Id = i.CompletedProcedureId WHERE cp.PatientId <> i.PatientId)
        THROW 51151, 'An amendment carries the patient of the completed procedure it corrects.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [Encounters] e ON e.Id = i.EncounterId WHERE e.PatientId <> i.PatientId)
        THROW 51151, 'The encounter of an amendment must be the patient''s own.', 1;
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.AmendmentNumber <> 1 + (SELECT COUNT(*) FROM [CompletedProcedureAmendments] a WHERE a.CompletedProcedureId = i.CompletedProcedureId AND a.AmendmentNumber < i.AmendmentNumber))
        THROW 51152, 'Amendments are numbered 1, 2, 3 for a completed procedure, with no gaps.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [CompletedProcedures] cp ON cp.Id = i.CompletedProcedureId JOIN [TreatmentPlans] p ON p.Id = cp.PlanId
               WHERE i.PerformedAtUtc IS NOT NULL AND i.PerformedAtUtc < p.CreatedAtUtc)
        THROW 51155, 'A procedure cannot be amended to a time before its plan was created or before it was accepted.', 1;
    IF EXISTS (SELECT 1 FROM inserted i JOIN [CompletedProcedures] cp ON cp.Id = i.CompletedProcedureId
               CROSS APPLY (SELECT MAX(s.RecordedAtUtc) AS AcceptedAt FROM [TreatmentPlanItemStates] s
                            WHERE s.ItemId = cp.PlanItemId AND s.Acceptance = 'Accepted' AND s.Completion = 'NotStarted'
                              AND NOT EXISTS (SELECT 1 FROM [TreatmentPlanItemStates] q WHERE q.ItemId = s.ItemId AND q.Sequence = s.Sequence - 1 AND q.Acceptance = 'Accepted')) a
               WHERE i.PerformedAtUtc IS NOT NULL AND a.AcceptedAt IS NOT NULL AND i.PerformedAtUtc < a.AcceptedAt)
        THROW 51155, 'A procedure cannot be amended to a time before its plan was created or before it was accepted.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompletedProcedureAmendments");

            migrationBuilder.DropTable(
                name: "CompletedProcedures");

            migrationBuilder.DropTable(
                name: "CompletionSets");
        }
    }
}
