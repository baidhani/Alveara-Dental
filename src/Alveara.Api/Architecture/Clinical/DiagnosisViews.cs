namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// A diagnosis rule refused a request. Carries a stable machine code (the client branches on it), a message safe to show, the HTTP status and, for <c>validation_failed</c>, every problem found with
/// its field so the screen can point at what to correct.
/// </summary>
public class DiagnosisException(string code, string message, int statusCode, IReadOnlyList<DiagnosisProblem>? problems = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<DiagnosisProblem> Problems { get; } = problems ?? [];
}

/// <summary>The only state a treatment-plan forward reference can be in until treatment plans exist and a later story reconciles them.</summary>
public static class TreatmentPlanReferenceStates
{
    public const string Unresolved = "Unresolved";
}

/// <summary>
/// One diagnosis as it is shown. <see cref="TreatmentPlanReference"/> is an opaque forward reference; <see cref="TreatmentPlanReferenceState"/> says so (Unresolved) whenever a reference is present, and is
/// null when there is none, so no screen can present the reference as proof that a plan exists. <see cref="RowVersion"/> is what a correction or withdrawal echoes back.
/// </summary>
public sealed record DiagnosisView(
    Guid Id, Guid PatientId, Guid EncounterId, DateTimeOffset EncounterAtUtc, string Label, string? ToothKey, string? Notes, string? TreatmentPlanReference, string? TreatmentPlanReferenceState,
    string Status, string? RecordedByName, DateTimeOffset RecordedAtUtc, string? UpdatedByName, DateTimeOffset? UpdatedAtUtc, string? WithdrawnByName, DateTimeOffset? WithdrawnAtUtc, string? WithdrawnReason, string RowVersion,
    string? CodingSystem = null, string? Code = null, string Source = DiagnosisSources.Manual, string? SourceNote = null, string? RegionKey = null, IReadOnlyList<DiagnosisLinkView>? Links = null);

/// <summary>A link from a diagnosis to a finding or periodontal chart of the same patient: what it points at, in words, and who linked it and when.</summary>
public sealed record DiagnosisLinkView(string LinkType, Guid TargetId, string Summary, string? LinkedByName, DateTimeOffset LinkedAtUtc);

/// <summary>One step in a diagnosis's history: the whole diagnosis as it stood after the change, who changed it, when and why.</summary>
public sealed record DiagnosisVersionView(
    int VersionNumber, string ChangeType, string Label, string? ToothKey, string? Notes, string? TreatmentPlanReference, string? TreatmentPlanReferenceState, string Status, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc,
    string? CodingSystem = null, string? Code = null, string Source = DiagnosisSources.Manual, string? SourceNote = null, string? RegionKey = null);

public sealed record DiagnosisHistoryView(Guid DiagnosisId, IReadOnlyList<DiagnosisVersionView> Versions);

/// <summary>
/// A correction to a diagnosis: the corrected label, tooth and notes (the whole entry, as on the screen), and what to do with the treatment-plan reference. The reference is NEVER removed or replaced by
/// accident: leaving <see cref="TreatmentPlanReference"/> null keeps it, a value replaces it (an explicit act, recorded in the history with the reason), and only
/// <see cref="ClearTreatmentPlanReference"/> removes it.
/// </summary>
public sealed record DiagnosisCorrection(string? Label, string? ToothKey, string? Notes, string? TreatmentPlanReference, bool ClearTreatmentPlanReference, string? TreatmentPlanReferenceState = null);

/// <summary>
/// ALV-013-C01: an amendment to the STRUCTURE of a diagnosis: the whole location (tooth or region), coding and source as they should now stand (null means none). The label, notes and treatment-plan
/// reference are not touched, so the reference and its provenance are carried through unchanged. The previous values stay in the history with who, when and why.
/// </summary>
public sealed record DiagnosisAmendment(string? ToothKey, string? RegionKey, string? CodingSystem, string? Code, string? Source, string? SourceNote, string? TreatmentPlanReferenceState = null);
