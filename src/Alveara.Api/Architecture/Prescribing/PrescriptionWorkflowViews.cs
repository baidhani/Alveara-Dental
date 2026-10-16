using Alveara.Api.Architecture.Billing;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>What the application can and cannot say about a medication beyond structure. It is stated, not implied: no validated drug-knowledge source is configured.</summary>
public static class DecisionSupport
{
    public const string Message =
        "Drug interaction checking and clinical dose-appropriateness checking are not configured in this application. Nothing here checks them: a prescription that passes every check on this screen can still interact with " +
        "another medication or be the wrong dose.";
}

/// <summary>A draft as it is read back. <see cref="RowVersion"/> must be echoed by the next change so a change made by someone else is never overwritten.</summary>
public sealed record DraftView(
    Guid Id, Guid PatientId, string Medication, decimal DoseValue, string DoseUnit, string Frequency, string? Instructions, decimal? QuantityValue, string? QuantityUnit, int? Refills, string? ReviewNote,
    string Status, Guid? SupersedesPrescriptionId, Guid? FinalizedPrescriptionId, string? DiscardedReason, string SafetyFingerprint, string? CreatedByName, DateTimeOffset CreatedAtUtc,
    string? UpdatedByName, DateTimeOffset UpdatedAtUtc, string RowVersion);

/// <summary>The answer to creating a draft: the draft, and whether it already existed under the same key (a retry changes nothing).</summary>
public sealed record SavedDraft(DraftView Draft, bool Replayed);

/// <summary>
/// How the allergy and medication lists stand against what the prescriber last acknowledged: the live fingerprint, whether it differs from the draft's, what was added and removed in words, the allergy
/// assessment for the draft's medication, and whether the prescriber's own review is required. Reading it changes nothing.
/// </summary>
public sealed record SafetyCheck(
    string Fingerprint, bool ChangedSinceAcknowledged, IReadOnlyList<string> Added, IReadOnlyList<string> Removed, AllergyAssessment Assessment, bool ReviewRequired, PrescriptionProblem? ReviewProblem);

/// <summary>A draft with its safety check against the live lists.</summary>
public sealed record DraftDetailView(DraftView Draft, SafetyCheck Safety);

/// <summary>
/// What finalizing a draft would meet right now, without changing anything: the structural problems (including the parts only finalizing requires), the safety check, and the standing limitation texts.
/// </summary>
public sealed record DraftCheck(IReadOnlyList<PrescriptionProblem> Problems, SafetyCheck Safety, bool CanFinalize, string DoseLimitation, string QuantityLimitation, string AllergyLimitation, string DecisionSupport);

/// <summary>Something that happened to a finalized prescription, who did it and when.</summary>
public sealed record PrescriptionEventView(string EventType, string? Reason, string? ActorName, DateTimeOffset OccurredAtUtc, Guid? SupersededByPrescriptionId);

/// <summary>
/// A finalized prescription as it is read back: what STORY-016 recorded, what finalizing added (null for a prescription saved by STORY-016, which has no instructions, quantity or refills), its status and its events.
/// </summary>
public sealed record PrescriptionRecord(
    Guid Id, Guid PatientId, string Medication, decimal DoseValue, string DoseUnit, string Frequency, string? Instructions, decimal? QuantityValue, string? QuantityUnit, int? Refills,
    string AllergyOutcome, string AllergyListState, string? AllergyMatches, string? ReviewNote, string? PrescribedByName, DateTimeOffset PrescribedAtUtc, string Status,
    Guid? SupersedesPrescriptionId, Guid? SupersededByPrescriptionId, bool HasDetails, IReadOnlyList<PrescriptionEventView> Events);

/// <summary>The answer to finalizing, cancelling or discontinuing: the record, and whether it was already so (a retry changes nothing).</summary>
public sealed record SavedRecord(PrescriptionRecord Record, bool Replayed);

/// <summary>The closed lists, ceilings and standing limitation texts the composer shows. No drug-knowledge source is configured, and that is part of the answer.</summary>
public sealed record WorkflowOptions(
    IReadOnlyList<string> DoseUnits, IReadOnlyDictionary<string, decimal> DoseTypingCeilings, IReadOnlyList<string> Frequencies, IReadOnlyList<string> QuantityUnits, decimal QuantityTypingCeiling, int RefillsMax,
    int InstructionsMax, int MedicationMax, int ReasonMax, bool DecisionSupportConfigured, string DecisionSupportMessage, string DoseLimitation, string QuantityLimitation, string AllergyLimitation);

/// <summary>What changed in the allergy and medication lists since the prescriber acknowledged them: the payload of a <c>safety_context_changed</c> refusal.</summary>
public sealed record SafetyChange(string CurrentFingerprint, IReadOnlyList<string> Added, IReadOnlyList<string> Removed);

/// <summary>
/// A finalized prescription laid out to be printed, built on the server from the immutable record (nothing on the screen is added up or re-derived). <see cref="ContentHash"/> identifies exactly this content, so
/// a reprint of an unchanged prescription is identical. A cancelled, discontinued or superseded prescription says so on its face.
/// </summary>
public sealed record PrescriptionPrintContent(
    Guid PrescriptionId, string Status, DocumentPractice? Practice, string PatientName, string PatientDateOfBirth, string PrescriberName, string Medication, string Dose, string Frequency, string Instructions,
    string Quantity, int Refills, string PrescribedAtUtc, string AllergyStatement, string LimitationStatement, Guid? SupersedesPrescriptionId, Guid? SupersededByPrescriptionId, string ContentHash);
