namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>
/// One part of a patient's record in an export package: a stable <see cref="Key"/> (it names the file <c>data/&lt;key&gt;.json</c> and appears in the manifest, so it never changes once shipped), a title a person can read,
/// the <see cref="Tables"/> whose rows belong to the patient (including child, version and event tables that carry no patient id of their own) and the <see cref="LookupTables"/> whose rows are practice-wide but are
/// exported when a row of the domain refers to them (a procedure definition a plan item uses), so the record can be read without the practice's database.
/// </summary>
public sealed record ExportDomain(string Key, string Title, IReadOnlyList<string> Tables, IReadOnlyList<string>? Lookup = null)
{
    public IReadOnlyList<string> LookupTables => Lookup ?? [];
}

/// <summary>Tables a package never carries, with the reason the manifest states for them. One reason per group so a reader sees why, not only that.</summary>
public sealed record ExcludedGroup(string Key, string Reason, IReadOnlyList<string> Tables);

/// <summary>
/// ALV-018-C01: THE answer to "what is in a patient export package, and what is deliberately not". Every table of the database model is named here exactly once, either in a domain the package carries or in an excluded
/// group with a reason, so an export can never be silently incomplete: <c>ExportDomainCatalogTests</c> compares this catalog with the model and FAILS when a table is added, renamed or removed (or a table that holds a
/// patient id is excluded) until this file says what the export does about it. It is the same idea as <see cref="Merge.PatientOwnedData"/>, which lists the columns that hold a patient id; this catalog adds the child
/// tables that have none and the practice-wide tables a record refers to. The manifest of every package is written from it.
/// </summary>
public static class ExportDomainCatalog
{
    public static readonly IReadOnlyList<ExportDomain> Domains =
    [
        new("registration", "Registration and history of changes", ["Patients", "Households", "PatientHistory"]),
        new("appointments", "Appointments", ["Appointments", "AppointmentEvents"], ["PracticeLocations", "Operatories", "AppointmentTypes"]),
        new("forms", "Patient forms and signed copies", ["PatientForms", "SignedFormSnapshots", "PatientFormEvents"], ["FormTemplates", "FormTemplateVersions"]),
        new("encounters", "Visits and clinical notes", ["Encounters", "EncounterEntries", "EncounterSectionMarks", "EncounterAddenda", "EncounterEvents", "EncounterNotes", "EncounterVitals"], ["NoteTemplates", "NoteTemplateSections"]),
        new("medical_history", "Medical history and clearances", ["ClinicalRecordItems", "ClinicalRecordItemVersions", "ClinicalSectionReviews", "ClinicalRecordEvents", "Clearances", "ClearanceVersions"]),
        new("safety_alerts", "Safety alerts", ["SafetyAlerts", "SafetyAlertVersions", "SafetyAlertAcknowledgements"]),
        new("dental_chart", "Dental chart (tooth findings)", ["ToothFindings", "ToothFindingVersions", "ToothFindingLinks"], ["ConditionTypes"]),
        new("diagnoses", "Diagnoses", ["Diagnoses", "DiagnosisVersions", "DiagnosisLinks", "DiagnosisPlanResolutions"]),
        new("periodontal", "Periodontal charting", ["PerioExams", "PerioReadings", "PerioToothRecords", "PerioSessions", "PerioSessionReadings", "PerioSessionTeeth", "PerioExamLinks"]),
        new("treatment_plans", "Treatment plans", ["TreatmentPlans", "TreatmentPlanItems", "TreatmentPlanEvents", "TreatmentPlanRevisions", "TreatmentPlanRevisionPhases", "TreatmentPlanRevisionItems", "TreatmentPlanItemStates"], ["ProcedureDefinitions", "ProcedureVersions"]),
        new("completed_procedures", "Completed procedures", ["CompletionSets", "CompletedProcedures", "CompletedProcedureAmendments"]),
        new("billing", "Billing and financial documents", ["BillingAccounts", "BillingEntries", "BillingAllocations", "BillingResponsibilities", "BillingAllocationReleases", "FinancialDocuments"], ["BillingPaymentMethods"]),
        new("prescriptions", "Prescriptions", ["Prescriptions", "PrescriptionDrafts", "PrescriptionDetails", "PrescriptionEvents"]),
        new("documents", "Documents (scans, images, letters)", ["Documents", "DocumentEvents", "DocumentIntegrity", "DocumentStatusChanges"], ["DocumentCategories"]),
        new("messages", "Messages and notifications about the patient", ["Messages", "MessageRecipients", "Notifications"]),
        new("follow_ups", "Follow-up tasks", ["FollowUpTasks", "FollowUpTaskEvents", "FollowUpReminderDeliveries"]),
        new("recall", "Recall plan", ["PatientRecallPlans"]),
        new("external_cases", "Referrals and lab cases", ["ReferralCases", "LabCases", "ExternalCaseEvents", "ExternalCaseDocuments"], ["ExternalParties"]),
        new("record_lineage", "Merge and unmerge history", ["PatientMerges", "PatientMergeMoves", "PatientMergeResolutions", "PatientMergeMovedRows", "PatientUnmerges", "PatientDuplicateDismissals"]),
    ];

    public static readonly IReadOnlyList<ExcludedGroup> Excluded =
    [
        new("accounts_and_credentials",
            "Sign-in accounts, staff profiles and one-time codes hold credentials and staff identities. They are never in a package; staff appear in a record by display name only.",
            ["UserAccounts", "StaffProfiles", "ProviderProfiles", "MfaRecoveryCodes", "MfaChallenges", "PasswordResetTokens", "BootstrapStates"]),
        new("access_policy",
            "Who may do what in this practice is security configuration of the practice, not part of any patient's record.",
            ["RolePermissionOverrides", "RolePermissionHistories", "PermissionDelegations", "PermissionPolicyState"]),
        new("audit_log",
            "The audit log is the practice's system-wide security record of who did what and names staff and other patients' events; the practice keeps it. Disclosures of this patient are reported by the export history, not copied into the package.",
            ["AuditLogEntries", "DataExportHistory"]),
        new("practice_configuration",
            "Practice-wide settings and schedules that belong to the practice and not to a patient.",
            ["PracticeSettings", "PatientRegistrationSettings", "RecallSettings", "DocumentSettings", "BlockedFileExtensions", "ProviderWeeklyAvailabilities", "ProviderBlockedTimes", "ConditionTypeEvents", "ProcedureEvents"]),
        new("system_operations",
            "Technical bookkeeping of the running system (job queue, replay protection, backups, measurements, counters, per-user cursors) with no clinical or administrative content about a patient.",
            ["BackgroundJobs", "BackgroundJobEffectReceipts", "MeasurementEvents", "IdempotencyReceipts", "BackupSettings", "BackupRecords", "RestoreDrills", "BackupNotifications", "DeploymentInvariants", "DocumentEventCounter", "NotificationCursors"]),
    ];

    public static IEnumerable<string> IncludedTables => Domains.SelectMany(d => d.Tables);

    public static IEnumerable<string> LookupTables => Domains.SelectMany(d => d.LookupTables);

    public static IEnumerable<string> ExcludedTables => Excluded.SelectMany(g => g.Tables);

    /// <summary>Every table the catalog names, in any role.</summary>
    public static IEnumerable<string> AllTables => IncludedTables.Concat(LookupTables).Concat(ExcludedTables);
}
