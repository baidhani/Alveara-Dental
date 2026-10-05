using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// What is recorded about a whole tooth rather than one site (ALV-012-C01): <paramref name="Mobility"/> (Miller grade 0 to 3) and <paramref name="Furcation"/> (grade 0 to 3, multi-rooted teeth only);
/// null means not assessed. <paramref name="Excluded"/> marks a tooth as not charted in this chart (missing, or not examined): it is skipped by the entry sweep and carries no readings.
/// </summary>
public sealed record PerioToothInput(string ToothKey, int? Mobility, int? Furcation, bool Excluded);

/// <summary>A whole chart as it is judged: its site readings and its per-tooth records.</summary>
public sealed record PerioChartInput(IReadOnlyList<PerioReadingInput>? Readings, IReadOnlyList<PerioToothInput>? Teeth);

/// <summary>
/// ALV-012-C01: the rules for a complete chart, on top of <see cref="PerioRules"/> (which keeps judging the site readings exactly as STORY-012 did, so every earlier chart and test is unchanged). Pure, no
/// database. A chart is judged as a whole: every problem is reported, and one problem means nothing is accepted.
/// <list type="bullet">
/// <item>Mobility and furcation are whole grades 0 to 3; a furcation grade belongs only on a multi-rooted tooth (<see cref="PerioSiteModel.IsMultiRooted"/>).</item>
/// <item>A tooth appears once among the tooth records, and must be a permanent tooth.</item>
/// <item>An EXCLUDED tooth carries no readings, mobility or furcation (they would contradict the exclusion).</item>
/// <item>A tooth the odontogram records as absent cannot be charted unless it is excluded: <c>tooth_absent</c>, naming the way out. The caller supplies that set; this class does not read the database.</item>
/// </list>
/// </summary>
public static class PerioChartValidator
{
    public const int MinGrade = 0;
    public const int MaxGrade = 3;

    /// <param name="requireReadings">True for a chart to be saved (it must hold at least one reading); false for a PART of a chart being entered, where nothing yet is not a mistake.</param>
    public static IReadOnlyList<PerioProblem> Validate(PerioChartInput? chart, IReadOnlySet<string>? absentTeeth = null, bool requireReadings = true)
    {
        var problems = new List<PerioProblem>(requireReadings || chart?.Readings is { Count: > 0 } ? PerioRules.Validate(chart?.Readings) : []);
        var teeth = chart?.Teeth ?? [];
        var excluded = new HashSet<string>();
        var seen = new HashSet<string>();

        foreach (var t in teeth)
        {
            if (t is null) { problems.Add(new(null, null, "teeth", "invalid", "A tooth record is missing.")); continue; }
            if (!ToothKeys.IsValid(t.ToothKey)) { problems.Add(new(t.ToothKey, null, "toothKey", "unknown_tooth", $"\"{t.ToothKey}\" is not a tooth. Use the two-digit FDI number, such as 16 or 47.")); continue; }
            if (ToothKeys.IsPrimary(t.ToothKey)) { problems.Add(new(t.ToothKey, null, "toothKey", "primary_tooth", $"Tooth {t.ToothKey} is a primary tooth. Periodontal charting covers permanent teeth only.")); continue; }
            if (!seen.Add(t.ToothKey)) problems.Add(new(t.ToothKey, null, "toothKey", "duplicate_tooth", $"Tooth {t.ToothKey} has more than one tooth record; keep one."));
            if (t.Excluded) excluded.Add(t.ToothKey);

            CheckGrade(problems, t.ToothKey, "mobility", t.Mobility, "Mobility");
            CheckGrade(problems, t.ToothKey, "furcation", t.Furcation, "Furcation");
            if (t.Furcation is not null && !PerioSiteModel.IsMultiRooted(t.ToothKey))
                problems.Add(new(t.ToothKey, null, "furcation", "furcation_not_applicable", $"Tooth {t.ToothKey} has a single root, so there is no furcation to grade. Leave it empty."));
            if (t.Excluded && (t.Mobility is not null || t.Furcation is not null))
                problems.Add(new(t.ToothKey, null, "excluded", "excluded_tooth", $"Tooth {t.ToothKey} is marked as not charted, so it cannot also have a mobility or furcation grade."));
        }

        foreach (var r in chart?.Readings ?? [])
        {
            if (r is null || r.ToothKey is null) continue;
            if (excluded.Contains(r.ToothKey))
                problems.Add(new(r.ToothKey, r.Site, "excluded", "excluded_tooth", $"Tooth {r.ToothKey} is marked as not charted, so site {r.Site} cannot have a reading. Remove the reading or the exclusion."));
            else if (absentTeeth?.Contains(r.ToothKey) == true)
                problems.Add(new(r.ToothKey, r.Site, "toothKey", "tooth_absent",
                    $"Tooth {r.ToothKey} is recorded as missing in the odontogram, so it cannot be charted. Mark it as not charted, or correct the odontogram first."));
        }
        return problems;
    }

    private static void CheckGrade(List<PerioProblem> problems, string tooth, string field, int? value, string label)
    {
        if (value is < MinGrade or > MaxGrade)
            problems.Add(new(tooth, null, field, "out_of_range", $"{label} at tooth {tooth} is {value}; it must be a whole grade from {MinGrade} to {MaxGrade}."));
    }
}
