using System.Security.Cryptography;
using System.Text;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.ExternalCases;

/// <summary>ALV-009-C02: the input checks of the case and party services. Each refusal is a stable code with a message a person can act on, raised before anything is stored.</summary>
public static class ExternalCaseValidation
{
    public static string Required(string? value, int max, string code, string what)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0 || v.Length > max || v.Any(char.IsControl))
            throw new ExternalCaseException(code, $"Write {what} (up to {max} characters) on one line.", 400);
        return v;
    }

    /// <summary>An absent or blank value is no value; a present one is plain single-line text within the limit.</summary>
    public static string? Optional(string? value, int max, string code, string what)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        if (v.Length > max || v.Any(char.IsControl))
            throw new ExternalCaseException(code, $"The {what} can be up to {max} characters on one line.", 400);
        return v;
    }

    /// <summary>A note: plain text over several lines, bounded; blank is no note.</summary>
    public static string? Note(string? note, int max = ExternalCaseRules.NoteMaxLength)
    {
        var n = note?.Trim();
        if (string.IsNullOrEmpty(n)) return null;
        if (n.Length > max || n.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            throw new ExternalCaseException("note_invalid", $"The note can be up to {max} characters of plain text.", 400);
        return n;
    }

    public static string RequiredNote(string? note, string what) =>
        Note(note, ExternalCaseRules.ReasonMaxLength) ?? throw new ExternalCaseException("note_required", $"Write {what}.", 400);

    public static string Kind(string? kind, IReadOnlyList<string> allowed, string code, string what) =>
        kind is not null && allowed.Contains(kind) ? kind : throw new ExternalCaseException(code, $"Choose {what}.", 400);

    public static string? ToothKey(string? key)
    {
        var k = key?.Trim();
        if (string.IsNullOrEmpty(k)) return null;
        return ToothKeys.All.Contains(k) ? k : throw new ExternalCaseException("tooth_invalid", "Choose a tooth from the chart (its two-digit number).", 400);
    }

    public static DateOnly? Due(DateOnly? dueOn, DateOnly today)
    {
        if (dueOn is not { } d) return null;
        return ExternalCaseRules.DueDateInRange(d, today)
            ? d : throw new ExternalCaseException("due_out_of_range", "The due date is too far in the past or in the future. Check the date.", 400);
    }

    public static string Key(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 64 ? throw new ExternalCaseException("idempotency_key_required", "Send an Idempotency-Key header of up to 64 characters.", 400) : key;

    /// <summary>The row version the person saw. A missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] Version(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ExternalCaseException("row_version_required", "Send the version you were shown so a change by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new ExternalCaseException("row_version_required", "The version is not valid. Reload and try again.", 400); }
    }

    /// <summary>A case id made from who asked and with which key, so a repeat of the request meets the same primary key and cannot create a second row.</summary>
    public static Guid DeterministicId(string purpose, params object[] parts)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"alveara.externalcase.{purpose}:" + string.Join(":", parts.Select(p => p is Guid g ? g.ToString("N") : p.ToString()))));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
