using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.Periodontal;

/// <summary>
/// ALV-012-C01: the six-site model, spelled out once. A tooth has six probed sites: distal, middle and mesial on the cheek side (<c>DB</c>, <c>B</c>, <c>MB</c>) and the same three on the tongue side
/// (<c>DL</c>, <c>L</c>, <c>ML</c>). The codes are the stored identity and never change; what a site is CALLED depends on the tooth (a front tooth's cheek side is its facial side, an upper tooth's
/// tongue side is its palatal side), which is presentation only. <see cref="Sweep"/> is the one documented full-mouth entry order, shared by the server and the screen.
/// </summary>
public static class PerioSiteModel
{
    /// <summary>The side of the tooth a site is on: cheek side (buccal/facial) or tongue side (lingual/palatal).</summary>
    public static bool IsCheekSide(string site) => site is "DB" or "B" or "MB";

    /// <summary>Upper teeth are in quadrants 1 and 2; their tongue side is the palatal side.</summary>
    public static bool IsUpper(string toothKey) => toothKey[0] is '1' or '2';

    /// <summary>Which end of the tooth a site is at: "distal", "mid" or "mesial".</summary>
    public static string Position(string site) => site[0] switch { 'D' => "distal", 'M' => "mesial", _ => "mid" };

    /// <summary>How a site reads for this tooth, for example "mesial palatal" (upper tooth, tongue side) or "distal facial" (front tooth, cheek side). Presentation only; the code is the identity.</summary>
    public static string Describe(string toothKey, string site)
    {
        if (!ToothKeys.Permanent.Contains(toothKey) || !PerioRules.Sites.Contains(site)) throw new ArgumentException($"Not a site of a permanent tooth: {toothKey} {site}.");
        var side = IsCheekSide(site) ? (ToothKeys.IsAnterior(toothKey) ? "facial" : "buccal") : (IsUpper(toothKey) ? "palatal" : "lingual");
        var position = site is "B" or "L" ? "mid" : Position(site);
        return $"{position} {side}";
    }

    // Arch order as the odontogram draws it, patient's right to left: upper 18..11 then 21..28, lower 48..41 then 31..38.
    private static readonly string[] UpperArch = [.. ToothKeys.Permanent.Where(k => k[0] == '1').Reverse(), .. ToothKeys.Permanent.Where(k => k[0] == '2')];
    private static readonly string[] LowerArch = [.. ToothKeys.Permanent.Where(k => k[0] == '4').Reverse(), .. ToothKeys.Permanent.Where(k => k[0] == '3')];

    /// <summary>The teeth in arch order, upper arch first (the order of the sweep; the same as the chart's rows).</summary>
    public static IReadOnlyList<string> ArchOrder { get; } = [.. UpperArch, .. LowerArch];

    /// <summary>
    /// The full-mouth entry order: for each arch, a sweep along the cheek side and then back along the tongue side (a snake, so the hand never jumps), upper arch then lower. Within a tooth the
    /// sites run in the direction of travel: on the patient's right side of an arch the sweep going toward the middle visits distal, mid, mesial; on the left it visits mesial, mid, distal; the
    /// return pass mirrors it. Teeth in <paramref name="excluded"/> (missing, or not charted at this visit) are skipped without disturbing the order of the rest.
    /// </summary>
    public static IReadOnlyList<(string Tooth, string Site)> Sweep(IReadOnlySet<string>? excluded = null)
    {
        var order = new List<(string, string)>();
        foreach (var arch in new[] { UpperArch, LowerArch })
        {
            // cheek side, patient's right to left: right-hand teeth distal to mesial, left-hand teeth mesial to distal
            foreach (var tooth in arch.Where(t => excluded?.Contains(t) != true))
                foreach (var site in IsRight(tooth) ? new[] { "DB", "B", "MB" } : new[] { "MB", "B", "DB" }) order.Add((tooth, site));
            // tongue side, back from left to right
            foreach (var tooth in arch.Reverse().Where(t => excluded?.Contains(t) != true))
                foreach (var site in IsRight(tooth) ? new[] { "ML", "L", "DL" } : new[] { "DL", "L", "ML" }) order.Add((tooth, site));
        }
        return order;
    }

    /// <summary>Right-hand quadrants of the patient: 1 (upper right) and 4 (lower right).</summary>
    private static bool IsRight(string toothKey) => toothKey[0] is '1' or '4';

    /// <summary>
    /// Teeth with more than one root, where a furcation can be graded: all molars (positions 6 to 8) and the first upper premolars (14 and 24). The rest have nothing to grade, so a furcation grade on
    /// them is refused rather than stored as a meaningless zero.
    /// </summary>
    public static bool IsMultiRooted(string toothKey) => ToothKeys.IsValid(toothKey) && !ToothKeys.IsPrimary(toothKey) && (ToothKeys.Split(toothKey).Position >= 6 || toothKey is "14" or "24");
}
