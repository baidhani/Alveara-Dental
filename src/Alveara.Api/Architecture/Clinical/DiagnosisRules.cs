using System.Text.RegularExpressions;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>A diagnosis entry as it arrives, before any of it is trusted.</summary>
public sealed record DiagnosisInput(Guid EncounterId, string? Label, string? ToothKey, string? Notes, string? TreatmentPlanReference);

/// <summary>One thing wrong with a diagnosis entry: which field, a stable code, and a message that says what to correct.</summary>
public sealed record DiagnosisProblem(string Field, string Code, string Message);

/// <summary>A diagnosis entry after normalization: exactly what would be stored.</summary>
public sealed record NormalizedDiagnosis(Guid EncounterId, string Label, string? ToothKey, string? Notes, string? TreatmentPlanReference);

/// <summary>The outcome of judging an entry: every problem found, and the normalized entry only when there are none.</summary>
public sealed record DiagnosisCheck(IReadOnlyList<DiagnosisProblem> Problems, NormalizedDiagnosis? Value);

/// <summary>
/// STORY-013: what counts as a valid structured diagnosis entry. Pure, no database, so the service, the API, the screen's hints and the tests all agree. An entry is judged as a whole: every problem is
/// reported together and one problem means nothing is accepted.
///
/// The fields: the <b>encounter</b> (required; that it exists and belongs to the same patient is checked where the database is, not here), a <b>label</b> (required, 1 to 200 characters, one line), an
/// optional <b>tooth</b> (one of the odontogram's FDI keys), optional <b>notes</b> (up to 1,000 characters, may span lines), and an optional <b>treatment-plan reference</b>.
///
/// The treatment-plan reference is a FORWARD reference: treatment plans do not exist yet (they belong to a later story), so it is an opaque, bounded, normalized piece of text kept beside the
/// diagnosis in the style of the odontogram's and the periodontal chart's links. Nothing here looks it up or treats it as proof that a plan exists. Absent (null) means no reference; a reference
/// that is present but blank is refused rather than silently dropped, because someone meant to say something. It is stored exactly as normalized (trimmed, runs of spaces collapsed, no control characters).
/// </summary>
public static partial class DiagnosisRules
{
    public const int LabelMax = 200;
    public const int NotesMax = 1000;
    public const int ReferenceMax = 100;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>One line of text: trimmed, runs of whitespace (spaces, non-breaking spaces and the like) collapsed to one space. Control characters are not handled here; callers refuse them first.</summary>
    public static string NormalizeLine(string value) => Whitespace().Replace(value.Trim(), " ");

    /// <summary>Text that may span lines: line endings made uniform and the ends trimmed, nothing else changed.</summary>
    public static string NormalizeNotes(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static bool HasControl(string value, bool allowLineBreaks) => value.Any(c => char.IsControl(c) && !(allowLineBreaks && c is '\n' or '\r' or '\t'));

    public static DiagnosisCheck Check(DiagnosisInput? input)
    {
        if (input is null) return new([new("diagnosis", "required", "Enter a diagnosis before saving.")], null);
        var problems = new List<DiagnosisProblem>();

        if (input.EncounterId == Guid.Empty) problems.Add(new("encounterId", "required", "Choose the encounter this diagnosis belongs to."));

        string? label = null;
        if (string.IsNullOrWhiteSpace(input.Label)) problems.Add(new("label", "required", "Enter the diagnosis."));
        else if (HasControl(input.Label, allowLineBreaks: false)) problems.Add(new("label", "invalid_characters", "The diagnosis must be a single line of text, without line breaks or control characters."));
        else
        {
            label = NormalizeLine(input.Label);
            if (label.Length > LabelMax) { problems.Add(new("label", "too_long", $"The diagnosis can be at most {LabelMax} characters; this one has {label.Length}. Shorten it and put the detail in the notes.")); label = null; }
        }

        string? tooth = null;
        if (input.ToothKey is not null)
        {
            if (!ToothKeys.IsValid(input.ToothKey)) problems.Add(new("toothKey", "unknown_tooth", $"\"{input.ToothKey}\" is not a tooth. Use the two-digit FDI number, such as 16 or 55, or leave the tooth empty."));
            else tooth = input.ToothKey;
        }

        string? notes = null;
        if (input.Notes is not null)
        {
            if (HasControl(input.Notes, allowLineBreaks: true)) problems.Add(new("notes", "invalid_characters", "The notes contain a character that cannot be saved. Remove it and try again."));
            else
            {
                var n = NormalizeNotes(input.Notes);
                if (n.Length > NotesMax) problems.Add(new("notes", "too_long", $"The notes can be at most {NotesMax} characters; these have {n.Length}."));
                else notes = n.Length == 0 ? null : n;
            }
        }

        string? reference = null;
        if (input.TreatmentPlanReference is not null)
        {
            if (HasControl(input.TreatmentPlanReference, allowLineBreaks: false)) problems.Add(new("treatmentPlanReference", "invalid_characters", "The treatment-plan reference must be a single line of text, without line breaks or control characters."));
            else
            {
                var r = NormalizeLine(input.TreatmentPlanReference);
                if (r.Length == 0) problems.Add(new("treatmentPlanReference", "blank", "The treatment-plan reference is blank. Enter the reference, or leave the field out if there is none."));
                else if (r.Length > ReferenceMax) problems.Add(new("treatmentPlanReference", "too_long", $"The treatment-plan reference can be at most {ReferenceMax} characters; this one has {r.Length}."));
                else reference = r;
            }
        }

        return problems.Count > 0 ? new(problems, null) : new(problems, new NormalizedDiagnosis(input.EncounterId, label!, tooth, notes, reference));
    }
}
