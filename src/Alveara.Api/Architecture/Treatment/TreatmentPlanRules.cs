using System.Text.RegularExpressions;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Procedures;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>A refusal the caller can show: a stable <see cref="Code"/>, a message safe to display and, where several fields need attention, a message per field.</summary>
public class TreatmentPlanException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
}

/// <summary>One proposed procedure as the caller supplies it. Everything is optional here so the rules can name every missing or wrong field in one answer.</summary>
public sealed record PlanItemInput(Guid? DiagnosisId, Guid? ProcedureId, string? ToothKey, string? Surface, string? IdempotencyKey);

/// <summary>What a caller supplies to create a plan: a title and at least one proposed procedure.</summary>
public sealed record PlanInput(string? Title, string? IdempotencyKey, IReadOnlyList<PlanItemInput>? Items);

/// <summary>
/// STORY-015: what a treatment plan accepts. These helpers are the only way a field is judged, so a wrong entry is refused with a message per field before anything is stored; the database repeats
/// the important rules (diagnosis and procedure checks, fee copy, tooth and surface fit) as constraints and triggers in case anything ever bypasses the service.
/// </summary>
public static class TreatmentPlanRules
{
    public const int TitleMax = 120;
    public const int ReasonMax = 500;
    public const int KeyMax = 64;
    public const int ItemsMax = 50;

    private static readonly Regex ControlCharacter = new("[\\x00-\\x1F\\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static TreatmentPlanException Invalid(IReadOnlyDictionary<string, string> errors) => new("validation_failed", "Some fields need attention.", 400, errors);

    /// <summary>The trimmed title, or null with a message added under <paramref name="field"/> (blank, too long, or containing a control character).</summary>
    public static string? CleanTitle(IDictionary<string, string> errors, string field, string? title)
    {
        var t = Clean(title);
        if (t is null) errors[field] = "A title is required, so the plan can be recognised later.";
        else if (t.Length > TitleMax) errors[field] = $"Keep the title to {TitleMax} characters or fewer.";
        else if (ControlCharacter.IsMatch(t)) errors[field] = "The title cannot contain control characters or line breaks.";
        else return t;
        return null;
    }

    /// <summary>The trimmed idempotency key, or null with a message added. A key is what makes a retried or double-clicked save add nothing the second time.</summary>
    public static string? CleanKey(IDictionary<string, string> errors, string field, string? key)
    {
        var k = Clean(key);
        if (k is null) errors[field] = "A save key is required so a repeated save adds nothing twice. Reload and try again.";
        else if (k.Length > KeyMax || ControlCharacter.IsMatch(k)) errors[field] = $"The save key must be {KeyMax} characters or fewer with no control characters.";
        else return k;
        return null;
    }

    /// <summary>The tooth (upper-cased as the odontogram keeps it) and surface letter, or a message when either is not valid or a surface has no tooth.</summary>
    public static (string? Tooth, string? Surface, string? Problem) CleanSite(string? toothKey, string? surface)
    {
        var tooth = Clean(toothKey);
        var surf = Clean(surface)?.ToUpperInvariant();
        if (tooth is not null && !ToothKeys.IsValid(tooth)) return (null, null, "That is not a tooth.");
        if (surf is not null && tooth is null) return (null, null, "A surface needs the tooth it is on.");
        if (surf is not null && !ToothSurfaces.IsValid(tooth!, surf)) return (null, null, "That surface does not exist on that tooth.");
        return (tooth, surf, null);
    }

    /// <summary>
    /// Whether a tooth and surface fit what a catalog version applies to, or the message that says why not. A whole-mouth, arch or quadrant procedure takes no tooth; a tooth procedure needs a
    /// tooth and no surface; a surface procedure needs both; permanent-only and primary-only procedures need a tooth of that dentition. The database trigger on plan items repeats this.
    /// </summary>
    public static string? ApplicabilityProblem(string scope, string dentition, string? toothKey, string? surface)
    {
        if (!ProcedureScopes.IsToothLevel(scope))
            return toothKey is null && surface is null ? null : "This procedure applies to a larger area than one tooth, so leave the tooth and surface empty.";
        if (toothKey is null) return scope == ProcedureScopes.ToothSurface ? "This procedure is done on a tooth surface; choose the tooth and the surface." : "This procedure is done on one tooth; choose the tooth.";
        if (scope == ProcedureScopes.Tooth && surface is not null) return "This procedure is done on a whole tooth, so leave the surface empty.";
        if (scope == ProcedureScopes.ToothSurface && surface is null) return "This procedure is done on a tooth surface; choose the surface.";
        if (dentition == ProcedureDentitions.Permanent && ToothKeys.IsPrimary(toothKey)) return "This procedure is for permanent teeth, and that is a primary tooth.";
        if (dentition == ProcedureDentitions.Primary && !ToothKeys.IsPrimary(toothKey)) return "This procedure is for primary teeth, and that is a permanent tooth.";
        return null;
    }

    public static string RequireReason(string? reason, string message)
    {
        var r = Clean(reason);
        if (r is null) throw new TreatmentPlanException("reason_required", message, 400, new Dictionary<string, string> { ["reason"] = message });
        if (r.Length > ReasonMax) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    /// <summary>Applies the caller's row version for an optimistic-concurrency check; a missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new TreatmentPlanException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new TreatmentPlanException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }
}
