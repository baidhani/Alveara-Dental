using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>One probed site: a tooth (FDI key, as the odontogram stores it), one of its six sites, and what was measured there.</summary>
/// <summary>
/// <paramref name="Suppuration"/> and <paramref name="Plaque"/> (ALV-012-C01) are optional: null means "not assessed at this site", which is different from false ("assessed, none"). A chart saved
/// without them (every STORY-012 chart) is unchanged.
/// </summary>
public sealed record PerioReadingInput(string ToothKey, string Site, int ProbingDepthMm, int RecessionMm, bool Bleeding, bool? Suppuration = null, bool? Plaque = null);

/// <summary>One thing wrong with a submitted chart, specific enough for the screen to point at the entry: which tooth and site, which field, and what is allowed.</summary>
public sealed record PerioProblem(string? ToothKey, string? Site, string Field, string Code, string Message);

/// <summary>
/// STORY-012: what counts as valid periodontal charting. Pure, no database, so the service, the API, the screen's hints and the tests all agree. A chart is judged as a whole:
/// every problem is reported, and one problem means nothing is accepted (the caller never saves part of a chart).
/// Measures: probing depth and recession are whole millimetres, 0 to 15; bleeding on probing is yes or no. Clinical attachment loss is derived (probing depth + recession), never stored.
/// Limits chosen deliberately: permanent teeth only (primary teeth are not routinely probed), and no negative recession (gingival overgrowth is not modelled).
/// </summary>
public static class PerioRules
{
    public const int MinMm = 0;
    public const int MaxMm = 15;

    /// <summary>The six probed sites of a tooth: distal, middle and mesial on the cheek side (buccal/facial), and the same three on the tongue side (lingual/palatal).</summary>
    public static readonly IReadOnlyList<string> Sites = ["DB", "B", "MB", "DL", "L", "ML"];

    /// <summary>32 permanent teeth, six sites each: the most one chart can hold.</summary>
    public static readonly int MaxReadings = ToothKeys.Permanent.Count * Sites.Count;

    /// <summary>
    /// The one rule for clinical attachment loss (CAL): probing depth plus recession, in millimetres, at the same site. It is DERIVED, never stored, so it can never disagree with its parts. Because
    /// recession is never negative (gingival overgrowth is not modelled), CAL is never less than the probing depth.
    /// </summary>
    public static int AttachmentLossMm(PerioReadingInput r) => r.ProbingDepthMm + r.RecessionMm;

    public static IReadOnlyList<PerioProblem> Validate(IReadOnlyList<PerioReadingInput>? readings)
    {
        var problems = new List<PerioProblem>();
        if (readings is null || readings.Count == 0)
            return [new(null, null, "readings", "required", "Enter at least one site reading before saving.")];
        if (readings.Count > MaxReadings)
            problems.Add(new(null, null, "readings", "too_many", $"A chart holds at most {MaxReadings} readings (32 teeth with 6 sites each); this one has {readings.Count}."));

        var seen = new HashSet<(string, string)>();
        foreach (var r in readings)
        {
            if (r is null) { problems.Add(new(null, null, "readings", "invalid", "A reading is missing.")); continue; }
            var tooth = ToothKeys.IsValid(r.ToothKey);
            if (!tooth) problems.Add(new(r.ToothKey, r.Site, "toothKey", "unknown_tooth", $"\"{r.ToothKey}\" is not a tooth. Use the two-digit FDI number, such as 16 or 47."));
            else if (ToothKeys.IsPrimary(r.ToothKey)) problems.Add(new(r.ToothKey, r.Site, "toothKey", "primary_tooth", $"Tooth {r.ToothKey} is a primary tooth. Periodontal charting covers permanent teeth only."));

            var site = Sites.Contains(r.Site);
            if (!site) problems.Add(new(r.ToothKey, r.Site, "site", "unknown_site", $"\"{r.Site}\" is not a site. Use one of {string.Join(", ", Sites)}."));

            if (tooth && site && !seen.Add((r.ToothKey, r.Site)))
                problems.Add(new(r.ToothKey, r.Site, "site", "duplicate_site", $"Tooth {r.ToothKey} site {r.Site} is entered more than once; keep one reading."));

            CheckMm(problems, r, "probingDepthMm", r.ProbingDepthMm, "Probing depth");
            CheckMm(problems, r, "recessionMm", r.RecessionMm, "Recession");
        }
        return problems;
    }

    private static void CheckMm(List<PerioProblem> problems, PerioReadingInput r, string field, int value, string label)
    {
        if (value is < MinMm or > MaxMm)
            problems.Add(new(r.ToothKey, r.Site, field, "out_of_range", $"{label} at tooth {r.ToothKey} site {r.Site} is {value} mm; it must be a whole number from {MinMm} to {MaxMm} mm."));
    }
}
