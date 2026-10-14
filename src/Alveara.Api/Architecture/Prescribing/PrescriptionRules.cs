using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>A prescription entry as it arrives, before any of it is trusted.</summary>
public sealed record PrescriptionInput(string? Medication, decimal? DoseValue, string? DoseUnit, string? Frequency, string? ReviewNote, string? IdempotencyKey);

/// <summary>One thing wrong with a prescription entry: which field, a stable code, and a message that says what to correct.</summary>
public sealed record PrescriptionProblem(string Field, string Code, string Message);

/// <summary>A prescription entry after normalization: exactly what would be stored (the dose unit and the frequency are the canonical spellings).</summary>
public sealed record NormalizedPrescription(string Medication, decimal DoseValue, string DoseUnit, string Frequency, string? ReviewNote, string IdempotencyKey);

/// <summary>The outcome of judging an entry: every problem found, and the normalized entry only when there are none.</summary>
public sealed record PrescriptionCheck(IReadOnlyList<PrescriptionProblem> Problems, NormalizedPrescription? Value);

/// <summary>
/// STORY-016: what counts as a structurally valid prescription entry. Pure, no database, so the service, the API, the screen's hints and the tests all agree. An entry is judged as a whole: every problem
/// is reported together and one problem means nothing is accepted.
///
/// <b>What "dosage accuracy" means here, and what it does not.</b> There is no authoritative drug-knowledge source in this application, so it cannot know whether a dose is clinically right for a medication or
/// a patient. What it checks is the <b>structure</b> of what was typed, by rules written down here: the dose is a positive number with at most three decimal places, its unit is one of a closed list, the number is
/// not above a <b>typing-error ceiling</b> for that unit (so 5000 tablets or 500 g is caught as a slip), and the frequency is one of a closed list. The ceilings are deliberately generous and are NOT clinical
/// dose limits: a dose below them can still be wrong, and nothing here says otherwise. The screens say so in words.
///
/// The medication is free text (the application has no formulary): 2 to 200 characters of one line, trimmed, runs of spaces collapsed, made of letters, digits and the punctuation a drug name uses.
/// </summary>
public static partial class PrescriptionRules
{
    public const int MedicationMin = 2;
    public const int MedicationMax = 200;
    public const int ReviewNoteMax = 500;
    public const int KeyMax = 64;
    public const int DoseDecimals = 3;
    public const string HashVersion = "v1";

    /// <summary>The units a dose may be written in, with the largest single dose this screen accepts in each: a typing-error ceiling, NOT a clinical limit.</summary>
    public static readonly IReadOnlyDictionary<string, decimal> TypingCeilings = new Dictionary<string, decimal>
    {
        ["mg"] = 5000m, ["mcg"] = 5000m, ["g"] = 10m, ["mL"] = 100m, ["tablet"] = 10m, ["capsule"] = 10m, ["drop"] = 20m, ["puff"] = 20m, ["unit"] = 1000m,
    };

    public static IReadOnlyList<string> DoseUnits { get; } = TypingCeilings.Keys.ToList();

    public static readonly IReadOnlyList<string> Frequencies =
    [
        "Once", "Once daily", "Twice daily", "Three times daily", "Four times daily", "Every 4 hours", "Every 6 hours", "Every 8 hours", "Every 12 hours", "At bedtime", "As needed",
    ];

    /// <summary>The sentence the screens show beside the dose: what was checked, and what was not.</summary>
    public const string DoseCheckLimitation =
        "The dose is checked for its form only (a positive number, a known unit and a known frequency, and not above a typing-error ceiling for that unit). It is not checked against any clinical dose range: " +
        "this application has no drug-knowledge source, so a dose that passes can still be wrong for the medication or the patient.";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string NormalizeLine(string value) => Whitespace().Replace(value.Trim(), " ");

    private static bool IsMedicationChar(char c) => char.IsLetterOrDigit(c) || c is ' ' or '-' or '.' or ',' or '(' or ')' or '/' or '%' or '+' or '\'';

    private static string? Canonical(IEnumerable<string> allowed, string value) => allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

    public static PrescriptionCheck Check(PrescriptionInput? input)
    {
        if (input is null) return new([new("prescription", "required", "Enter a prescription before saving.")], null);
        var problems = new List<PrescriptionProblem>();

        string? medication = null;
        if (string.IsNullOrWhiteSpace(input.Medication)) problems.Add(new("medication", "required", "Enter the medication."));
        else if (input.Medication.Any(char.IsControl)) problems.Add(new("medication", "invalid_characters", "The medication must be a single line of text, without line breaks or control characters."));
        else
        {
            medication = NormalizeLine(input.Medication);
            if (medication.Length < MedicationMin) { problems.Add(new("medication", "too_short", $"The medication name needs at least {MedicationMin} characters.")); medication = null; }
            else if (medication.Length > MedicationMax) { problems.Add(new("medication", "too_long", $"The medication name can be at most {MedicationMax} characters; this one has {medication.Length}.")); medication = null; }
            else if (!medication.All(IsMedicationChar)) { problems.Add(new("medication", "invalid_characters", "The medication name can use letters, digits, spaces and - . , ( ) / % + ' only.")); medication = null; }
        }

        string? unit = null;
        if (string.IsNullOrWhiteSpace(input.DoseUnit)) problems.Add(new("doseUnit", "required", $"Choose the dose unit: {string.Join(", ", DoseUnits)}."));
        else
        {
            unit = Canonical(DoseUnits, input.DoseUnit.Trim());
            if (unit is null) problems.Add(new("doseUnit", "unknown_unit", $"\"{NormalizeLine(input.DoseUnit)}\" is not a unit this screen accepts. Choose one of: {string.Join(", ", DoseUnits)}."));
        }

        decimal? dose = null;
        if (input.DoseValue is null) problems.Add(new("doseValue", "required", "Enter the dose as a number."));
        else if (input.DoseValue <= 0m) problems.Add(new("doseValue", "not_positive", "The dose must be more than zero."));
        else if (decimal.Round(input.DoseValue.Value, DoseDecimals) != input.DoseValue.Value)
            problems.Add(new("doseValue", "too_many_decimals", $"The dose can have at most {DoseDecimals} decimal places; {input.DoseValue.Value.ToString(CultureInfo.InvariantCulture)} has more."));
        else if (unit is not null && input.DoseValue > TypingCeilings[unit])
            problems.Add(new("doseValue", "above_typing_ceiling",
                $"A single dose of {input.DoseValue.Value.ToString(CultureInfo.InvariantCulture)} {unit} is above the {TypingCeilings[unit].ToString(CultureInfo.InvariantCulture)} {unit} this screen accepts. Check the number and the unit. " +
                "This ceiling only catches typing mistakes; it is not a clinical dose limit."));
        else dose = input.DoseValue;

        string? frequency = null;
        if (string.IsNullOrWhiteSpace(input.Frequency)) problems.Add(new("frequency", "required", $"Choose how often: {string.Join(", ", Frequencies)}."));
        else
        {
            frequency = Canonical(Frequencies, NormalizeLine(input.Frequency));
            if (frequency is null) problems.Add(new("frequency", "unknown_frequency", $"\"{NormalizeLine(input.Frequency)}\" is not a frequency this screen accepts. Choose one of: {string.Join(", ", Frequencies)}."));
        }

        string? note = null;
        if (!string.IsNullOrWhiteSpace(input.ReviewNote))
        {
            if (input.ReviewNote.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) problems.Add(new("reviewNote", "invalid_characters", "The note cannot contain control characters."));
            else
            {
                note = input.ReviewNote.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                if (note.Length > ReviewNoteMax) { problems.Add(new("reviewNote", "too_long", $"The note can be at most {ReviewNoteMax} characters; this one has {note.Length}.")); note = null; }
            }
        }

        string? key = null;
        if (string.IsNullOrWhiteSpace(input.IdempotencyKey)) problems.Add(new("idempotencyKey", "required", "A save key is required so that a retry cannot save the prescription twice."));
        else if (input.IdempotencyKey.Trim().Length > KeyMax || input.IdempotencyKey.Any(char.IsControl)) problems.Add(new("idempotencyKey", "invalid", $"The save key must be 1 to {KeyMax} characters without control characters."));
        else key = input.IdempotencyKey.Trim();

        if (problems.Count > 0 || medication is null || unit is null || dose is null || frequency is null || key is null) return new(problems, null);
        return new(problems, new NormalizedPrescription(medication, dose.Value, unit, frequency, note, key));
    }

    /// <summary>
    /// What identifies a request, so that a retry with the same key replays and anything else under that key is a conflict. It binds the operation, the medication (case-insensitively), the dose, the unit, the
    /// frequency and the review note; the key itself is not part of it.
    /// </summary>
    public static string RequestHash(NormalizedPrescription value)
    {
        var canonical = string.Join('|', HashVersion, "prescribe", value.Medication.ToLowerInvariant(), value.DoseValue.ToString("0.###", CultureInfo.InvariantCulture), value.DoseUnit, value.Frequency, value.ReviewNote ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>
    /// The prescriber's explicit review, required whenever the allergy check found a match or could not say anything. Absent when the check found no recorded match. The note is the prescriber's own words and is
    /// stored with the entry; nothing proceeds silently.
    /// </summary>
    public static PrescriptionProblem? ReviewProblem(AllergyAssessment assessment, string? normalizedReviewNote)
    {
        if (assessment.Outcome == AllergyOutcomes.NoRecordedMatch || normalizedReviewNote is not null) return null;
        return assessment.Outcome == AllergyOutcomes.RecordedAllergyMatch
            ? new("reviewNote", "allergy_review_required", "This medication matches a recorded allergy. Say why you are prescribing it anyway, or change the medication.")
            : new("reviewNote", "allergy_status_review_required", "This patient's allergies are not established, so the allergy check cannot say anything. Confirm that you have asked the patient, in your own words, or review the allergies first.");
    }
}
