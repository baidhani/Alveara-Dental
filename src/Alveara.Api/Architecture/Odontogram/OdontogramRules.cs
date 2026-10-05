namespace Alveara.Api.Architecture.Odontogram;

/// <summary>Validation and lifecycle rules for tooth findings; one place so the service, the API and the tests agree. Every refusal names each field.</summary>
public static class OdontogramRules
{
    public const int ReasonMax = 500;

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OdontogramException Invalid(Dictionary<string, string> errors) => new("validation_failed", "Some fields need attention.", 400, errors);

    public sealed record FindingFields(string ToothKey, string? Surface, string Condition, string State, ConditionType Type);

    /// <summary>
    /// Validates a new finding against the condition the catalogue resolved for its code (null when the code is unknown). A tooth that is not one of the 52 keys is refused rather than
    /// guessed at, a surface must exist on that kind of tooth, a surface condition needs a surface and a whole-tooth condition must not carry one, and a condition must apply to the tooth's
    /// dentition (an implant is not recorded on a primary tooth). A surface letter is accepted in either case. Every refusal names its field.
    /// </summary>
    public static FindingFields ValidateFinding(string? toothKey, string? surface, string? condition, string? state, ConditionType? type)
    {
        var errors = new Dictionary<string, string>();
        var tooth = toothKey?.Trim();
        var c = Clean(condition);
        var s = Clean(state);
        var surf = Clean(surface)?.ToUpperInvariant();

        var toothOk = ToothKeys.IsValid(tooth);
        if (!toothOk) errors["toothKey"] = "Choose one of the teeth on the chart.";
        if (c is null) errors["condition"] = "Choose the condition.";
        else if (type is null) errors["condition"] = "That condition does not exist. Choose one from the list.";
        if (s is null || !FindingStates.All.Contains(s)) errors["state"] = "Choose " + string.Join(", ", FindingStates.All) + ".";

        if (type is not null)
        {
            if (toothOk && !ConditionDentitions.Allows(type.AppliesTo, ToothKeys.IsPrimary(tooth!)))
                errors["condition"] = $"{type.Label} cannot be recorded on {(ToothKeys.IsPrimary(tooth!) ? "primary" : "permanent")} teeth.";
            if (type.Scope == ConditionScopes.Surface)
            {
                if (surf is null) errors["surface"] = "Choose the surface this applies to.";
                else if (toothOk && !ToothSurfaces.IsValid(tooth!, surf)) errors["surface"] = "That surface does not exist on this tooth.";
                else if (!toothOk && !ToothSurfaces.Any.Contains(surf)) errors["surface"] = "That is not a tooth surface.";
            }
            else if (surf is not null) errors["surface"] = $"{type.Label} applies to the whole tooth, so no surface can be chosen.";
        }
        if (errors.Count > 0) throw Invalid(errors);
        return new FindingFields(tooth!, surf, type!.Code, s!, type);   // the catalogue's own spelling of the code, never what was typed
    }

    /// <summary>Forward only: Diagnosed to Planned, Planned to Completed, or Diagnosed straight to Completed (work done the same day). Existing and Completed are final; to correct them, withdraw and record again.</summary>
    public static bool CanMove(string from, string to) =>
        (from, to) is (FindingStates.Diagnosed, FindingStates.Planned) or (FindingStates.Diagnosed, FindingStates.Completed) or (FindingStates.Planned, FindingStates.Completed);

    public static string ValidateTargetState(string? state)
    {
        var s = Clean(state);
        if (s is null || !FindingStates.All.Contains(s)) throw Invalid(new Dictionary<string, string> { ["state"] = "Choose " + string.Join(", ", FindingStates.All) + "." });
        return s;
    }

    public static string RequireReason(string? reason, string message)
    {
        var r = Clean(reason);
        if (r is null) throw new OdontogramException("reason_required", message, 400, new Dictionary<string, string> { ["reason"] = message });
        if (r.Length > ReasonMax) throw Invalid(new Dictionary<string, string> { ["reason"] = $"Keep the reason to {ReasonMax} characters or fewer." });
        return r;
    }

    /// <summary>Applies the caller's row version for an optimistic-concurrency check; a missing or malformed one is a 400 before anything changes.</summary>
    public static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new OdontogramException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            return Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new OdontogramException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }
}
