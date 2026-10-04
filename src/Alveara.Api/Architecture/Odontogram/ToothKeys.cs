namespace Alveara.Api.Architecture.Odontogram;

/// <summary>
/// STORY-006: the one identity of a tooth in the database. A tooth is stored as its FDI / ISO 3950 two-digit string: the first digit is the quadrant (1 upper right, 2 upper left,
/// 3 lower left, 4 lower right for permanent teeth; 5 to 8 for the same quadrants of the primary dentition), the second is the position from the midline (1 to 8 permanent, 1 to 5
/// primary). That gives 52 keys. Universal (1-32, A-T) and Palmer are only ways of DISPLAYING a key (<see cref="ToothNumbering"/>); they are never stored, so changing how a practice
/// reads the chart can never change which tooth a finding belongs to.
///
/// Not covered by a key, and so recorded as limits rather than guessed at: supernumerary teeth, and findings about an area rather than one tooth (a quadrant, an arch, the whole
/// mouth). The key is a string so those can be added later without migrating existing findings.
/// </summary>
public static class ToothKeys
{
    public static readonly IReadOnlyList<string> Permanent = Build([1, 2, 3, 4], 8);
    public static readonly IReadOnlyList<string> Primary = Build([5, 6, 7, 8], 5);
    public static readonly IReadOnlyList<string> All = [.. Permanent, .. Primary];

    private static readonly HashSet<string> Valid = [.. All];

    private static List<string> Build(int[] quadrants, int perQuadrant)
    {
        var keys = new List<string>();
        foreach (var q in quadrants)
            for (var p = 1; p <= perQuadrant; p++) keys.Add($"{q}{p}");
        return keys;
    }

    /// <summary>True only for one of the 52 keys, exactly as stored (case and spacing do not matter to digits, so no trimming is done: " 11" is not a key).</summary>
    public static bool IsValid(string? key) => key is not null && Valid.Contains(key);

    public static bool IsPrimary(string key) => key[0] >= '5';

    /// <summary>Quadrant 1 to 4 (permanent) or 5 to 8 (primary), then the position from the midline.</summary>
    public static (int Quadrant, int Position) Split(string key) => (key[0] - '0', key[1] - '0');

    /// <summary>
    /// Incisors and canines are "anterior": their biting edge is the Incisal surface and their outer surface is the Facial one. Premolars and molars are "posterior": Occlusal and
    /// Buccal. A primary tooth is anterior at positions 1 to 3 (incisors, canine) and posterior at 4 and 5 (molars); a permanent one at positions 1 to 3 and 4 to 8.
    /// </summary>
    public static bool IsAnterior(string key) => Split(key).Position <= 3;
}
