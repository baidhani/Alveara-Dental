namespace Alveara.Api.Architecture.Safety;

/// <summary>Limits and validation for alerts and clearances; one place so the services, the API and the tests agree. Every refusal is a 400 naming each field.</summary>
public static class SafetyRules
{
    public const int TitleMax = 200;
    public const int DetailMax = 1000;
    public const int SourceMax = 300;
    public const int ReasonMax = 500;
    public const int NameMax = 200;
    public const int DocumentMax = 300;

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static SafetyException Invalid(Dictionary<string, string> errors) => new("validation_failed", "Some fields need attention.", 400, errors);

    public sealed record AlertFields(string Category, string Title, string? Detail, string Severity, string Source);

    /// <summary>Validates an alert's fields. The source is required: an alert that does not say where it came from is refused (<c>source_required</c>), never accepted and guessed at later.</summary>
    public static AlertFields ValidateAlert(string? category, string? title, string? detail, string? severity, string? source, bool categoryRequired)
    {
        var errors = new Dictionary<string, string>();
        var c = Clean(category);
        var t = Clean(title);
        var d = Clean(detail);
        var sv = Clean(severity);
        var src = Clean(source);
        if (categoryRequired && (c is null || !SafetyCategories.AlertCategories.Contains(c))) errors["category"] = "Choose " + string.Join(", ", SafetyCategories.AlertCategories) + ".";
        if (t is null) errors["title"] = "A title is required.";
        else if (t.Length > TitleMax) errors["title"] = $"Keep the title to {TitleMax} characters or fewer.";
        if (d is { Length: > DetailMax }) errors["detail"] = $"Keep the detail to {DetailMax} characters or fewer.";
        if (sv is null || !SafetySeverities.All.Contains(sv)) errors["severity"] = "Choose Critical, High, Moderate or Low.";
        if (src is null) errors["sourceNote"] = "A source is required.";
        else if (src.Length > SourceMax) errors["sourceNote"] = $"Keep the source to {SourceMax} characters or fewer.";
        if (src is null && errors.Count == 1)
            throw new SafetyException("source_required", "Say where this information came from (for example 'reported by the patient at intake').", 400, errors);
        if (errors.Count > 0) throw Invalid(errors);
        return new AlertFields(c ?? "", t!, d, sv!, src!);
    }

    /// <summary>A reason that must be given (resolving or reopening an alert, resolving or cancelling a clearance).</summary>
    public static string RequireReason(string? reason, string message)
    {
        var r = Clean(reason);
        if (r is null) throw new SafetyException("reason_required", message, 400, new Dictionary<string, string> { ["reason"] = message });
        if (r.Length > ReasonMax) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    public static string? OptionalReason(string? reason)
    {
        var r = Clean(reason);
        if (r is { Length: > ReasonMax }) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    public sealed record ClearanceFields(string Kind, string Reason, string? RequestedFrom);

    public static ClearanceFields ValidateClearance(string? kind, string? reason, string? requestedFrom)
    {
        var errors = new Dictionary<string, string>();
        var k = Clean(kind);
        var r = Clean(reason);
        var from = Clean(requestedFrom);
        if (k is null || !SafetyCategories.ClearanceKinds.Contains(k)) errors["kind"] = "Choose Medical or Dental.";
        if (r is null) errors["reason"] = "Say why the clearance is needed.";
        else if (r.Length > ReasonMax) errors["reason"] = $"Keep the reason to {ReasonMax} characters or fewer.";
        if (from is { Length: > NameMax }) errors["requestedFrom"] = $"Keep this to {NameMax} characters or fewer.";
        if (errors.Count > 0) throw Invalid(errors);
        return new ClearanceFields(k!, r!, from);
    }

    public static string? OptionalDocument(string? reference)
    {
        var r = Clean(reference);
        if (r is { Length: > DocumentMax }) throw Invalid(new Dictionary<string, string> { ["documentReference"] = $"Keep the reference to {DocumentMax} characters or fewer." });
        return r;
    }

    public static string RequireDocument(string? reference) =>
        OptionalDocument(reference) ?? throw Invalid(new Dictionary<string, string> { ["documentReference"] = "Say which document this is." });

    public static string? OptionalNote(string? note)
    {
        var n = Clean(note);
        if (n is { Length: > ReasonMax }) throw Invalid(new Dictionary<string, string> { ["note"] = $"Keep the note to {ReasonMax} characters or fewer." });
        return n;
    }

    /// <summary>Applies the caller's row version for an optimistic-concurrency check; a missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new SafetyException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new SafetyException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }
}
