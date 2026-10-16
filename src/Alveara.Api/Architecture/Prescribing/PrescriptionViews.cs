namespace Alveara.Api.Architecture.Prescribing;

/// <summary>A refusal with a stable code, a message that is safe to show, and (for a validation or a review refusal) the problems and the allergy assessment behind it.</summary>
public class PrescriptionException(string code, string message, int statusCode, IReadOnlyList<PrescriptionProblem>? problems = null, AllergyAssessment? assessment = null, object? detail = null) : Exception(message)
{
    /// <summary>Anything else the caller needs to act on the refusal (ALV-016-C01: what changed in the allergy and medication lists); null for a STORY-016 refusal.</summary>
    public object? Detail { get; } = detail;
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyList<PrescriptionProblem> Problems { get; } = problems ?? [];
    public AllergyAssessment? Assessment { get; } = assessment;
}

/// <summary>A saved prescription as it is read back, with who prescribed it and what the allergy check said when it was saved.</summary>
public sealed record PrescriptionView(
    Guid Id, Guid PatientId, string Medication, decimal DoseValue, string DoseUnit, string Frequency, string AllergyOutcome, string AllergyListState, string? AllergyMatches, string? ReviewNote,
    string? PrescribedByName, DateTimeOffset PrescribedAtUtc);

/// <summary>The answer to a save: the prescription, and whether it was already saved under the same key (a retry changes nothing).</summary>
public sealed record SavedPrescription(PrescriptionView Prescription, bool Replayed);

/// <summary>
/// What the screen is told before anything is saved: every structural problem with the entry, the allergy assessment (null while the medication itself is not valid), whether the prescriber's own review is
/// required, and the standing limitation texts. Reading it saves nothing and writes nothing.
/// </summary>
public sealed record PrescriptionPreview(IReadOnlyList<PrescriptionProblem> Problems, AllergyAssessment? Assessment, bool ReviewRequired, PrescriptionProblem? ReviewProblem, string DoseLimitation, string AllergyLimitation);

/// <summary>The closed lists and ceilings the structural rules use, for the screen's hints. The ceilings are typing-error ceilings, not clinical dose limits.</summary>
public sealed record PrescriptionOptions(IReadOnlyList<string> DoseUnits, IReadOnlyDictionary<string, decimal> TypingCeilings, IReadOnlyList<string> Frequencies, int DoseDecimals, int MedicationMax, int ReviewNoteMax,
    string DoseLimitation, string AllergyLimitation);
