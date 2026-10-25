using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Alveara.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientMerges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MergedAtUtc",
                table: "Patients",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PatientMerges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergeKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SourcePatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurvivorPatientId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientMerges", x => x.Id);
                    table.CheckConstraint("CK_PatientMerges_Distinct", "[SourcePatientId] <> [SurvivorPatientId]");
                    table.CheckConstraint("CK_PatientMerges_Key", "LEN(LTRIM(RTRIM([MergeKey]))) > 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [MergeKey] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PatientMerges_Reason", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Reason], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Reason] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Reason] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PatientMerges_Time", "[MergedAtUtc] >= '2000-01-01' AND [MergedAtUtc] < '2200-01-01'");
                    table.ForeignKey(
                        name: "FK_PatientMerges_Patients_SourcePatientId",
                        column: x => x.SourcePatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientMerges_Patients_SurvivorPatientId",
                        column: x => x.SurvivorPatientId,
                        principalTable: "Patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PatientMerges_UserAccounts_MergedByUserId",
                        column: x => x.MergedByUserId,
                        principalTable: "UserAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientMergeMoves",
                columns: table => new
                {
                    MergeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TableName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ColumnName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RowsMoved = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientMergeMoves", x => new { x.MergeId, x.TableName, x.ColumnName });
                    table.CheckConstraint("CK_PatientMergeMoves_Names", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([TableName], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND LEN(REPLACE(REPLACE(REPLACE(REPLACE([ColumnName], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0");
                    table.CheckConstraint("CK_PatientMergeMoves_Rows", "[RowsMoved] >= 0");
                    table.ForeignKey(
                        name: "FK_PatientMergeMoves_PatientMerges_MergeId",
                        column: x => x.MergeId,
                        principalTable: "PatientMerges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PatientMergeResolutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MergeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Choice = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReplacedValue = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientMergeResolutions", x => x.Id);
                    table.CheckConstraint("CK_PatientMergeResolutions_Choice", "[Choice] COLLATE Latin1_General_CS_AS IN ('KeptSurvivor','TookSource','LeftWithSource')");
                    table.CheckConstraint("CK_PatientMergeResolutions_Kind", "[Kind] COLLATE Latin1_General_CS_AS IN ('Field','Record')");
                    table.CheckConstraint("CK_PatientMergeResolutions_Replaced", "[ReplacedValue] IS NULL OR CHARINDEX(NCHAR(0), [ReplacedValue] COLLATE Latin1_General_BIN2) = 0");
                    table.CheckConstraint("CK_PatientMergeResolutions_Shape", "([Kind] = 'Field' AND [Choice] IN ('KeptSurvivor','TookSource') AND ([Choice] = 'TookSource' OR [ReplacedValue] IS NULL)) OR ([Kind] = 'Record' AND [Choice] = 'LeftWithSource' AND [ReplacedValue] IS NULL)");
                    table.CheckConstraint("CK_PatientMergeResolutions_Subject", "LEN(REPLACE(REPLACE(REPLACE(REPLACE([Subject], N' ', N''), NCHAR(9), N''), NCHAR(10), N''), NCHAR(13), N'')) > 0 AND CHARINDEX(NCHAR(0), [Subject] COLLATE Latin1_General_BIN2) = 0 AND PATINDEX(N'%[' + NCHAR(1) + N'-' + NCHAR(31) + N']%', [Subject] COLLATE Latin1_General_BIN2) = 0");
                    table.ForeignKey(
                        name: "FK_PatientMergeResolutions_PatientMerges_MergeId",
                        column: x => x.MergeId,
                        principalTable: "PatientMerges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Patients_Merged",
                table: "Patients",
                sql: "[MergedAtUtc] IS NULL OR ([IsActive] = 0 AND [MergedAtUtc] >= '2000-01-01' AND [MergedAtUtc] < '2200-01-01')");

            migrationBuilder.CreateIndex(
                name: "IX_PatientMergeResolutions_MergeId_Kind_Subject",
                table: "PatientMergeResolutions",
                columns: new[] { "MergeId", "Kind", "Subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientMerges_MergedByUserId",
                table: "PatientMerges",
                column: "MergedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientMerges_MergeKey",
                table: "PatientMerges",
                column: "MergeKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientMerges_SourcePatientId",
                table: "PatientMerges",
                column: "SourcePatientId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PatientMerges_SurvivorPatientId",
                table: "PatientMerges",
                column: "SurvivorPatientId");

            // STORY-017 (errors 51230 to 51234): the lineage tables are append-only, a retired patient is marked merged only by a lineage row and stays so, and the one narrow exception the merge service uses.
            // 1. the immutable-record triggers of every table that holds a patient id stand aside for an UPDATE made inside a merge transaction (the session value alveara.patient_merge = 1);
            // 2. TR_<table>_MergeScope, created for each of those tables, refuses that UPDATE unless it sets nothing but the patient-id columns, and refuses any insert or delete made inside a merge transaction.
            migrationBuilder.Sql(@"DECLARE @pairs TABLE (T sysname, C sysname);
INSERT INTO @pairs (T, C) VALUES
    ('Appointments','PatientId'),
    ('BillingAccounts','PatientId'),
    ('BillingAllocationReleases','PatientId'),
    ('BillingAllocations','PatientId'),
    ('BillingEntries','PaidByPatientId'),
    ('BillingEntries','PatientId'),
    ('BillingResponsibilities','PatientId'),
    ('BillingResponsibilities','ResponsiblePatientId'),
    ('ClearanceVersions','PatientId'),
    ('Clearances','PatientId'),
    ('ClinicalRecordEvents','PatientId'),
    ('ClinicalRecordItemVersions','PatientId'),
    ('ClinicalRecordItems','PatientId'),
    ('ClinicalSectionReviews','PatientId'),
    ('CompletedProcedureAmendments','PatientId'),
    ('CompletedProcedures','PatientId'),
    ('CompletionSets','PatientId'),
    ('Diagnoses','PatientId'),
    ('DiagnosisLinks','PatientId'),
    ('DiagnosisPlanResolutions','PatientId'),
    ('DiagnosisVersions','PatientId'),
    ('DocumentEvents','PatientId'),
    ('Documents','PatientId'),
    ('EncounterEvents','PatientId'),
    ('EncounterVitals','PatientId'),
    ('Encounters','PatientId'),
    ('FinancialDocuments','PatientId'),
    ('FollowUpTasks','PatientId'),
    ('LabCases','PatientId'),
    ('Messages','RelatedPatientId'),
    ('Notifications','RelatedPatientId'),
    ('PatientFormEvents','PatientId'),
    ('PatientForms','PatientId'),
    ('PatientHistory','PatientId'),
    ('PatientRecallPlans','PatientId'),
    ('Patients','GuarantorPatientId'),
    ('PerioExamLinks','PatientId'),
    ('PerioExams','PatientId'),
    ('PerioSessions','PatientId'),
    ('PrescriptionDrafts','PatientId'),
    ('Prescriptions','PatientId'),
    ('ReferralCases','PatientId'),
    ('SafetyAlertAcknowledgements','PatientId'),
    ('SafetyAlertVersions','PatientId'),
    ('SafetyAlerts','PatientId'),
    ('SignedFormSnapshots','PatientId'),
    ('ToothFindingLinks','PatientId'),
    ('ToothFindingVersions','PatientId'),
    ('ToothFindings','PatientId'),
    ('TreatmentPlanEvents','PatientId'),
    ('TreatmentPlanItemStates','PatientId'),
    ('TreatmentPlanItems','PatientId'),
    ('TreatmentPlanRevisionItems','PatientId'),
    ('TreatmentPlanRevisionPhases','PatientId'),
    ('TreatmentPlanRevisions','PatientId'),
    ('TreatmentPlans','PatientId');
DECLARE @name sysname, @def nvarchar(max), @pos int, @guard nvarchar(max) = N'
IF CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')) = 1 AND EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted) RETURN;
';
DECLARE patch CURSOR LOCAL STATIC READ_ONLY FOR
    SELECT DISTINCT t.name, m.definition FROM sys.triggers t
    JOIN sys.sql_modules m ON m.object_id = t.object_id
    JOIN sys.trigger_events e ON e.object_id = t.object_id AND e.type = 2
    WHERE t.parent_class = 1 AND OBJECT_NAME(t.parent_id) IN (SELECT T FROM @pairs) AND m.definition NOT LIKE N'%alveara.patient_merge%';
OPEN patch; FETCH NEXT FROM patch INTO @name, @def;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @pos = CHARINDEX(N' AS', @def, CHARINDEX(N' ON ', @def));
    IF @pos = 0 OR LEFT(@def, 14) <> N'CREATE TRIGGER ' THROW 51234, 'An immutable-record trigger has a shape the merge migration does not know how to patch.', 1;
    SET @def = STUFF(STUFF(@def, @pos + 3, 0, @guard), 1, 6, N'ALTER');
    EXEC (@def);
    FETCH NEXT FROM patch INTO @name, @def;
END
CLOSE patch; DEALLOCATE patch;");

            migrationBuilder.Sql(@"DECLARE @pairs TABLE (T sysname, C sysname);
INSERT INTO @pairs (T, C) VALUES
    ('Appointments','PatientId'),
    ('BillingAccounts','PatientId'),
    ('BillingAllocationReleases','PatientId'),
    ('BillingAllocations','PatientId'),
    ('BillingEntries','PaidByPatientId'),
    ('BillingEntries','PatientId'),
    ('BillingResponsibilities','PatientId'),
    ('BillingResponsibilities','ResponsiblePatientId'),
    ('ClearanceVersions','PatientId'),
    ('Clearances','PatientId'),
    ('ClinicalRecordEvents','PatientId'),
    ('ClinicalRecordItemVersions','PatientId'),
    ('ClinicalRecordItems','PatientId'),
    ('ClinicalSectionReviews','PatientId'),
    ('CompletedProcedureAmendments','PatientId'),
    ('CompletedProcedures','PatientId'),
    ('CompletionSets','PatientId'),
    ('Diagnoses','PatientId'),
    ('DiagnosisLinks','PatientId'),
    ('DiagnosisPlanResolutions','PatientId'),
    ('DiagnosisVersions','PatientId'),
    ('DocumentEvents','PatientId'),
    ('Documents','PatientId'),
    ('EncounterEvents','PatientId'),
    ('EncounterVitals','PatientId'),
    ('Encounters','PatientId'),
    ('FinancialDocuments','PatientId'),
    ('FollowUpTasks','PatientId'),
    ('LabCases','PatientId'),
    ('Messages','RelatedPatientId'),
    ('Notifications','RelatedPatientId'),
    ('PatientFormEvents','PatientId'),
    ('PatientForms','PatientId'),
    ('PatientHistory','PatientId'),
    ('PatientRecallPlans','PatientId'),
    ('Patients','GuarantorPatientId'),
    ('PerioExamLinks','PatientId'),
    ('PerioExams','PatientId'),
    ('PerioSessions','PatientId'),
    ('PrescriptionDrafts','PatientId'),
    ('Prescriptions','PatientId'),
    ('ReferralCases','PatientId'),
    ('SafetyAlertAcknowledgements','PatientId'),
    ('SafetyAlertVersions','PatientId'),
    ('SafetyAlerts','PatientId'),
    ('SignedFormSnapshots','PatientId'),
    ('ToothFindingLinks','PatientId'),
    ('ToothFindingVersions','PatientId'),
    ('ToothFindings','PatientId'),
    ('TreatmentPlanEvents','PatientId'),
    ('TreatmentPlanItemStates','PatientId'),
    ('TreatmentPlanItems','PatientId'),
    ('TreatmentPlanRevisionItems','PatientId'),
    ('TreatmentPlanRevisionPhases','PatientId'),
    ('TreatmentPlanRevisions','PatientId'),
    ('TreatmentPlans','PatientId');
DECLARE @t sysname, @cols nvarchar(max), @sql nvarchar(max);
DECLARE scope CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT T FROM @pairs;
OPEN scope; FETCH NEXT FROM scope INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SELECT @cols = STRING_AGG(CAST(N'UPDATE(' + QUOTENAME(c.name) + N')' AS nvarchar(max)), N' OR ') WITHIN GROUP (ORDER BY c.column_id)
      FROM sys.columns c WHERE c.object_id = OBJECT_ID(QUOTENAME(@t)) AND c.is_computed = 0 AND c.system_type_id <> 189 AND c.name NOT IN (SELECT C FROM @pairs WHERE T = @t);
    SET @sql = N'CREATE TRIGGER ' + QUOTENAME(N'TR_' + @t + N'_MergeScope') + N' ON ' + QUOTENAME(@t) + N' AFTER INSERT, UPDATE, DELETE AS
BEGIN
    IF CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')) = 1
       AND ((EXISTS (SELECT 1 FROM inserted) AND NOT EXISTS (SELECT 1 FROM deleted)) OR (NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted)) OR ' + @cols + N')
        THROW 51234, ''During a patient merge only the patient id of an existing row may change.'', 1;
END';
    EXEC (@sql);
    FETCH NEXT FROM scope INTO @t;
END
CLOSE scope; DEALLOCATE scope;");

            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientMerges_Immutable] ON [PatientMerges] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51230, 'A merge lineage is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientMergeMoves_Immutable] ON [PatientMergeMoves] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51230, 'A merge lineage is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientMergeResolutions_Immutable] ON [PatientMergeResolutions] AFTER UPDATE, DELETE AS
BEGIN
    THROW 51230, 'A merge lineage is never changed or deleted.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_PatientMerges_Survivor] ON [PatientMerges] AFTER INSERT AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i JOIN [PatientMerges] m ON m.[SourcePatientId] = i.[SurvivorPatientId])
        THROW 51231, 'A patient that was merged into another record cannot be the survivor of a merge.', 1;
END");
            migrationBuilder.Sql(@"CREATE TRIGGER [TR_Patients_Merged] ON [Patients] AFTER INSERT, UPDATE AS
BEGIN
    IF EXISTS (SELECT 1 FROM inserted i WHERE i.[MergedAtUtc] IS NOT NULL
               AND NOT EXISTS (SELECT 1 FROM [PatientMerges] m WHERE m.[SourcePatientId] = i.[Id] AND m.[MergedAtUtc] = i.[MergedAtUtc]))
        THROW 51232, 'A patient is marked merged only by a merge lineage with the same time.', 1;
    IF CONVERT(int, SESSION_CONTEXT(N'alveara.patient_merge')) = 1 AND EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted) RETURN;
    IF EXISTS (SELECT 1 FROM deleted d WHERE d.[MergedAtUtc] IS NOT NULL)
        THROW 51233, 'A patient record that was merged into another never changes.', 1;
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // a rollback is only possible while no merge has been made
            migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM [PatientMerges])
    THROW 51239, 'Patient merge data exists, so this migration cannot be rolled back.', 1;");
            migrationBuilder.Sql(@"DROP TRIGGER [TR_Patients_Merged];");
            migrationBuilder.Sql(@"DECLARE @pairs TABLE (T sysname, C sysname);
INSERT INTO @pairs (T, C) VALUES
    ('Appointments','PatientId'),
    ('BillingAccounts','PatientId'),
    ('BillingAllocationReleases','PatientId'),
    ('BillingAllocations','PatientId'),
    ('BillingEntries','PaidByPatientId'),
    ('BillingEntries','PatientId'),
    ('BillingResponsibilities','PatientId'),
    ('BillingResponsibilities','ResponsiblePatientId'),
    ('ClearanceVersions','PatientId'),
    ('Clearances','PatientId'),
    ('ClinicalRecordEvents','PatientId'),
    ('ClinicalRecordItemVersions','PatientId'),
    ('ClinicalRecordItems','PatientId'),
    ('ClinicalSectionReviews','PatientId'),
    ('CompletedProcedureAmendments','PatientId'),
    ('CompletedProcedures','PatientId'),
    ('CompletionSets','PatientId'),
    ('Diagnoses','PatientId'),
    ('DiagnosisLinks','PatientId'),
    ('DiagnosisPlanResolutions','PatientId'),
    ('DiagnosisVersions','PatientId'),
    ('DocumentEvents','PatientId'),
    ('Documents','PatientId'),
    ('EncounterEvents','PatientId'),
    ('EncounterVitals','PatientId'),
    ('Encounters','PatientId'),
    ('FinancialDocuments','PatientId'),
    ('FollowUpTasks','PatientId'),
    ('LabCases','PatientId'),
    ('Messages','RelatedPatientId'),
    ('Notifications','RelatedPatientId'),
    ('PatientFormEvents','PatientId'),
    ('PatientForms','PatientId'),
    ('PatientHistory','PatientId'),
    ('PatientRecallPlans','PatientId'),
    ('Patients','GuarantorPatientId'),
    ('PerioExamLinks','PatientId'),
    ('PerioExams','PatientId'),
    ('PerioSessions','PatientId'),
    ('PrescriptionDrafts','PatientId'),
    ('Prescriptions','PatientId'),
    ('ReferralCases','PatientId'),
    ('SafetyAlertAcknowledgements','PatientId'),
    ('SafetyAlertVersions','PatientId'),
    ('SafetyAlerts','PatientId'),
    ('SignedFormSnapshots','PatientId'),
    ('ToothFindingLinks','PatientId'),
    ('ToothFindingVersions','PatientId'),
    ('ToothFindings','PatientId'),
    ('TreatmentPlanEvents','PatientId'),
    ('TreatmentPlanItemStates','PatientId'),
    ('TreatmentPlanItems','PatientId'),
    ('TreatmentPlanRevisionItems','PatientId'),
    ('TreatmentPlanRevisionPhases','PatientId'),
    ('TreatmentPlanRevisions','PatientId'),
    ('TreatmentPlans','PatientId');
DECLARE @t sysname, @sql nvarchar(max);
DECLARE scope CURSOR LOCAL FAST_FORWARD FOR SELECT DISTINCT T FROM @pairs;
OPEN scope; FETCH NEXT FROM scope INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'DROP TRIGGER ' + QUOTENAME(N'TR_' + @t + N'_MergeScope');
    EXEC (@sql);
    FETCH NEXT FROM scope INTO @t;
END
CLOSE scope; DEALLOCATE scope;");
            migrationBuilder.Sql(@"DECLARE @pairs TABLE (T sysname, C sysname);
INSERT INTO @pairs (T, C) VALUES
    ('Appointments','PatientId'),
    ('BillingAccounts','PatientId'),
    ('BillingAllocationReleases','PatientId'),
    ('BillingAllocations','PatientId'),
    ('BillingEntries','PaidByPatientId'),
    ('BillingEntries','PatientId'),
    ('BillingResponsibilities','PatientId'),
    ('BillingResponsibilities','ResponsiblePatientId'),
    ('ClearanceVersions','PatientId'),
    ('Clearances','PatientId'),
    ('ClinicalRecordEvents','PatientId'),
    ('ClinicalRecordItemVersions','PatientId'),
    ('ClinicalRecordItems','PatientId'),
    ('ClinicalSectionReviews','PatientId'),
    ('CompletedProcedureAmendments','PatientId'),
    ('CompletedProcedures','PatientId'),
    ('CompletionSets','PatientId'),
    ('Diagnoses','PatientId'),
    ('DiagnosisLinks','PatientId'),
    ('DiagnosisPlanResolutions','PatientId'),
    ('DiagnosisVersions','PatientId'),
    ('DocumentEvents','PatientId'),
    ('Documents','PatientId'),
    ('EncounterEvents','PatientId'),
    ('EncounterVitals','PatientId'),
    ('Encounters','PatientId'),
    ('FinancialDocuments','PatientId'),
    ('FollowUpTasks','PatientId'),
    ('LabCases','PatientId'),
    ('Messages','RelatedPatientId'),
    ('Notifications','RelatedPatientId'),
    ('PatientFormEvents','PatientId'),
    ('PatientForms','PatientId'),
    ('PatientHistory','PatientId'),
    ('PatientRecallPlans','PatientId'),
    ('Patients','GuarantorPatientId'),
    ('PerioExamLinks','PatientId'),
    ('PerioExams','PatientId'),
    ('PerioSessions','PatientId'),
    ('PrescriptionDrafts','PatientId'),
    ('Prescriptions','PatientId'),
    ('ReferralCases','PatientId'),
    ('SafetyAlertAcknowledgements','PatientId'),
    ('SafetyAlertVersions','PatientId'),
    ('SafetyAlerts','PatientId'),
    ('SignedFormSnapshots','PatientId'),
    ('ToothFindingLinks','PatientId'),
    ('ToothFindingVersions','PatientId'),
    ('ToothFindings','PatientId'),
    ('TreatmentPlanEvents','PatientId'),
    ('TreatmentPlanItemStates','PatientId'),
    ('TreatmentPlanItems','PatientId'),
    ('TreatmentPlanRevisionItems','PatientId'),
    ('TreatmentPlanRevisionPhases','PatientId'),
    ('TreatmentPlanRevisions','PatientId'),
    ('TreatmentPlans','PatientId');
DECLARE @name sysname, @def nvarchar(max), @guard nvarchar(max) = N'
IF CONVERT(int, SESSION_CONTEXT(N''alveara.patient_merge'')) = 1 AND EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted) RETURN;
';
DECLARE unpatch CURSOR LOCAL STATIC READ_ONLY FOR
    SELECT DISTINCT t.name, m.definition FROM sys.triggers t
    JOIN sys.sql_modules m ON m.object_id = t.object_id
    WHERE t.parent_class = 1 AND OBJECT_NAME(t.parent_id) IN (SELECT T FROM @pairs) AND m.definition LIKE N'%alveara.patient_merge%';
OPEN unpatch; FETCH NEXT FROM unpatch INTO @name, @def;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @def = STUFF(REPLACE(@def, @guard, N''), 1, 6, N'ALTER');
    EXEC (@def);
    FETCH NEXT FROM unpatch INTO @name, @def;
END
CLOSE unpatch; DEALLOCATE unpatch;");

            migrationBuilder.DropTable(
                name: "PatientMergeMoves");

            migrationBuilder.DropTable(
                name: "PatientMergeResolutions");

            migrationBuilder.DropTable(
                name: "PatientMerges");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Patients_Merged",
                table: "Patients");

            migrationBuilder.DropColumn(
                name: "MergedAtUtc",
                table: "Patients");
        }
    }
}
