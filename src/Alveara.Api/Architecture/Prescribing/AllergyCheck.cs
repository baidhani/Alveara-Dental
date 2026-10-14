using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>One active allergy as the patient's clinical record states it.</summary>
public sealed record RecordedAllergy(string Name, string? Severity, string? Reaction);

/// <summary>How much the allergy list can be relied on to be the whole story.</summary>
public static class AllergyListStates
{
    /// <summary>A clinician reviewed the list, or recorded that none is known.</summary>
    public const string Established = "Established";
    /// <summary>Allergies are listed but were not confirmed after they last changed.</summary>
    public const string Unconfirmed = "Unconfirmed";
    /// <summary>Nobody has reviewed it, or a clinician recorded that it is unknown.</summary>
    public const string NotEstablished = "NotEstablished";

    /// <summary>The state for a clinical-record section status (<c>Reviewed</c>, <c>NoneKnown</c>, <c>NeedsReview</c>, <c>Unknown</c>, <c>NotReviewed</c>); anything unrecognized is not established.</summary>
    public static string From(string? sectionStatus) => sectionStatus switch
    {
        "Reviewed" or "NoneKnown" => Established,
        "NeedsReview" => Unconfirmed,
        _ => NotEstablished,
    };
}

public static class AllergyOutcomes
{
    /// <summary>The medication's name matches an allergy recorded for this patient.</summary>
    public const string RecordedAllergyMatch = "RecordedAllergyMatch";
    /// <summary>No recorded allergy matches by name, and the list is established. This is a name comparison, not a clearance.</summary>
    public const string NoRecordedMatch = "NoRecordedMatch";
    /// <summary>No recorded allergy matches, but the list is not established (never reviewed, unknown, or changed since it was confirmed), so the check cannot say anything.</summary>
    public const string NotEstablished = "NotEstablished";
}

/// <summary>The result of the allergy check: the outcome, the recorded allergies that matched, the state of the list, a sentence for the screen and the standing limitation.</summary>
public sealed record AllergyAssessment(string Outcome, IReadOnlyList<RecordedAllergy> Matches, string ListState, string Message, string Limitation);

/// <summary>
/// STORY-016: the allergy check. Pure and deterministic. It compares the NAME of the medication with the names of the patient's active recorded allergies, nothing more.
///
/// A match is when every word of one name appears among the words of the other (case and punctuation ignored): "Penicillin" matches "Penicillin V Potassium", "Amoxicillin 500 mg" matches "amoxicillin", and
/// "Penicillin V" does not match "Penicillin G". It does <b>not</b> know drug families, brand names or cross-reactions, so it does not know that amoxicillin is a penicillin; a result of "no recorded match" is
/// never a clearance, and the limitation sentence travels with every result. The check never invents a fact: an allergy list nobody has reviewed is reported as <i>not established</i>, never as clear.
/// </summary>
public static partial class AllergyCheck
{
    public const string Limitation =
        "This check compares the medication's name with the allergies recorded for this patient. It does not know drug families, brand names or cross-reactions (for example, it does not know that amoxicillin is a penicillin), " +
        "so \"no recorded match\" is not a clearance. Check the patient's history yourself.";

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotLettersOrDigits();

    /// <summary>The words of a name: lower case, punctuation removed, in the order given, without duplicates.</summary>
    public static IReadOnlyList<string> Words(string name) =>
        NotLettersOrDigits().Split(name.ToLowerInvariant()).Where(w => w.Length > 0).Distinct().ToList();

    private static bool Matches(IReadOnlyList<string> medication, IReadOnlyList<string> allergy) =>
        medication.Count > 0 && allergy.Count > 0 && (allergy.All(medication.Contains) || medication.All(allergy.Contains));

    public static AllergyAssessment Assess(string medication, IReadOnlyList<RecordedAllergy> activeAllergies, string listState)
    {
        var words = Words(medication);
        var matches = activeAllergies.Where(a => Matches(words, Words(a.Name))).OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (matches.Count > 0)
            return new(AllergyOutcomes.RecordedAllergyMatch, matches, listState,
                $"This medication matches {(matches.Count == 1 ? "an allergy" : "allergies")} recorded for this patient: {string.Join("; ", matches.Select(Describe))}.", Limitation);
        if (listState == AllergyListStates.Established)
            return new(AllergyOutcomes.NoRecordedMatch, [], listState, "No allergy recorded for this patient matches this medication's name. This is a name comparison only, not a clearance.", Limitation);
        return new(AllergyOutcomes.NotEstablished, [], listState,
            listState == AllergyListStates.Unconfirmed
                ? "This patient's allergy list has changed since it was last confirmed, so the allergy check cannot say anything. Nothing listed here does not mean none."
                : "This patient's allergies have not been reviewed (or are recorded as unknown), so the allergy check cannot say anything. Nothing listed here does not mean none.",
            Limitation);
    }

    private static string Describe(RecordedAllergy a) =>
        a.Name + (a.Severity is null ? " (severity not recorded)" : $" ({a.Severity.ToLowerInvariant()})") + (string.IsNullOrWhiteSpace(a.Reaction) ? "" : $", reaction: {a.Reaction}");
}
