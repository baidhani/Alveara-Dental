namespace Alveara.Api.Architecture.Safety;

/// <summary>
/// ALV-N011: a patient-safety alert a clinician stated explicitly - a significant condition, a pregnancy flag, anticoagulant status, an adverse reaction, or a custom alert -
/// with where the information came from. Allergies and current medications are NOT copied into alerts: they are read live from the clinical record
/// (<see cref="Clinical.ClinicalRecordItem"/>) so there is one source of truth. Nothing here is ever inferred from unrelated data or created to fill an empty field.
///
/// An alert is Active or Resolved. Acknowledging it (see <see cref="SafetyAlertAcknowledgement"/>) records that someone SAW it and never changes its status; resolving needs
/// who, when and a reason. An alert is never deleted, and every change appends a <see cref="SafetyAlertVersion"/>. A stale edit is refused through <see cref="RowVersion"/>.
/// </summary>
public class SafetyAlert
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="SafetyCategories.AlertCategories"/>.</summary>
    public required string Category { get; set; }
    public required string Title { get; set; }
    public string? Detail { get; set; }
    /// <summary>One of <see cref="SafetySeverities"/>.</summary>
    public required string Severity { get; set; }
    /// <summary>Where the information came from, in the clinician's words (required: an alert with no source is refused).</summary>
    public required string SourceNote { get; set; }
    /// <summary>The clinical-record item this alert is about, when there is one (for example the history item for a condition). The link is provenance only.</summary>
    public Guid? SourceItemId { get; set; }
    /// <summary><see cref="SafetyAlertStatuses.Active"/> or <see cref="SafetyAlertStatuses.Resolved"/>.</summary>
    public required string Status { get; set; }
    /// <summary>Starts at 1 and moves with every change that a person who already saw the alert should see again; an acknowledgement belongs to one revision.</summary>
    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    /// <summary>Set exactly when Status is Resolved (a database check keeps them in step); reopening clears them (the history keeps what they were).</summary>
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public Guid? ResolvedByUserId { get; set; }
    public string? ResolutionReason { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>Append-only history of a <see cref="SafetyAlert"/>: a full snapshot after every create, change, resolve and reopen with who, when and why. A trigger refuses any edit.</summary>
public class SafetyAlertVersion
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public Guid PatientId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>One of <see cref="SafetyChangeTypes"/>.</summary>
    public required string ChangeType { get; set; }
    public required string Category { get; set; }
    public required string Title { get; set; }
    public string? Detail { get; set; }
    public required string Severity { get; set; }
    public required string SourceNote { get; set; }
    public required string Status { get; set; }
    public string? Reason { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

/// <summary>
/// "This person saw this revision of this alert." A separate, append-only record on purpose: it can never change the alert's status, so acknowledging can never be mistaken for
/// resolving. When the alert changes (a new revision) earlier acknowledgements no longer count and it needs to be seen again.
/// </summary>
public class SafetyAlertAcknowledgement
{
    public Guid Id { get; set; }
    public Guid AlertId { get; set; }
    public Guid PatientId { get; set; }
    public Guid UserId { get; set; }
    public int Revision { get; set; }
    public DateTimeOffset AcknowledgedAtUtc { get; set; }
}

/// <summary>
/// A medical or dental clearance the practice needs before treatment: requested -> received -> resolved (or cancelled), each step with who, when and why. Receiving does not resolve
/// it; resolving needs a received clearance and a reason. The supporting document may not be available yet: the clearance can be received without one and the reference attached
/// later (documents are a later story, so the reference is free text today). Never deleted; every change appends a <see cref="ClearanceVersion"/>.
/// </summary>
public class Clearance
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    /// <summary>One of <see cref="SafetyCategories.ClearanceKinds"/>.</summary>
    public required string Kind { get; set; }
    /// <summary>Why the clearance is needed (required).</summary>
    public required string Reason { get; set; }
    /// <summary>A hash of the reason (case-insensitive), so the database can refuse a second OPEN clearance of the same kind for the same reason without indexing the long text.</summary>
    public required string ReasonKey { get; set; }
    /// <summary>Who it was requested from (a physician, a specialist), as the clinician wrote it.</summary>
    public string? RequestedFrom { get; set; }
    /// <summary>One of <see cref="ClearanceStatuses"/>.</summary>
    public required string Status { get; set; }
    /// <summary>A reference to the supporting document, when there is one (free text until documents exist); null means "not yet available".</summary>
    public string? DocumentReference { get; set; }
    public DateTimeOffset RequestedAtUtc { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTimeOffset? ReceivedAtUtc { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public string? ReceivedNote { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? ClosedByUserId { get; set; }
    /// <summary>Why it was resolved or cancelled (required for both).</summary>
    public string? ClosingReason { get; set; }
    public DateTimeOffset? UpdatedAtUtc { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class ClearanceVersion
{
    public Guid Id { get; set; }
    public Guid ClearanceId { get; set; }
    public Guid PatientId { get; set; }
    public int VersionNumber { get; set; }
    /// <summary>One of <see cref="ClearanceChangeTypes"/>.</summary>
    public required string ChangeType { get; set; }
    public required string Kind { get; set; }
    public required string Reason { get; set; }
    public string? RequestedFrom { get; set; }
    public required string Status { get; set; }
    public string? DocumentReference { get; set; }
    public string? Note { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
}

public static class SafetyCategories
{
    public const string Allergy = "Allergy";
    public const string Medication = "Medication";
    public const string Condition = "Condition";
    public const string Pregnancy = "Pregnancy";
    public const string Anticoagulant = "Anticoagulant";
    public const string AdverseReaction = "AdverseReaction";
    public const string Clearance = "Clearance";
    public const string Custom = "Custom";

    /// <summary>The categories a clinician can state as an alert. Allergies and medications come from the clinical record; clearances have their own workflow.</summary>
    public static readonly IReadOnlyList<string> AlertCategories = [Condition, Pregnancy, Anticoagulant, AdverseReaction, Custom];
    public static readonly IReadOnlyList<string> ClearanceKinds = ["Medical", "Dental"];
}

public static class SafetySeverities
{
    public const string Critical = "Critical";
    public const string High = "High";
    public const string Moderate = "Moderate";
    public const string Low = "Low";
    /// <summary>Most urgent first.</summary>
    public static readonly IReadOnlyList<string> All = [Critical, High, Moderate, Low];

    /// <summary>The rank of a severity (0 = most urgent), or int.MaxValue for none.</summary>
    public static int Rank(string? severity) => severity is null ? int.MaxValue : All.ToList().IndexOf(severity) is var i and >= 0 ? i : int.MaxValue;

    /// <summary>An allergy's recorded severity on the same scale; null stays null (not recorded is never guessed).</summary>
    public static string? FromAllergy(string? allergySeverity) => allergySeverity switch { "Severe" => High, "Moderate" => Moderate, "Mild" => Low, _ => null };
}

public static class SafetyAlertStatuses
{
    public const string Active = "Active";
    public const string Resolved = "Resolved";
}

public static class SafetyChangeTypes
{
    public const string Created = "Created";
    public const string Changed = "Changed";
    public const string Resolved = "Resolved";
    public const string Reopened = "Reopened";
}

public static class ClearanceStatuses
{
    public const string Requested = "Requested";
    public const string Received = "Received";
    public const string Resolved = "Resolved";
    public const string Cancelled = "Cancelled";
    public static bool IsOpen(string status) => status is Requested or Received;
}

public static class ClearanceChangeTypes
{
    public const string Requested = "Requested";
    public const string Received = "Received";
    public const string DocumentAttached = "DocumentAttached";
    public const string Resolved = "Resolved";
    public const string Cancelled = "Cancelled";
}
