namespace Alveara.Api.Architecture.Odontogram;

/// <summary>Validation and lifecycle rules for tooth findings; one place so the service, the API and the tests agree. Every refusal names each field.</summary>
public static class OdontogramRules
{
    public const int ReasonMax = 500;

    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OdontogramException Invalid(Dictionary<string, string> errors) => new("validation_failed", "Some fields need attention.", 400, errors);

    public sealed record FindingFields(string ToothKey, string? Surface, string Condition, string State);

    /// <summary>
    /// Validates a new finding. A tooth that is not one of the 52 keys is refused (<c>tooth_invalid</c>) rather than guessed at, a surface must exist on that kind of tooth, a surface
    /// condition needs a surface and a whole-tooth condition must not carry one. A surface letter is accepted in either case.
    /// </summary>
    public static FindingFields ValidateFinding(string? toothKey, string? surface, string? condition, string? state)
    {
        var errors = new Dictionary<string, string>();
        var tooth = toothKey?.Trim();
        var c = Clean(condition);
        var s = Clean(state);
        var surf = Clean(surface)?.ToUpperInvariant();

        var toothOk = ToothKeys.IsValid(tooth);
        if (!toothOk) errors["toothKey"] = "Choose one of the teeth on the chart.";
        var conditionOk = c is not null && FindingConditions.All.Contains(c);
        if (!conditionOk) errors["condition"] = "Choose " + string.Join(", ", FindingConditions.All) + ".";
        if (s is null || !FindingStates.All.Contains(s)) errors["state"] = "Choose " + string.Join(", ", FindingStates.All) + ".";

        if (conditionOk)
        {
            if (FindingConditions.NeedsSurface(c!))
            {
                if (surf is null) errors["surface"] = "Choose the surface this applies to.";
                else if (toothOk && !ToothSurfaces.IsValid(tooth!, surf)) errors["surface"] = "That surface does not exist on this tooth.";
                else if (!toothOk && !ToothSurfaces.Any.Contains(surf)) errors["surface"] = "That is not a tooth surface.";
            }
            else if (surf is not null) errors["surface"] = $"{c} applies to the whole tooth, so no surface can be chosen.";
        }
        if (errors.Count > 0) throw Invalid(errors);
        return new FindingFields(tooth!, surf, c!, s!);
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
