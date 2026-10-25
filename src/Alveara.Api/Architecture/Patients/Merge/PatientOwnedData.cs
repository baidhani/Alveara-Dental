namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>What a patient-id column means to a merge.</summary>
public enum PatientDataRole
{
    /// <summary>The row belongs to the patient (their chart, appointments, billing, documents, tasks). A merge moves it to the surviving patient.</summary>
    Owner,
    /// <summary>The row belongs to someone else and only points at a patient (a guarantor, the person who paid, a responsible party, a related patient on a message). A merge re-points the pointer where it names the merged-away patient.</summary>
    Reference,
}

/// <summary>
/// How a database uniqueness rule that includes the patient id is handled when the merged-away patient and the survivor would end up with the same key. The rule is declared here so a merge cannot meet it by surprise.
/// </summary>
public enum MergeCollisionPolicy
{
    /// <summary>At most one row per patient (a billing account, a perio session setup, a recall arrangement): when both patients have one, the merge needs an explicit resolution; it never silently drops either.</summary>
    OnePerPatient,
    /// <summary>A natural key (a chart item by kind and name, a section review, a tooth finding, a clearance, a safety alert, a signed form by template, a plan reference code): an equal key on both patients is a conflict the merge reports and the administrator resolves.</summary>
    NaturalKey,
    /// <summary>A replay-protection key (the Idempotency-Key a create carried): it only guards a retry, so an equal key on both patients is re-keyed on the moved row, never a conflict for the person.</summary>
    ReplayKey,
}

/// <summary>A uniqueness rule that includes the patient id: the columns (patient id first) and how a merge deals with a collision.</summary>
public sealed record UniqueRule(string Columns, MergeCollisionPolicy Policy);

/// <summary>One column that holds a patient id: where, what it means to a merge, the uniqueness rules it takes part in, and whether it is a database foreign key to the patient table. A column that is NOT (child, version and event tables that copy the id of their parent) is guarded by triggers that insist it equals the parent's, so a merge must change parent and child together in one flagged transaction and cannot rely on a foreign key violation to find a table it forgot.</summary>
public sealed record PatientOwnedColumn(string Table, string Column, PatientDataRole Role, IReadOnlyList<UniqueRule>? Unique = null, bool ForeignKey = true)
{
    public IReadOnlyList<UniqueRule> UniqueRules => Unique ?? [];
}

/// <summary>
/// STORY-017: THE list of everything that holds a patient id, which is what "a merge preserves linked data" means in this database. A merge moves (or re-points) exactly these columns inside one transaction; nothing outside
/// this list is touched and nothing inside it is forgotten, because <c>PatientOwnedDataTests</c> compares the list with the database model and FAILS when a table or column with a patient id is added, removed or given a new
/// uniqueness rule without this list being updated. Audit entries are history about a patient and are never moved (a merge adds its own); the Patients table itself holds the merged-away row (the guarantor pointer below
/// is the only patient id in it).
/// </summary>
public static class PatientOwnedData
{
    public static readonly IReadOnlyList<PatientOwnedColumn> All =
    [
        new("Appointments", "PatientId", PatientDataRole.Owner),
        new("BillingAccounts", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId", MergeCollisionPolicy.OnePerPatient) }),
        new("BillingAllocationReleases", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("BillingAllocations", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("BillingEntries", "PaidByPatientId", PatientDataRole.Reference),
        new("BillingEntries", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("BillingResponsibilities", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("BillingResponsibilities", "ResponsiblePatientId", PatientDataRole.Reference),
        new("ClearanceVersions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("Clearances", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,Kind,ReasonKey", MergeCollisionPolicy.NaturalKey) }),
        new("ClinicalRecordEvents", "PatientId", PatientDataRole.Owner),
        new("ClinicalRecordItemVersions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("ClinicalRecordItems", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,Kind,Name", MergeCollisionPolicy.NaturalKey) }),
        new("ClinicalSectionReviews", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,Section", MergeCollisionPolicy.NaturalKey) }),
        new("CompletedProcedureAmendments", "PatientId", PatientDataRole.Owner),
        new("CompletedProcedures", "PatientId", PatientDataRole.Owner),
        new("CompletionSets", "PatientId", PatientDataRole.Owner),
        new("Diagnoses", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey) }),
        new("DiagnosisLinks", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("DiagnosisPlanResolutions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("DiagnosisVersions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("DocumentEvents", "PatientId", PatientDataRole.Owner),
        new("Documents", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey) }),
        new("EncounterEvents", "PatientId", PatientDataRole.Owner),
        new("EncounterVitals", "PatientId", PatientDataRole.Owner),
        new("Encounters", "PatientId", PatientDataRole.Owner),
        new("FinancialDocuments", "PatientId", PatientDataRole.Owner),
        new("FollowUpTasks", "PatientId", PatientDataRole.Owner),
        new("LabCases", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey) }),
        new("Messages", "RelatedPatientId", PatientDataRole.Reference),
        new("Notifications", "RelatedPatientId", PatientDataRole.Reference),
        new("PatientFormEvents", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("PatientForms", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,TemplateId", MergeCollisionPolicy.NaturalKey) }),
        new("PatientHistory", "PatientId", PatientDataRole.Owner),
        new("PatientRecallPlans", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId", MergeCollisionPolicy.OnePerPatient) }),
        new("Patients", "GuarantorPatientId", PatientDataRole.Reference),
        new("PerioExamLinks", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("PerioExams", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey) }),
        new("PerioSessions", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId", MergeCollisionPolicy.OnePerPatient) }),
        new("PrescriptionDrafts", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,CreateKey", MergeCollisionPolicy.ReplayKey) }),
        new("Prescriptions", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey) }),
        new("ReferralCases", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey) }),
        new("SafetyAlertAcknowledgements", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("SafetyAlertVersions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("SafetyAlerts", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,Category,Title", MergeCollisionPolicy.NaturalKey) }),
        new("SignedFormSnapshots", "PatientId", PatientDataRole.Owner),
        new("ToothFindingLinks", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("ToothFindingVersions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("ToothFindings", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,ToothKey,Surface,Condition", MergeCollisionPolicy.NaturalKey) }),
        new("TreatmentPlanEvents", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("TreatmentPlanItemStates", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("TreatmentPlanItems", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("TreatmentPlanRevisionItems", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("TreatmentPlanRevisionPhases", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("TreatmentPlanRevisions", "PatientId", PatientDataRole.Owner, ForeignKey: false),
        new("TreatmentPlans", "PatientId", PatientDataRole.Owner, new UniqueRule[] { new("PatientId,IdempotencyKey", MergeCollisionPolicy.ReplayKey), new("PatientId,ReferenceCode", MergeCollisionPolicy.NaturalKey) }),
    ];

    public static IEnumerable<PatientOwnedColumn> Owned => All.Where(c => c.Role == PatientDataRole.Owner);

    public static IEnumerable<PatientOwnedColumn> References => All.Where(c => c.Role == PatientDataRole.Reference);

    /// <summary>The session value only the merge service sets, inside its transaction, and the name of the merge-scope trigger every registered table carries (see the AddPatientMerges migration).</summary>
    public const string MergeFlag = "alveara.patient_merge";
    public static string MergeScopeTrigger(string table) => $"TR_{table}_MergeScope";

    /// <summary>The merge's own lineage table. It holds patient ids (who was merged into whom) but is not patient data: a merge never moves it, and the registry test leaves it out.</summary>
    public static readonly IReadOnlyList<string> Bookkeeping = ["PatientMerges"];

    /// <summary>The distinct tables a merge touches.</summary>
    public static IReadOnlyList<string> Tables => All.Select(c => c.Table).Distinct().Order().ToList();
}
