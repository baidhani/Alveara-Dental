using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Periodontal;

namespace Alveara.Api.Controllers;

/// <summary>One probed site as the client sends it. Depth, recession and bleeding are required (a value left out is reported, never defaulted: a missing depth is not "0 mm" and a missing bleeding answer is not "no"); suppuration and plaque are optional, and leaving one out means it was not assessed.</summary>
public record PerioReadingRequest(string? ToothKey, string? Site, int? ProbingDepthMm, int? RecessionMm, bool? Bleeding, bool? Suppuration = null, bool? Plaque = null);

/// <summary>What is recorded about a whole tooth. Mobility and furcation are optional (left out means not assessed); <c>excluded</c> left out means false.</summary>
public record PerioToothRequest(string? ToothKey, int? Mobility, int? Furcation, bool? Excluded);

public record PerioSiteRefRequest(string? ToothKey, string? Site);

/// <summary>
/// Shared by the periodontal controllers: who is asking, how a refusal is returned (a stable <c>error</c> code, a message safe to show and, for <c>validation_failed</c>, <c>problems</c> naming every
/// entry to correct), and how a request body becomes the domain's inputs. A stale edit is the shared 409 concurrency conflict.
/// </summary>
public abstract class PerioControllerBase : ControllerBase
{
    protected Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    protected async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PerioException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    /// <summary>
    /// Turns the readings into domain inputs, reporting every missing value as a problem together with the rule problems of what was sent, so one refusal lists everything to fix. Returns null when
    /// <paramref name="requests"/> is null (the caller decides whether that is an error).
    /// </summary>
    protected static List<PerioReadingInput>? ToInputs(List<PerioReadingRequest?>? requests)
    {
        if (requests is null) return null;
        var missing = new List<PerioProblem>();
        var inputs = new List<PerioReadingInput>();
        foreach (var r in requests)
        {
            if (r is null) { missing.Add(new(null, null, "readings", "invalid", "A reading is missing.")); continue; }
            var before = missing.Count;
            if (r.ProbingDepthMm is null) missing.Add(new(r.ToothKey, r.Site, "probingDepthMm", "required", $"Enter the probing depth for tooth {r.ToothKey} site {r.Site}."));
            if (r.RecessionMm is null) missing.Add(new(r.ToothKey, r.Site, "recessionMm", "required", $"Enter the recession for tooth {r.ToothKey} site {r.Site}, or 0 if there is none."));
            if (r.Bleeding is null) missing.Add(new(r.ToothKey, r.Site, "bleeding", "required", $"Say whether tooth {r.ToothKey} site {r.Site} bled on probing (yes or no)."));
            if (missing.Count == before) inputs.Add(new PerioReadingInput(r.ToothKey!, r.Site!, r.ProbingDepthMm!.Value, r.RecessionMm!.Value, r.Bleeding!.Value, r.Suppuration, r.Plaque));
        }
        if (missing.Count == 0) return inputs;
        var all = missing.Concat(inputs.Count == 0 ? [] : PerioRules.Validate(inputs)).ToList();
        throw new PerioException("validation_failed", "The chart has entries that need correcting. Nothing was saved.", 400, all);
    }

    protected static List<PerioToothInput>? ToTeeth(List<PerioToothRequest?>? requests)
    {
        if (requests is null) return null;
        if (requests.Any(t => t is null)) throw new PerioException("validation_failed", "A tooth record is missing. Nothing was saved.", 400, [new(null, null, "teeth", "invalid", "A tooth record is missing.")]);
        return [.. requests.Select(t => new PerioToothInput(t!.ToothKey!, t.Mobility, t.Furcation, t.Excluded ?? false))];
    }

    protected static List<PerioSiteRef>? ToSiteRefs(List<PerioSiteRefRequest?>? requests)
    {
        if (requests is null) return null;
        if (requests.Any(c => c is null || c.ToothKey is null || c.Site is null))
            throw new PerioException("validation_failed", "An entry to clear is missing its tooth or site. Nothing was saved.", 400, [new(null, null, "clearSites", "invalid", "An entry to clear is missing its tooth or site.")]);
        return [.. requests.Select(c => new PerioSiteRef(c!.ToothKey!, c.Site!))];
    }
}
