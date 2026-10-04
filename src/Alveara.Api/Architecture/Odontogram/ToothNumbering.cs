namespace Alveara.Api.Architecture.Odontogram;

/// <summary>The three tooth-numbering systems in use. Only a way of showing a tooth; the stored identity is always the FDI key (<see cref="ToothKeys"/>).</summary>
public enum NumberingSystem
{
    /// <summary>US Universal: permanent 1-32 (upper right molar to lower right molar), primary A-T. The default.</summary>
    Universal = 0,
    /// <summary>FDI / ISO 3950: the key itself.</summary>
    Fdi = 1,
    /// <summary>Palmer notation in text form: quadrant (UR, UL, LL, LR) plus 1-8, or A-E for a primary tooth, for example "UR1" or "LLE".</summary>
    Palmer = 2,
}

/// <summary>
/// STORY-006: renders and reads a tooth key in any of the three numbering systems. Pure and total over the 52 keys: every system maps the keys one-to-one, and
/// <see cref="Parse"/>(<see cref="Display"/>(k)) is k. Which system a practice or a user sees is a presentation preference (not built in this story: the screen takes the system as
/// a parameter and defaults to Universal); nothing here touches the database.
/// </summary>
public static class ToothNumbering
{
    private static readonly string[] PalmerQuadrants = ["", "UR", "UL", "LL", "LR"];

    // Universal permanent: UR 18..11 are 1..8, UL 21..28 are 9..16, LL 38..31 are 17..24, LR 41..48 are 25..32.
    // Universal primary: UR 55..51 are A..E, UL 61..65 are F..J, LL 75..71 are K..O, LR 81..85 are P..T.
    private static readonly Dictionary<string, string> ToUniversal = BuildUniversal();
    private static readonly Dictionary<string, string> FromUniversal = ToUniversal.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    private static Dictionary<string, string> BuildUniversal()
    {
        var map = new Dictionary<string, string>();
        for (var p = 1; p <= 8; p++)
        {
            map[$"1{p}"] = (9 - p).ToString();           // 18 -> 1 ... 11 -> 8
            map[$"2{p}"] = (8 + p).ToString();           // 21 -> 9 ... 28 -> 16
            map[$"3{p}"] = (25 - p).ToString();          // 38 -> 17 ... 31 -> 24
            map[$"4{p}"] = (24 + p).ToString();          // 41 -> 25 ... 48 -> 32
        }
        for (var p = 1; p <= 5; p++)
        {
            map[$"5{p}"] = ((char)('A' + 5 - p)).ToString();   // 55 -> A ... 51 -> E
            map[$"6{p}"] = ((char)('F' + p - 1)).ToString();   // 61 -> F ... 65 -> J
            map[$"7{p}"] = ((char)('K' + 5 - p)).ToString();   // 75 -> K ... 71 -> O
            map[$"8{p}"] = ((char)('P' + p - 1)).ToString();   // 81 -> P ... 85 -> T
        }
        return map;
    }

    /// <summary>How the tooth reads in the chosen system. Throws for anything that is not one of the 52 keys: a wrong key is a defect to surface, never something to show as a blank.</summary>
    public static string Display(string key, NumberingSystem system)
    {
        if (!ToothKeys.IsValid(key)) throw new ArgumentException("Not a tooth key.", nameof(key));
        switch (system)
        {
            case NumberingSystem.Fdi: return key;
            case NumberingSystem.Universal: return ToUniversal[key];
            case NumberingSystem.Palmer:
                var (q, p) = ToothKeys.Split(key);
                var quadrant = PalmerQuadrants[(q - 1) % 4 + 1];
                return quadrant + (ToothKeys.IsPrimary(key) ? ((char)('A' + p - 1)).ToString() : p.ToString());
            default: throw new ArgumentOutOfRangeException(nameof(system));
        }
    }

    /// <summary>
    /// Reads what a person typed or a legacy system supplied and returns the stored key, or null when it is not exactly a tooth in that system (so a typo is refused rather than guessed
    /// at). Universal letters and Palmer quadrants are accepted in either case; surrounding spaces are ignored.
    /// </summary>
    public static string? Parse(string? text, NumberingSystem system)
    {
        var t = text?.Trim();
        if (string.IsNullOrEmpty(t)) return null;
        switch (system)
        {
            case NumberingSystem.Fdi: return ToothKeys.IsValid(t) ? t : null;
            case NumberingSystem.Universal: return FromUniversal.GetValueOrDefault(t.ToUpperInvariant());
            case NumberingSystem.Palmer:
                var upper = t.ToUpperInvariant();
                if (upper.Length != 3) return null;
                var q = Array.IndexOf(PalmerQuadrants, upper[..2]);
                if (q < 1) return null;
                var c = upper[2];
                if (c is >= '1' and <= '8') return Check($"{q}{c}");
                if (c is >= 'A' and <= 'E') return Check($"{q + 4}{c - 'A' + 1}");
                return null;
            default: throw new ArgumentOutOfRangeException(nameof(system));
        }

        static string? Check(string key) => ToothKeys.IsValid(key) ? key : null;
    }
}
