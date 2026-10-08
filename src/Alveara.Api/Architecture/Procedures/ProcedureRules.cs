using System.Text.RegularExpressions;

namespace Alveara.Api.Architecture.Procedures;

/// <summary>A refusal the caller can show: a stable <see cref="Code"/>, a message safe to display and, where several fields need attention, a message per field. <see cref="Usage"/> is set when an
/// inactivation needs the person to confirm that the procedure is still in use.</summary>
public class ProcedureCatalogException(string code, string message, int statusCode, IReadOnlyDictionary<string, string>? fieldErrors = null, ProcedureUsageView? usage = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string> FieldErrors { get; } = fieldErrors ?? new Dictionary<string, string>();
    public ProcedureUsageView? Usage { get; } = usage;
}

/// <summary>What a caller supplies to create a procedure or to revise one. Everything is optional here so the rules can name every missing or wrong field in one answer.</summary>
public sealed record ProcedureInput(
    string? CodeSystem, string? Code, string? Description, string? Category, string? Scope, string? Dentition, decimal? Fee,
    string? SourceName, string? SourceVersion, DateOnly? EffectiveFrom, DateOnly? ValidThrough);

/// <summary>The fields after the rules have accepted them: trimmed, upper-cased where it matters, and with the defaults filled in.</summary>
public sealed record ProcedureFields(
    string CodeSystem, string Code, string Description, string Category, string Scope, string Dentition, decimal Fee,
    string? SourceName, string? SourceVersion, DateOnly EffectiveFrom, DateOnly? ValidThrough);

/// <summary>
/// ALV-N005: what the catalog accepts. The same rules are the only way in, so a wrong entry is refused with a message per field before anything is stored, and the database repeats the
/// important ones (code shape, fee range, allowed values) as constraints in case anything ever bypasses the service.
/// </summary>
public static class ProcedureRules
{
    public const int DescriptionMax = 200;
    public const int SourceNameMax = 80;
    public const int SourceVersionMax = 40;
    public const int ReasonMax = 500;
    public const int CodeMax = 20;
    public const decimal FeeMax = 1_000_000.00m;
    public static readonly DateOnly EarliestDate = new(2000, 1, 1);
    public const int FutureYearsMax = 3;

    private static readonly Regex LocalShape = new("^[A-Z0-9][A-Z0-9-]{1,19}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CdtShape = new("^D[0-9]{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ExternalShape = new("^[A-Z0-9][A-Z0-9.-]{0,19}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ControlCharacter = new("[\\x00-\\x1F\\x7F]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static ProcedureCatalogException Invalid(Dictionary<string, string> errors) => new("validation_failed", "Some fields need attention.", 400, errors);

    /// <summary>
    /// Checks every field and throws one <c>validation_failed</c> naming all that need attention. <paramref name="earliestEffective"/> is the first date a NEW version may start on: today, or the
    /// start of the newest existing version when that is later (a change never reaches back and rewrites what was already in effect); null for a brand-new procedure, which may start on any
    /// accepted date.
    /// </summary>
    public static ProcedureFields Validate(ProcedureInput input, DateOnly today, DateOnly? earliestEffective)
    {
        var errors = new Dictionary<string, string>();
        var system = Clean(input.CodeSystem);
        var code = Clean(input.Code)?.ToUpperInvariant();
        var description = Clean(input.Description);
        var category = Clean(input.Category);
        var scope = Clean(input.Scope);
        var dentition = Clean(input.Dentition) ?? ProcedureDentitions.Both;
        var sourceName = Clean(input.SourceName);
        var sourceVersion = Clean(input.SourceVersion);

        if (system is null || !ProcedureCodeSystems.All.Contains(system)) errors["codeSystem"] = $"Choose a code system: {string.Join(", ", ProcedureCodeSystems.All)}.";
        if (code is null) errors["code"] = "A procedure code is required.";
        else if (system is not null && ProcedureCodeSystems.All.Contains(system) && CodeProblem(system, code) is { } problem) errors["code"] = problem;

        if (description is null) errors["description"] = "A description is required.";
        else if (description.Length > DescriptionMax) errors["description"] = $"Keep the description to {DescriptionMax} characters or fewer.";
        else if (ControlCharacter.IsMatch(description)) errors["description"] = "The description cannot contain control characters.";

        if (category is null || !ProcedureCategories.All.Contains(category)) errors["category"] = $"Choose a category: {string.Join(", ", ProcedureCategories.All)}.";
        if (scope is null || !ProcedureScopes.All.Contains(scope)) errors["scope"] = $"Choose what the procedure is performed on: {string.Join(", ", ProcedureScopes.All)}.";
        if (!ProcedureDentitions.All.Contains(dentition)) errors["dentition"] = $"Choose Permanent, Primary or Both.";
        else if (scope is not null && ProcedureScopes.All.Contains(scope) && !ProcedureScopes.IsToothLevel(scope)) dentition = ProcedureDentitions.Both; // dentition only matters for tooth-level work

        if (input.Fee is null) errors["fee"] = "A fee is required (enter 0 for no charge).";
        else if (input.Fee < 0) errors["fee"] = "A fee cannot be negative.";
        else if (input.Fee > FeeMax) errors["fee"] = $"A fee cannot be more than {FeeMax:F2}.";
        else if (input.Fee != decimal.Round(input.Fee.Value, 2)) errors["fee"] = "A fee has at most two decimal places.";

        if (sourceName is { Length: > SourceNameMax }) errors["sourceName"] = $"Keep the source name to {SourceNameMax} characters or fewer.";
        if (sourceVersion is { Length: > SourceVersionMax }) errors["sourceVersion"] = $"Keep the source edition to {SourceVersionMax} characters or fewer.";
        if (system == ProcedureCodeSystems.External && sourceName is null) errors["sourceName"] = "Name the source of an external code set.";
        if (system == ProcedureCodeSystems.Local && (sourceName is not null || sourceVersion is not null)) errors["sourceName"] = "A local code has no external source; leave the source empty or choose another code system.";

        var effective = input.EffectiveFrom ?? (earliestEffective is { } e && e > today ? e : today);
        if (effective < EarliestDate) errors["effectiveFrom"] = $"The start date cannot be before {EarliestDate:yyyy-MM-dd}.";
        else if (effective > today.AddYears(FutureYearsMax)) errors["effectiveFrom"] = $"The start date cannot be more than {FutureYearsMax} years ahead.";
        else if (earliestEffective is { } earliest && effective < earliest) errors["effectiveFrom"] = $"A change takes effect on {earliest:yyyy-MM-dd} or later; it cannot reach back and change what was already in effect.";
        if (input.ValidThrough is { } through && through < effective) errors["validThrough"] = "The last valid date cannot be before the start date.";

        if (errors.Count > 0) throw Invalid(errors);
        return new ProcedureFields(system!, code!, description!, category!, scope!, dentition, decimal.Round(input.Fee!.Value, 2), sourceName, sourceVersion, effective, input.ValidThrough);
    }

    /// <summary>The problem with a code, or null when it is acceptable in its code system. A local code may never be CDT-shaped, so it cannot pass for one.</summary>
    public static string? CodeProblem(string system, string code) => system switch
    {
        ProcedureCodeSystems.Cdt => CdtShape.IsMatch(code) ? null : "A CDT code is the letter D and four digits (for example D1234).",
        ProcedureCodeSystems.External => ExternalShape.IsMatch(code) ? null : "Use 1 to 20 letters, digits, dots or hyphens, starting with a letter or digit.",
        _ => !LocalShape.IsMatch(code) ? "A local code is 2 to 20 letters, digits or hyphens, starting with a letter or digit."
           : CdtShape.IsMatch(code) ? "That looks like a CDT code. Choose the CDT code system for it, or give the local code a different shape (for example LOCAL-1234)." : null,
    };

    public static string RequireReason(string? reason, string message)
    {
        var r = Clean(reason);
        if (r is null) throw new ProcedureCatalogException("reason_required", message, 400, new Dictionary<string, string> { ["reason"] = message });
        if (r.Length > ReasonMax) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    public static string? OptionalReason(string? reason)
    {
        var r = Clean(reason);
        if (r is { Length: > ReasonMax }) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    /// <summary>Applies the caller's row version for an optimistic-concurrency check; a missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ProcedureCatalogException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ProcedureCatalogException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }

    /// <summary>The version of a procedure that applies on a date: the newest one that has started by then (a later correction on the same day wins).</summary>
    public static ProcedureVersion? InEffect(IEnumerable<ProcedureVersion> versions, DateOnly on) =>
        versions.Where(v => v.EffectiveFrom <= on).OrderByDescending(v => v.EffectiveFrom).ThenByDescending(v => v.VersionNumber).FirstOrDefault();

    /// <summary>Active, Inactive, Scheduled (no version has started yet) or Expired (the code's last valid date has passed) on a date.</summary>
    public static string StatusOn(bool isActive, ProcedureVersion? inEffect, DateOnly on) =>
        !isActive ? "Inactive" : inEffect is null ? "Scheduled" : inEffect.ValidThrough is { } through && through < on ? "Expired" : "Active";
}
