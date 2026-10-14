namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// STORY-016: one prescription entry, as the prescriber saved it. It is append-only: the database refuses any change or deletion (a correction belongs to the prescription lifecycle of a later story and
/// would be a new record that refers to this one). It keeps, beside what was prescribed, what the allergy check said at that moment (so the record shows what the prescriber was told), the prescriber's
/// own review note when the check required one, who saved it and when. The medication is free text: the application has no formulary and no drug-knowledge source.
/// </summary>
public class Prescription
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public required string Medication { get; set; }
    /// <summary>A positive number with at most three decimals, not above the typing-error ceiling of <see cref="DoseUnit"/> (see <see cref="PrescriptionRules"/>).</summary>
    public decimal DoseValue { get; set; }
    /// <summary>One of <see cref="PrescriptionRules.DoseUnits"/>.</summary>
    public required string DoseUnit { get; set; }
    /// <summary>One of <see cref="PrescriptionRules.Frequencies"/>.</summary>
    public required string Frequency { get; set; }
    /// <summary>One of the <see cref="AllergyOutcomes"/>: what the allergy check said when this was saved.</summary>
    public required string AllergyOutcome { get; set; }
    /// <summary>One of the <see cref="AllergyListStates"/>: how far the allergy list could be relied on then.</summary>
    public required string AllergyListState { get; set; }
    /// <summary>The recorded allergies that matched, in words, when the outcome is a match; null otherwise.</summary>
    public string? AllergyMatches { get; set; }
    /// <summary>The prescriber's own words, required whenever the outcome is anything but "no recorded match".</summary>
    public string? ReviewNote { get; set; }
    /// <summary>The caller's key for this save; unique per patient, so a retry cannot save the prescription twice.</summary>
    public required string IdempotencyKey { get; set; }
    /// <summary>SHA-256 (lower-case hexadecimal) of the canonical request (<see cref="PrescriptionRules.RequestHash"/>): the same key with a different request is a conflict.</summary>
    public required string RequestHash { get; set; }
    /// <summary>Carried through the audit entry and the logs so one save can be traced end to end.</summary>
    public Guid CorrelationId { get; set; }
    public Guid PrescriberUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
