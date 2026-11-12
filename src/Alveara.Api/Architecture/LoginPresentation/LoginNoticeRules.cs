using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.LoginPresentation;

/// <summary>The outcome of checking a footer notice: the normalized text (null = empty, meaning "no custom text") or the refusal code and message.</summary>
public sealed record NoticeCheck(string? Text, string? ErrorCode, string? ErrorMessage)
{
    public bool Ok => ErrorCode is null;
}

/// <summary>
/// ALV-N020-C01: the plain-text rules for the sign-in footer notice (custom text and the deployment default alike). Trimmed; line endings normalized to "\n";
/// 1-1,000 characters; at most 10 line breaks; no tabs or other control characters; no "&lt;" or "&gt;" and no URL forms ("://", "www.", "mailto:"), so a notice can
/// never look like markup or a link. The client always renders it as text (never as HTML) whatever it contains.
/// </summary>
public static partial class LoginNoticeRules
{
    public const int MaxLength = 1000;
    public const int MaxLineBreaks = 10;

    [GeneratedRegex(@"://|\bwww\.|\bmailto:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlForm();

    public static NoticeCheck Check(string? raw)
    {
        var text = (raw ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length == 0) return new(null, null, null);
        if (text.Length > MaxLength)
            return new(null, "notice_too_long", $"The notice can be at most {MaxLength:N0} characters.");
        if (text.Count(c => c == '\n') > MaxLineBreaks)
            return new(null, "notice_too_many_lines", $"The notice can have at most {MaxLineBreaks} line breaks.");
        if (text.Any(c => c != '\n' && char.IsControl(c)))
            return new(null, "notice_control_character", "The notice can contain only ordinary text and line breaks (no tabs or control characters).");
        if (text.Contains('<') || text.Contains('>'))
            return new(null, "notice_markup", "The notice is plain text: it cannot contain the characters < or >.");
        if (UrlForm().IsMatch(text))
            return new(null, "notice_link", "The notice is plain text: it cannot contain web or e-mail addresses.");
        return new(text, null, null);
    }
}
