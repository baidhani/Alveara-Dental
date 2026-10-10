using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>A phase after every rule has accepted it.</summary>
public sealed record LayoutPhase(int Number, string Name, string? PlannedVisit, DateOnly? TargetDate);

/// <summary>
/// ALV-015-C01: the content a revision holds, in the form the rules and the comparison work with: the rationale, the patient-facing explanation, the phases and which phase each procedure sits in.
/// Two layouts are the same plan content exactly when they are equal (see <see cref="SameAs"/>).
/// </summary>
public sealed record RevisionLayout(string? Rationale, string? Explanation, IReadOnlyList<LayoutPhase> Phases, IReadOnlyDictionary<Guid, int?> Placement)
{
    public bool SameAs(RevisionLayout other) =>
        Rationale == other.Rationale && Explanation == other.Explanation && Phases.SequenceEqual(other.Phases)
        && Placement.Count == other.Placement.Count && Placement.All(p => other.Placement.TryGetValue(p.Key, out var phase) && phase == p.Value);
}

/// <summary>
/// ALV-015-C01: what an explicit revision accepts. Like the rest of the plan rules, every wrong field is named in one answer before anything is stored: the keys are <c>reason</c>,
/// <c>clinicalRationale</c>, <c>patientExplanation</c>, <c>phases</c>, <c>phases[0].number</c> (and <c>.name</c>, <c>.plannedVisit</c>, <c>.targetDate</c>) and <c>placements[0].itemId</c> (and <c>.phaseNumber</c>).
/// </summary>
public static class TreatmentPlanRevisionRules
{
    private static readonly Regex ControlCharacter = new("[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F\\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex OneLineControl = new("[\\x00-\\x1F\\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Text that may span lines. Null means keep what the plan has; blank clears it (returned as the empty string). Line endings are made uniform and the ends trimmed.
    /// </summary>
    public static string? CleanText(IDictionary<string, string> errors, string field, string? value, int max)
    {
        if (value is null) return null;
        if (ControlCharacter.IsMatch(value)) { errors[field] = "Use plain text, without control characters."; return null; }
        var text = value.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (text.Length > max) { errors[field] = $"This can be at most {max} characters; it has {text.Length}."; return null; }
        return text;
    }

    /// <summary>The rationale or explanation a revision will hold: the new text, the old text when null was sent, or null when it was cleared.</summary>
    public static string? Resolve(string? sent, string? current) => sent is null ? current : sent.Length == 0 ? null : sent;

    private static string? OneLine(IDictionary<string, string> errors, string field, string? value, int max, bool required, string requiredMessage)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : Regex.Replace(value.Trim(), "\\s+", " ");
        if (text is null) { if (required) errors[field] = requiredMessage; return null; }
        if (OneLineControl.IsMatch(value!.Replace(" ", "").Replace("\t", ""))) { errors[field] = "This must be a single line of text, without control characters."; return null; }
        if (text.Length > max) { errors[field] = $"This can be at most {max} characters; it has {text.Length}."; return null; }
        return text;
    }

    /// <summary>The phases a revision will lay out, or null when the caller sent none (keep the current ones). Numbers are the phases' identity, so they must be distinct.</summary>
    public static IReadOnlyList<LayoutPhase>? CleanPhases(IDictionary<string, string> errors, IReadOnlyList<PlanPhaseInput>? phases, DateOnly today)
    {
        if (phases is null) return null;
        if (phases.Count > TreatmentPlanRules.PhasesMax) { errors["phases"] = $"A plan can have at most {TreatmentPlanRules.PhasesMax} phases."; return null; }
        var clean = new List<LayoutPhase>();
        var seen = new HashSet<int>();
        for (var i = 0; i < phases.Count; i++)
        {
            var p = phases[i];
            var before = errors.Count;
            var key = $"phases[{i}].";
            if (p.Number is null or < 1 || p.Number > TreatmentPlanRules.PhasesMax) errors[key + "number"] = $"Give the phase a number from 1 to {TreatmentPlanRules.PhasesMax}.";
            else if (!seen.Add(p.Number.Value)) errors[key + "number"] = $"Phase {p.Number} is listed twice.";
            var name = OneLine(errors, key + "name", p.Name, TreatmentPlanRules.PhaseNameMax, true, "Name the phase.");
            var visit = OneLine(errors, key + "plannedVisit", p.PlannedVisit, TreatmentPlanRules.PlannedVisitMax, false, "");
            if (p.TargetDate is { } d && (d < new DateOnly(2000, 1, 1) || d > today.AddYears(10))) errors[key + "targetDate"] = "Choose a date between 2000 and ten years from now.";
            if (errors.Count == before) clean.Add(new LayoutPhase(p.Number!.Value, name!, visit, p.TargetDate));
        }
        return clean.OrderBy(p => p.Number).ToList();
    }

    /// <summary>
    /// The placement a revision will hold: the current placement of every procedure, with the requested moves applied. A move names a procedure of this plan that has not been withdrawn and either a phase
    /// the resulting layout has or none. Afterwards no procedure may be left in a phase the layout no longer has.
    /// </summary>
    public static IReadOnlyDictionary<Guid, int?>? CleanPlacement(IDictionary<string, string> errors, IReadOnlyList<PlanPlacementInput>? moves, IReadOnlyDictionary<Guid, int?> current,
        ISet<Guid> activeItems, IReadOnlyList<LayoutPhase> phases)
    {
        var result = new Dictionary<Guid, int?>(current);
        var moved = new HashSet<Guid>();
        for (var i = 0; moves is not null && i < moves.Count; i++)
        {
            var m = moves[i];
            var key = $"placements[{i}].";
            if (m.ItemId is null || !activeItems.Contains(m.ItemId.Value)) { errors[key + "itemId"] = "Choose a procedure of this plan that has not been withdrawn."; continue; }
            if (!moved.Add(m.ItemId.Value)) { errors[key + "itemId"] = "This procedure is placed twice."; continue; }
            if (m.PhaseNumber is { } n && phases.All(p => p.Number != n)) { errors[key + "phaseNumber"] = $"This plan has no phase {n}."; continue; }
            result[m.ItemId.Value] = m.PhaseNumber;
        }
        // a withdrawn procedure's place no longer matters; a procedure still in play must sit in a phase that exists
        foreach (var id in result.Keys.ToList())
        {
            if (result[id] is not { } n || phases.Any(p => p.Number == n)) continue;
            if (activeItems.Contains(id)) errors["phases"] = $"Phase {n} still holds a procedure. Move it to another phase, or to none, in the same revision.";
            else result[id] = null;
        }
        return errors.Count == 0 ? result : null;
    }
}
