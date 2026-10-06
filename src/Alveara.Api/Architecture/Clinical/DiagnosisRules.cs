using System.Text.RegularExpressions;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>A diagnosis entry as it arrives, before any of it is trusted.</summary>
public sealed record DiagnosisInput(Guid EncounterId, string? Label, string? ToothKey, string? Notes, string? TreatmentPlanReference,
    string? CodingSystem = null, string? Code = null, string? Source = null, string? SourceNote = null, string? RegionKey = null, string? TreatmentPlanReferenceState = null);

/// <summary>One thing wrong with a diagnosis entry: which field, a stable code, and a message that says what to correct.</summary>
public sealed record DiagnosisProblem(string Field, string Code, string Message);

/// <summary>A diagnosis entry after normalization: exactly what would be stored.</summary>
public sealed record NormalizedDiagnosis(Guid EncounterId, string Label, string? ToothKey, string? Notes, string? TreatmentPlanReference,
    string? CodingSystem = null, string? Code = null, string Source = DiagnosisSources.Manual, string? SourceNote = null, string? RegionKey = null);

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
    public const int CodeMax = 30;
    public const int SourceNoteMax = 200;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>One line of text: trimmed, runs of whitespace (spaces, non-breaking spaces and the like) collapsed to one space. Control characters are not handled here; callers refuse them first.</summary>
    public static string NormalizeLine(string value) => Whitespace().Replace(value.Trim(), " ");

    /// <summary>Text that may span lines: line endings made uniform and the ends trimmed, nothing else changed.</summary>
    public static string NormalizeNotes(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private static bool IsCodeChar(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_';

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

        // ALV-013-C01: a treatment-plan reference can only be Unresolved until treatment plans exist, so a request that tries to mark it as anything else is refused, not quietly ignored
        if (input.TreatmentPlanReferenceState is not null && input.TreatmentPlanReferenceState != TreatmentPlanReferenceStates.Unresolved)
            problems.Add(new("treatmentPlanReferenceState", "not_supported", "A treatment-plan reference cannot be resolved or validated yet: treatment plans do not exist, so it stays unresolved."));

        // ALV-013-C01: optional coding. A diagnosis needs none; when it names a system it must give the code, and the other way round. Only the system's NAME is checked, never the code against a code set.
        string? system = null, code = null;
        var hasSystem = input.CodingSystem is not null;
        var hasCode = input.Code is not null;
        if (hasSystem)
        {
            if (!DiagnosisCodingSystems.All.Contains(input.CodingSystem!)) problems.Add(new("codingSystem", "unsupported_system", $"\"{input.CodingSystem}\" is not a supported coding system. Use one of {string.Join(", ", DiagnosisCodingSystems.All)}, or leave the coding empty."));
            else system = input.CodingSystem;
        }
        if (hasCode)
        {
            var c = input.Code!.Trim();
            if (c.Length == 0) problems.Add(new("code", "blank", "The code is blank. Enter the code, or leave the coding empty."));
            else if (c.Length > CodeMax) problems.Add(new("code", "too_long", $"The code can be at most {CodeMax} characters; this one has {c.Length}."));
            else if (!c.All(IsCodeChar)) problems.Add(new("code", "invalid_characters", "A code can contain only letters, digits, dots, hyphens and underscores, with no spaces."));
            else code = c;
        }
        if (hasSystem != hasCode) problems.Add(new(hasSystem ? "code" : "codingSystem", "coding_incomplete", hasSystem ? "A coding system needs its code. Enter the code, or remove the system." : "A code needs its coding system. Choose the system, or remove the code."));

        string source = DiagnosisSources.Manual;
        if (input.Source is not null)
        {
            if (!DiagnosisSources.All.Contains(input.Source)) problems.Add(new("source", "unsupported_source", $"\"{input.Source}\" is not a source. Use one of {string.Join(", ", DiagnosisSources.All)}."));
            else source = input.Source;
        }

        string? sourceNote = null;
        if (input.SourceNote is not null)
        {
            if (HasControl(input.SourceNote, allowLineBreaks: false)) problems.Add(new("sourceNote", "invalid_characters", "The source note must be a single line of text, without line breaks or control characters."));
            else
            {
                var n = NormalizeLine(input.SourceNote);
                if (n.Length == 0) problems.Add(new("sourceNote", "blank", "The source note is blank. Enter it, or leave it out."));
                else if (n.Length > SourceNoteMax) problems.Add(new("sourceNote", "too_long", $"The source note can be at most {SourceNoteMax} characters; this one has {n.Length}."));
                else if (input.Source is null or DiagnosisSources.Manual) problems.Add(new("sourceNote", "source_required", "A source note says where an imported or mapped diagnosis came from; choose Imported or Mapped, or remove the note."));
                else sourceNote = n;
            }
        }

        string? region = null;
        if (input.RegionKey is not null)
        {
            if (!DiagnosisRegions.All.Contains(input.RegionKey)) problems.Add(new("regionKey", "unknown_region", $"\"{input.RegionKey}\" is not an oral region. Use one of {string.Join(", ", DiagnosisRegions.All)}, or leave the region empty."));
            else if (input.ToothKey is not null) problems.Add(new("regionKey", "conflicts_with_tooth", "A diagnosis is about one tooth or about a region, not both. Remove the tooth or the region."));
            else region = input.RegionKey;
        }

        return problems.Count > 0 ? new(problems, null) : new(problems, new NormalizedDiagnosis(input.EncounterId, label!, tooth, notes, reference, system, code, source, sourceNote, region));
    }
}
