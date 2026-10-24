using System.Security.Cryptography;
using System.Text;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>
/// STORY-009: the rules for what a person may enter, applied before anything is stored (the database refuses the same things by itself). Text is trimmed; a title is one short line of plain text; a note
/// may span lines and tabs but holds no other control character; a due date lies within a sane window; a recall interval is a whole number of days.
/// </summary>
public static class FollowUpValidation
{
    public static string Title(string? title)
    {
        var t = title?.Trim() ?? "";
        if (t.Length == 0 || t.Length > FollowUpRules.TitleMaxLength || t.Any(char.IsControl))
            throw new FollowUpException("title_invalid", $"Write a short title (up to {FollowUpRules.TitleMaxLength} characters) on one line.", 400);
        return t;
    }

    /// <summary>An absent or blank note is no note.</summary>
    public static string? Note(string? note)
    {
        var n = note?.Trim();
        if (string.IsNullOrEmpty(n)) return null;
        if (n.Length > FollowUpRules.NoteMaxLength || n.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new FollowUpException("note_invalid", $"The note can be up to {FollowUpRules.NoteMaxLength} characters of plain text.", 400);
        return n;
    }

    /// <summary>The reason for cancelling: required.</summary>
    public static string CancelNote(string? note) =>
        Note(note) ?? throw new FollowUpException("note_required", "Write why the follow-up is being cancelled.", 400);

    public static DateTimeOffset Due(DateTimeOffset? due, DateTimeOffset now)
    {
        if (due is not { } d) throw new FollowUpException("due_required", "Choose when the follow-up is due.", 400);
        if (d < now.AddDays(-FollowUpRules.MaxPastDays) || d > now.AddDays(FollowUpRules.MaxFutureDays))
            throw new FollowUpException("due_out_of_range", "The due date is too far in the past or in the future. Check the date.", 400);
        return d.ToUniversalTime();
    }

    public static int? Interval(int? days)
    {
        if (days is null) return null;
        if (days < 1 || days > FollowUpRules.MaxRecallIntervalDays)
            throw new FollowUpException("interval_invalid", $"A recall interval is 1 to {FollowUpRules.MaxRecallIntervalDays} days, or none.", 400);
        return days;
    }

    public static string Key(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 64 ? throw new FollowUpException("idempotency_key_required", "Send an Idempotency-Key header of up to 64 characters.", 400) : key;

    /// <summary>The row version the person saw. A missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] Version(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new FollowUpException("row_version_required", "Send the version of the follow-up you were shown so a change by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new FollowUpException("row_version_required", "The follow-up version is not valid. Reload and try again.", 400); }
    }

    /// <summary>A task id made from what identifies the request (who, which key; or which task a recall follows), so a repeat of the request meets the same primary key and cannot create a second row.</summary>
    public static Guid DeterministicId(string purpose, params object[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"alveara.followup.{purpose}:" + string.Join(":", parts.Select(p => p is Guid g ? g.ToString("N") : p.ToString()))));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
