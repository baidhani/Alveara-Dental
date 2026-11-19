using System.Text;

namespace Alveara.Api.Architecture.Measurement;

/// <summary>
/// ALV-N025 (F-128): turns any code (a refusal code, a state name) into an accepted measurement category - <c>^[a-z][a-z0-9_]{0,39}$</c>, the rule of
/// <see cref="MeasurementEvent"/> - so a measurement is never dropped because of how a code is spelled. Upper case becomes lower case with an underscore before it
/// ("InChair" -> "in_chair"), any other character becomes an underscore, repeats collapse, a leading non-letter gets "c_", and the result is cut to 40 characters.
/// </summary>
public static class MeasurementCategory
{
    public static string Of(string? code)
    {
        var sb = new StringBuilder();
        foreach (var ch in code ?? string.Empty)
        {
            if (char.IsUpper(ch)) { if (sb.Length > 0 && sb[^1] != '_') sb.Append('_'); sb.Append(char.ToLowerInvariant(ch)); }
            else if (ch is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var text = sb.ToString().Trim('_');
        if (text.Length == 0) text = "unknown";
        if (text[0] is < 'a' or > 'z') text = "c_" + text;
        return text.Length > 40 ? text[..40].TrimEnd('_') : text;
    }
}
