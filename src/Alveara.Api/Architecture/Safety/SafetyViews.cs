namespace Alveara.Api.Architecture.Safety;

/// <summary>A patient-safety rule refused a request. Carries a stable machine code (the client branches on it), a message safe to show, the HTTP status, and per-field messages.</summary>
public class SafetyException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}

/// <summary>Where a safety entry came from: read live from the clinical record, or stated as an alert.</summary>
public static class SafetyOrigins
{
    public const string ClinicalRecord = "ClinicalRecord";
    public const string Alert = "Alert";
}

/// <summary>
/// One thing a clinician should know before treating this patient. <see cref="Origin"/> says where it comes from and <see cref="Source"/> says so in words. For an alert,
/// <see cref="Revision"/> and <see cref="RowVersion"/> are what a writer echoes back, and <see cref="AcknowledgedByMe"/> says whether the CALLER has seen this revision - which
/// is separate from <see cref="Status"/> and never changes it. <see cref="NeedsAttention"/> flags stale or conflicting data (for example an alert whose clinical-record source
/// has since been resolved), with the reason in words; nothing is hidden or silently corrected.
/// </summary>
public sealed record SafetyEntry(
    string Origin, Guid Id, string Category, string Title, string? Detail, string? Severity, string Status, string Source, Guid? SourceItemId,
    DateTimeOffset LastUpdatedAtUtc, string? LastUpdatedByName, bool NeedsAttention, string? AttentionReason,
    bool AcknowledgedByMe, DateTimeOffset? AcknowledgedAtUtc, int? Revision, string? RowVersion,
    DateTimeOffset? ResolvedAtUtc, string? ResolvedByName, string? ResolutionReason);

public sealed record ClearanceView(
    Guid Id, string Kind, string Reason, string? RequestedFrom, string Status, string? DocumentReference, bool DocumentPending,
    DateTimeOffset RequestedAtUtc, string? RequestedByName, DateTimeOffset? ReceivedAtUtc, string? ReceivedByName, string? ReceivedNote,
    DateTimeOffset? ClosedAtUtc, string? ClosedByName, string? ClosingReason, DateTimeOffset? UpdatedAtUtc, string RowVersion);

/// <summary>A part of the picture that is NOT established (never reviewed, unknown, changed since confirmed). It is shown as exactly that - an absence of an entry here never means "none".</summary>
public sealed record SafetyGap(string Section, string Status, string Message);

/// <summary>The compact figures for the patient header and the clinical screens: counts and the highest severity only, no names.</summary>
public sealed record SafetySummary(
    Guid PatientId, DateTimeOffset AsOfUtc, int ActiveAlertCount, int ActiveAllergyCount, int CurrentMedicationCount, string? HighestSeverity,
    int OpenClearanceCount, int UnacknowledgedAlertCount, int NeedsAttentionCount, IReadOnlyList<SafetyGap> Gaps);

/// <summary>
/// The shared patient-safety context every clinical screen can query before diagnosis, planning, treatment and prescribing. <see cref="Entries"/> are the active ones, most urgent
/// first; <see cref="Resolved"/> alerts stay visible with who resolved them and why; <see cref="Clearances"/> list open ones first.
/// </summary>
public sealed record SafetyContext(
    Guid PatientId, DateTimeOffset AsOfUtc, SafetySummary Summary, IReadOnlyList<SafetyEntry> Entries, IReadOnlyList<SafetyEntry> Resolved,
    IReadOnlyList<ClearanceView> Clearances, IReadOnlyList<SafetyGap> Gaps);

/// <summary>
/// The MINIMAL indicator the shared live visit board may show: whether the patient has something active and whether a clearance is open. It deliberately carries no category,
/// name, severity or count - the shared board must not expose a diagnosis, allergy or medication.
/// </summary>
public sealed record SafetyIndicator(bool Alert, bool Clearance);

public sealed record AlertVersionView(
    int VersionNumber, string ChangeType, string Category, string Title, string? Detail, string Severity, string SourceNote, string Status, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc);

public sealed record AlertHistoryView(Guid AlertId, IReadOnlyList<AlertVersionView> Versions);

public sealed record ClearanceVersionView(
    int VersionNumber, string ChangeType, string Kind, string Reason, string? RequestedFrom, string Status, string? DocumentReference, string? Note, string? ActorName, DateTimeOffset OccurredAtUtc);

public sealed record ClearanceHistoryView(Guid ClearanceId, IReadOnlyList<ClearanceVersionView> Versions);

/// <summary>The query the downstream clinical screens (odontogram, perio, diagnosis, planning, completion, prescriptions) use, so they all read the same safety picture.</summary>
public interface ISafetyContextProvider
{
    Task<SafetyContext> GetAsync(Guid patientId, Guid forUserId, CancellationToken ct);
    Task<SafetySummary> SummaryAsync(Guid patientId, Guid forUserId, CancellationToken ct);
}
