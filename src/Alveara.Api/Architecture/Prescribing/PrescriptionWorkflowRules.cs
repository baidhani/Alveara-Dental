using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>A prescription draft or finalization as it arrives, before any of it is trusted: the STORY-016 entry plus what a prescription needs to be dispensed.</summary>
public sealed record WorkflowInput(
    string? Medication, decimal? DoseValue, string? DoseUnit, string? Frequency, string? Instructions, decimal? QuantityValue, string? QuantityUnit, int? Refills, string? ReviewNote, string? IdempotencyKey);

/// <summary>A draft after normalization: exactly what would be stored. Instructions, quantity and refills may still be absent on a draft; they are required to finalize.</summary>
public sealed record NormalizedWorkflow(
    string Medication, decimal DoseValue, string DoseUnit, string Frequency, string? Instructions, decimal? QuantityValue, string? QuantityUnit, int? Refills, string? ReviewNote, string IdempotencyKey);

/// <summary>The outcome of judging an entry: every problem found, and the normalized entry only when there are none.</summary>
public sealed record WorkflowCheck(IReadOnlyList<PrescriptionProblem> Problems, NormalizedWorkflow? Value);

/// <summary>The states of a draft. A draft is the only mutable record of the workflow, and only while it is a Draft.</summary>
public static class DraftStatuses
{
    public const string Draft = "Draft";
    public const string Finalized = "Finalized";
    public const string Discarded = "Discarded";
    public static readonly IReadOnlyList<string> All = [Draft, Finalized, Discarded];
}

/// <summary>What can happen to a finalized prescription. Each is an appended event, never an edit of the prescription.</summary>
public static class PrescriptionEventTypes
{
    public const string Finalized = "Finalized";
    public const string Cancelled = "Cancelled";
    public const string Discontinued = "Discontinued";
    public const string Superseded = "Superseded";
    public static readonly IReadOnlyList<string> All = [Finalized, Cancelled, Discontinued, Superseded];
    /// <summary>The events that end a prescription: at most one of them can ever be recorded for a prescription.</summary>
    public static readonly IReadOnlyList<string> Ending = [Cancelled, Discontinued, Superseded];
}

/// <summary>The status of a finalized prescription, worked out from its events. A prescription saved by STORY-016 has no events and is Active.</summary>
public static class PrescriptionStatuses
{
    public const string Active = "Active";
    public const string Cancelled = "Cancelled";
    public const string Discontinued = "Discontinued";
    public const string Superseded = "Superseded";

    public static string From(IEnumerable<string> eventTypes) =>
        eventTypes.LastOrDefault(t => PrescriptionEventTypes.Ending.Contains(t)) ?? Active;

    /// <summary>Only an Active prescription can be cancelled, discontinued or superseded; once ended it stays ended.</summary>
    public static bool CanEnd(string status) => status == Active;
}

/// <summary>
/// ALV-016-C01: what counts as a complete, structurally valid prescription for finalization. Pure, no database. The STORY-016 rules judge the medication, dose, unit, frequency, review note and key (they are reused,
/// not copied, so the two can never disagree); this adds the instructions, the quantity dispensed (a number and a unit) and the number of refills.
///
/// <b>What these rules are not.</b> The limits here (a quantity of at most 1000 and at most 12 refills) are typing-error ceilings in the same sense as the dose ceilings: they catch a slipped key, they are not clinical
/// or legal limits (no controlled-substance or jurisdiction rule is modelled), and the screens say so. There is still no drug-knowledge source, so nothing here checks interactions or whether a dose is clinically right.
/// A draft may leave the instructions, quantity and refills empty; finalizing may not.
/// </summary>
public static class PrescriptionWorkflowRules
{
    public const int InstructionsMax = 500;
    public const int QuantityDecimals = 2;
    public const decimal QuantityTypingCeiling = 1000m;
    public const int RefillsMax = 12;
    public const string HashVersion = "w1";

    /// <summary>The units a quantity may be written in.</summary>
    public static readonly IReadOnlyList<string> QuantityUnits = ["tablet", "capsule", "mL", "g", "tube", "bottle", "inhaler", "unit"];

    public const string QuantityLimitation =
        "The quantity and the number of refills are checked for their form only (a positive number in a known unit, a whole number of refills, and not above a typing-error ceiling). They are not checked against any clinical, legal or " +
        "regulatory limit: this application has no drug-knowledge source and models no controlled-substance rule.";

    private static string? Canonical(IEnumerable<string> allowed, string value) => allowed.FirstOrDefault(a => string.Equals(a, value, StringComparison.OrdinalIgnoreCase));

    /// <summary>Judges an entry for saving as a draft: the core fields must be valid, and any instructions, quantity or refills that were given must be well formed.</summary>
    public static WorkflowCheck CheckDraft(WorkflowInput? input)
    {
        if (input is null) return new([new("prescription", "required", "Enter a prescription before saving.")], null);
        var core = PrescriptionRules.Check(new PrescriptionInput(input.Medication, input.DoseValue, input.DoseUnit, input.Frequency, input.ReviewNote, input.IdempotencyKey));
        var problems = core.Problems.ToList();

        string? instructions = null;
        if (!string.IsNullOrWhiteSpace(input.Instructions))
        {
            if (input.Instructions.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) problems.Add(new("instructions", "invalid_characters", "The instructions cannot contain control characters."));
            else
            {
                instructions = input.Instructions.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
                if (instructions.Length > InstructionsMax) { problems.Add(new("instructions", "too_long", $"The instructions can be at most {InstructionsMax} characters; these have {instructions.Length}.")); instructions = null; }
            }
        }

        string? quantityUnit = null;
        if (!string.IsNullOrWhiteSpace(input.QuantityUnit))
        {
            quantityUnit = Canonical(QuantityUnits, PrescriptionRules.NormalizeLine(input.QuantityUnit));
            if (quantityUnit is null) problems.Add(new("quantityUnit", "unknown_unit", $"\"{PrescriptionRules.NormalizeLine(input.QuantityUnit)}\" is not a quantity unit this screen accepts. Choose one of: {string.Join(", ", QuantityUnits)}."));
        }
        decimal? quantity = null;
        if (input.QuantityValue is { } q)
        {
            if (q <= 0m) problems.Add(new("quantityValue", "not_positive", "The quantity must be more than zero."));
            else if (decimal.Round(q, QuantityDecimals) != q) problems.Add(new("quantityValue", "too_many_decimals", $"The quantity can have at most {QuantityDecimals} decimal places."));
            else if (q > QuantityTypingCeiling)
                problems.Add(new("quantityValue", "above_typing_ceiling", $"A quantity of {q.ToString(CultureInfo.InvariantCulture)} is above the {QuantityTypingCeiling.ToString(CultureInfo.InvariantCulture)} this screen accepts. Check the number. This ceiling only catches typing mistakes; it is not a clinical or legal limit."));
            else quantity = q;
        }
        if (input.QuantityValue is not null && string.IsNullOrWhiteSpace(input.QuantityUnit)) problems.Add(new("quantityUnit", "required", $"Choose the quantity unit: {string.Join(", ", QuantityUnits)}."));
        if (input.QuantityValue is null && !string.IsNullOrWhiteSpace(input.QuantityUnit)) problems.Add(new("quantityValue", "required", "Enter the quantity as a number, or clear the unit."));

        int? refills = null;
        if (input.Refills is { } r)
        {
            if (r < 0) problems.Add(new("refills", "negative", "The number of refills cannot be negative."));
            else if (r > RefillsMax) problems.Add(new("refills", "above_typing_ceiling", $"{r} refills is above the {RefillsMax} this screen accepts. Check the number. This ceiling only catches typing mistakes; it is not a legal limit."));
            else refills = r;
        }

        if (problems.Count > 0 || core.Value is not { } v) return new(problems, null);
        return new(problems, new NormalizedWorkflow(v.Medication, v.DoseValue, v.DoseUnit, v.Frequency, instructions, quantity, quantityUnit, refills, v.ReviewNote, v.IdempotencyKey));
    }

    /// <summary>Judges an entry for finalization: everything a draft needs, and the instructions, the quantity with its unit and the refills as well. Each missing one is named.</summary>
    public static WorkflowCheck CheckFinal(WorkflowInput? input)
    {
        var draft = CheckDraft(input);
        if (input is null) return draft;
        var problems = draft.Problems.ToList();
        if (string.IsNullOrWhiteSpace(input.Instructions)) problems.Add(new("instructions", "required", "Write the instructions for the patient (how to take it, for how long)."));
        if (input.QuantityValue is null && string.IsNullOrWhiteSpace(input.QuantityUnit)) { problems.Add(new("quantityValue", "required", "Enter the quantity to dispense.")); problems.Add(new("quantityUnit", "required", $"Choose the quantity unit: {string.Join(", ", QuantityUnits)}.")); }
        if (input.Refills is null) problems.Add(new("refills", "required", $"Enter the number of refills (0 to {RefillsMax}; 0 means none)."));
        if (problems.Count > draft.Problems.Count)
            return new(problems.GroupBy(p => (p.Field, p.Code)).Select(g => g.First()).ToList(), null);
        return draft;
    }

    /// <summary>True when a normalized entry has everything finalization needs.</summary>
    public static bool IsComplete(NormalizedWorkflow v) => v.Instructions is not null && v.QuantityValue is not null && v.QuantityUnit is not null && v.Refills is not null;

    /// <summary>
    /// What identifies a draft request, so that a retry with the same key replays and anything else under that key is a conflict. It binds every field except the key itself.
    /// </summary>
    public static string RequestHash(NormalizedWorkflow v)
    {
        var canonical = string.Join('|', HashVersion, "draft", v.Medication.ToLowerInvariant(), v.DoseValue.ToString("0.###", CultureInfo.InvariantCulture), v.DoseUnit, v.Frequency,
            v.Instructions ?? "", v.QuantityValue?.ToString("0.##", CultureInfo.InvariantCulture) ?? "", v.QuantityUnit ?? "", v.Refills?.ToString(CultureInfo.InvariantCulture) ?? "", v.ReviewNote ?? "");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    /// <summary>What identifies the request to amend one particular prescription, so a retry under the same key replays and the same key for another prescription is a conflict.</summary>
    public static string AmendHash(Guid prescriptionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('|', HashVersion, "amend", prescriptionId.ToString("N"))))).ToLowerInvariant();

    /// <summary>The STORY-016 entry inside a complete draft, so finalization runs exactly the STORY-016 allergy check and review rule on it.</summary>
    public static NormalizedPrescription CoreOf(NormalizedWorkflow v) => new(v.Medication, v.DoseValue, v.DoseUnit, v.Frequency, v.ReviewNote, v.IdempotencyKey);

    /// <summary>A reason for cancelling or discontinuing: required, one piece of text, at most <see cref="PrescriptionRules.ReviewNoteMax"/> characters. Returns the normalized reason or the problem.</summary>
    public static (string? Reason, PrescriptionProblem? Problem) CheckReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return (null, new("reason", "required", "Say why, in your own words. It is kept with the record."));
        if (reason.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t'))) return (null, new("reason", "invalid_characters", "The reason cannot contain control characters."));
        var text = reason.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        return text.Length > PrescriptionRules.ReviewNoteMax ? (null, new("reason", "too_long", $"The reason can be at most {PrescriptionRules.ReviewNoteMax} characters; this one has {text.Length}.")) : (text, null);
    }
}

/// <summary>One recorded fact the prescriber saw: an allergy or a current medication, as the safety context showed it.</summary>
public sealed record SnapshotItem(string Category, string Title, string? Severity, string? Detail, string Status);

/// <summary>
/// ALV-016-C01: the allergy and medication lists a prescriber saw, reduced to a small, ordered set of lines and a fingerprint of them. Finalization recomputes it from the live safety context: when it differs, the lists
/// changed since the prescriber looked, and the prescriber is told what changed instead of finalizing against a stale view. Pure, so the comparison is the same wherever it runs.
/// </summary>
public static class SafetySnapshot
{
    public const string Version = "s1";

    /// <summary>One line per active allergy and per current medication, plus one line each for how far the two lists can be relied on, in a fixed order.</summary>
    public static IReadOnlyList<string> Lines(IEnumerable<SnapshotItem> items, string allergyListStatus, string medicationListStatus) =>
        items.Select(i => string.Join('|', i.Category, i.Title.Trim(), i.Severity ?? "", (i.Detail ?? "").Trim(), i.Status))
             .Concat([$"AllergyList|{allergyListStatus}", $"MedicationList|{medicationListStatus}"])
             .OrderBy(l => l, StringComparer.Ordinal).ToList();

    public static string Fingerprint(IReadOnlyList<string> lines) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version + "\n" + string.Join('\n', lines)))).ToLowerInvariant();

    public static string Join(IReadOnlyList<string> lines) => string.Join('\n', lines);
    public static IReadOnlyList<string> Split(string text) => text.Length == 0 ? [] : text.Split('\n');

    /// <summary>What was added to and removed from the lists since the prescriber looked, as readable lines.</summary>
    public static (IReadOnlyList<string> Added, IReadOnlyList<string> Removed) Diff(IReadOnlyList<string> seen, IReadOnlyList<string> now) =>
        (now.Except(seen, StringComparer.Ordinal).Select(Describe).ToList(), seen.Except(now, StringComparer.Ordinal).Select(Describe).ToList());

    /// <summary>How far a list can be relied on, in words: the section statuses the clinical record uses, spelled out.</summary>
    private static string StatusWords(string status) => status switch
    {
        "NeedsReview" => "changed since it was last confirmed, so it needs review",
        "NotReviewed" => "never reviewed",
        "NoneKnown" => "none known",
        _ => status.ToLowerInvariant(),
    };

    private static string Describe(string line)
    {
        var p = line.Split('|');
        if (p[0] is "AllergyList" or "MedicationList") return $"{(p[0] == "AllergyList" ? "Allergy list" : "Medication list")}: {StatusWords(p[1])}";
        var what = $"{p[0]}: {p[1]}";
        if (p.Length > 2 && p[2].Length > 0) what += $" ({p[2].ToLowerInvariant()})";
        if (p.Length > 3 && p[3].Length > 0) what += $", {p[3]}";
        return what;
    }
}
