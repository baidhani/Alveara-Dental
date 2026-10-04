namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: one item in a patient's LONGITUDINAL clinical record - a medical-history condition, a dental-history item, an allergy or a medication that holds
/// across encounters (STORY-005's encounter entries are what was documented at one visit; this is the running chart).
///
/// An item moves through a status over time (an allergy becomes Resolved, a medication Discontinued) and can be corrected; none of that overwrites the past: every
/// change appends a <see cref="ClinicalRecordItemVersion"/> with who, when and (when given) why, and an item entered in error is marked removed, never deleted.
/// A change made during an encounter names that encounter. Concurrent edits are refused through <see cref="RowVersion"/>.
/// </summary>
public class ClinicalRecordItem
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="EncounterEntryKinds"/>.</summary>
    public required string Kind { get; set; }
    public required string Name { get; set; }
    public string? Detail { get; set; }
    /// <summary>Allergies only: the reaction, when known.</summary>
    public string? Reaction { get; set; }
    /// <summary>Allergies only: one of <see cref="EncounterSeverities"/>, when known.</summary>
    public string? Severity { get; set; }
    /// <summary>Medications only.</summary>
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    /// <summary>One of <see cref="ClinicalItemStatuses"/>; which values are allowed depends on the kind (a database check agrees with <see cref="ClinicalRecordRules"/>).</summary>
    public required string Status { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    /// <summary>The last time the item (or its status) changed; the section's "reviewed" stamp is stale once any item is newer than it.</summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Set when the item was entered in error. It is kept (with its history) and no longer shown or counted.</summary>
    public DateTimeOffset? RemovedAtUtc { get; set; }
    public Guid? RemovedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>
/// Append-only history of a <see cref="ClinicalRecordItem"/>: a full snapshot after each change, so "what did this allergy say last March, and who changed it" is
/// always answerable. A database trigger refuses any update or delete.
/// </summary>
public class ClinicalRecordItemVersion
{
    public Guid Id { get; set; }
    public Guid ItemId { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>1 for the first version, then 2, 3... (unique per item, so two writers cannot both claim the same next version).</summary>
    public int VersionNumber { get; set; }
    /// <summary>One of <see cref="ClinicalChangeTypes"/>.</summary>
    public required string ChangeType { get; set; }
    public required string Kind { get; set; }
    public required string Name { get; set; }
    public string? Detail { get; set; }
    public string? Reaction { get; set; }
    public string? Severity { get; set; }
    public string? Dose { get; set; }
    public string? Frequency { get; set; }
    public required string Status { get; set; }
    /// <summary>Why, when the clinician said (required when an item is removed as entered in error).</summary>
    public string? Reason { get; set; }
    /// <summary>The draft encounter this change was made during, when there was one.</summary>
    public Guid? EncounterId { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// The state of one section of a patient's record when it is NOT simply "has items": the clinician reviewed it and nothing is known
/// (<see cref="ClinicalReviewStates.NoneKnown"/>), could not establish it (<see cref="ClinicalReviewStates.Unknown"/>), or confirmed that the items listed are
/// current (<see cref="ClinicalReviewStates.Reviewed"/>). No row at all means "not reviewed". At most one row per patient and section.
///
/// These states exist so a required field never forces an invented fact: "none known", "unknown" and "not reviewed" are three different statements.
/// </summary>
public class ClinicalSectionReview
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="EncounterEntryKinds"/>.</summary>
    public required string Section { get; set; }
    public required string State { get; set; }
    public DateTimeOffset ReviewedAtUtc { get; set; }
    public Guid? ReviewedByUserId { get; set; }
}

/// <summary>Append-only, PHI-free history of changes to a patient's clinical record sections (who reviewed, what kind of change, when). A trigger refuses updates and deletes.</summary>
public class ClinicalRecordEvent
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public required string Section { get; set; }
    public required string EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public Guid? EncounterId { get; set; }
    public string? Detail { get; set; }
}

public static class ClinicalItemStatuses
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    /// <summary>History items and allergies that no longer apply.</summary>
    public const string Resolved = "Resolved";
    /// <summary>Medications that were stopped.</summary>
    public const string Discontinued = "Discontinued";

    public static IReadOnlyList<string> AllowedFor(string kind) =>
        kind == EncounterEntryKinds.Medication ? [Active, Inactive, Discontinued] : [Active, Inactive, Resolved];
}

public static class ClinicalChangeTypes
{
    public const string Added = "Added";
    public const string Changed = "Changed";
    public const string StatusChanged = "StatusChanged";
    public const string RemovedInError = "RemovedInError";
}

public static class ClinicalReviewStates
{
    public const string Reviewed = "Reviewed";
    public const string NoneKnown = "NoneKnown";
    public const string Unknown = "Unknown";
    /// <summary>Not stored: a request for it clears the section's review.</summary>
    public const string NotReviewed = "NotReviewed";
    public static readonly IReadOnlyList<string> Stored = [Reviewed, NoneKnown, Unknown];
}

/// <summary>What a reader is told about a section of the record. Derived from the items and the review row, never stored.</summary>
public static class ClinicalSectionStatuses
{
    /// <summary>Items are listed and a clinician confirmed them after the last change.</summary>
    public const string Reviewed = "Reviewed";
    /// <summary>Items are listed and have not been confirmed, or an item changed after the confirmation.</summary>
    public const string NeedsReview = "NeedsReview";
    public const string NoneKnown = "NoneKnown";
    public const string Unknown = "Unknown";
    /// <summary>Nothing is listed and nobody has said anything about it.</summary>
    public const string NotReviewed = "NotReviewed";
}
