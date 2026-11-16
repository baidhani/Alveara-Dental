using Alveara.Api.Architecture.Time;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Completion;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Controllers;

/// <summary>A selected set of one plan's procedures to complete: the caller's key, the plan's version as last read, the encounter, the provider, when it was performed and which procedures.</summary>
/// <summary>ALV-N021 A3: <c>PerformedAtLocal</c> is the practice-local time the screen sends; <c>PerformedAtUtc</c> is the legacy form, still accepted alone (D-2).</summary>
public record CompleteProceduresBody(string? IdempotencyKey, string? RowVersion, Guid? EncounterId, Guid? ProviderId, DateTimeOffset? PerformedAtUtc, List<Guid>? ItemIds, string? PerformedAtLocal = null);

/// <summary>ALV-008-C02: adopt ONE procedure completed the old way: the caller's key, the plan's version as last read, who performed it, where, when and why it is being recorded now. The procedure is named by the route.</summary>
/// <summary>ALV-N021 A3: as <see cref="CompleteProceduresBody"/>, <c>PerformedAtLocal</c> or the legacy <c>PerformedAtUtc</c>, never both.</summary>
public record AdoptCompletionBody(string? IdempotencyKey, string? RowVersion, Guid? EncounterId, Guid? ProviderId, DateTimeOffset? PerformedAtUtc, string? Reason, string? PerformedAtLocal = null);

/// <summary>A correction to a completed procedure: the amendment last seen (0 if none), the caller's key, what to correct and why.</summary>
public record AmendProcedureBody(string? IdempotencyKey, int? ExpectedAmendmentNumber, Guid? ProviderId, DateTimeOffset? PerformedAtUtc, Guid? EncounterId, string? Reason);

/// <summary>
/// ALV-008-C01: completing a selected set of procedures as one operation, the readiness the review screen reads first, and the completed-procedure records with their corrections. Completing, correcting and the
/// readiness need <see cref="Permission.ManageTreatmentPlans"/> (dentists and administrators; the writes also need a CSRF token); reading completed procedures needs
/// <see cref="Permission.ViewClinicalDocumentation"/>. A refusal returns a stable <c>error</c> code, a message safe to show and <c>fieldErrors</c>; a refusal about particular procedures
/// (<c>items_not_completable</c>, <c>ambiguous_chart_finding</c>) also lists them in <c>items</c>. A stale plan or a stale correction is the shared 409 concurrency conflict. The charge amount is never in the request.
/// </summary>
[ApiController]
[Authorize]
public class CompletionController(CompletedProcedureService completion, IPracticeClock clock) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    /// <summary>ALV-N021 A3 (BR-2): the performed time a completion means (seconds allowed: "now" must never be earlier than something just recorded).</summary>
    private DateTimeOffset? Performed(string? local, DateTimeOffset? legacyUtc)
    {
        var performed = PracticeLocalInput.Resolve(local, legacyUtc, "performedAtLocal", "performedAtUtc", allowSeconds: true, clock);
        if (local is not null) PracticeLocalInput.MarkLocalUsed(HttpContext, "performedAtLocal", "performedAtUtc");
        return performed;
    }

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (LocalTimeInputException ex)   // ALV-N021 A3 (BR-2): a refused practice-local time, with the field named
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors });
        }
        catch (ItemsNotCompletableException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = PracticeLocalInput.WithLocalAlias(ex.FieldErrors, HttpContext), items = ex.Items });
        }
        catch (AmbiguousChartFindingException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = PracticeLocalInput.WithLocalAlias(ex.FieldErrors, HttpContext), items = ex.Items });
        }
        catch (TreatmentPlanException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = PracticeLocalInput.WithLocalAlias(ex.FieldErrors, HttpContext) });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    [HttpPost("api/treatment-plans/{planId:guid}/complete-procedures")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Complete(Guid planId, [FromBody] CompleteProceduresBody body, CancellationToken ct) =>
        Run(async () => Ok(await completion.CompleteAsync(planId, new CompleteProceduresRequest(body.IdempotencyKey, body.RowVersion, body.EncounterId, body.ProviderId, Performed(body.PerformedAtLocal, body.PerformedAtUtc), body.ItemIds), Actor, ct)));

    /// <summary>
    /// ALV-008-C02: adopts one procedure that was completed the old, state-only way: records its provider, encounter, performed time and reason, the completed-procedure record, the chart effect and its one charge in
    /// one transaction, without changing the old completion's history. <c>not_a_legacy_completion</c> when the procedure is not Completed or already has a record; the same refusals and conflicts as completing.
    /// </summary>
    [HttpPost("api/treatment-plans/{planId:guid}/items/{itemId:guid}/adopt-completion")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Adopt(Guid planId, Guid itemId, [FromBody] AdoptCompletionBody body, CancellationToken ct) =>
        Run(async () => Ok(await completion.AdoptAsync(planId, itemId, new AdoptCompletionRequest(body.IdempotencyKey, body.RowVersion, body.EncounterId, body.ProviderId, Performed(body.PerformedAtLocal, body.PerformedAtUtc), body.Reason), Actor, ct)));

    /// <summary>What completing the given procedures (or every open one) would do: which can be completed, the chart effect and the charge effect of each. Reads only.</summary>
    [HttpGet("api/treatment-plans/{planId:guid}/completion-readiness")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    public Task<IActionResult> Readiness(Guid planId, [FromQuery] Guid[]? itemIds, CancellationToken ct) =>
        Run(async () => Ok(await completion.ReadinessAsync(planId, itemIds, ct)));

    /// <summary>ALV-008-C02 Gate D: counts only (no patient, plan or amount) of every disagreement between completion records, the ledger and the chart, and of procedures still waiting for adoption. Reads only.</summary>
    [HttpGet("api/completion/reconciliation")]
    [RequirePermission(Permission.ViewAuditLog)]
    public Task<IActionResult> Reconciliation(CancellationToken ct) => Run(async () => Ok(await completion.ReconcileAsync(ct)));

    [HttpGet("api/patients/{patientId:guid}/completed-procedures")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> List(Guid patientId, CancellationToken ct) =>
        Run(async () => Ok(new { patientId, procedures = await completion.ListAsync(patientId, ct) }));

    [HttpGet("api/completed-procedures/{id:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) => Run(async () => Ok(await completion.DetailAsync(id, ct)));

    [HttpPost("api/completed-procedures/{id:guid}/amend")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Amend(Guid id, [FromBody] AmendProcedureBody body, CancellationToken ct) =>
        Run(async () => Ok(await completion.AmendAsync(id, new AmendCompletedProcedureRequest(body.IdempotencyKey, body.ExpectedAmendmentNumber, body.ProviderId, body.PerformedAtUtc, body.EncounterId, body.Reason), Actor, ct)));
}
