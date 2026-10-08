using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Treatment;

namespace Alveara.Api.Controllers;

/// <summary>One proposed procedure as the client sends it: the diagnosis it is for, the catalog procedure and, where the procedure needs it, the tooth and surface.</summary>
public record PlanItemRequest(Guid? DiagnosisId, Guid? ProcedureId, string? ToothKey, string? Surface);

/// <summary>A new plan: a title and at least one proposed procedure. <c>idempotencyKey</c> makes a retried save return the plan the first save made (the items' keys are derived from it).</summary>
public record CreatePlanRequest(string? IdempotencyKey, string? Title, List<PlanItemRequest>? Items);

/// <summary>One more procedure for an existing plan, with the plan's row version as the caller last read it.</summary>
public record AddPlanItemRequest(string? IdempotencyKey, Guid? DiagnosisId, Guid? ProcedureId, string? ToothKey, string? Surface, string? RowVersion);

public record RenamePlanRequest(string? Title, string? RowVersion);

public record WithdrawPlanRequest(string? Reason, string? RowVersion);

/// <summary>
/// STORY-015: treatment plans. Reading needs <see cref="Permission.ViewClinicalDocumentation"/> (a plan carries diagnosis wording, so it is read like the rest of clinical documentation);
/// creating a plan, adding or withdrawing a procedure, renaming and withdrawing need <see cref="Permission.ManageTreatmentPlans"/> (dentists and administrators) plus a CSRF token. A refusal
/// returns a stable <c>error</c> code, a message safe to show and, for <c>validation_failed</c>, <c>fieldErrors</c>: a message for every field that needs correcting (an item's fields are named
/// <c>items[0].diagnosisId</c> and so on when a plan is created with several). A stale change is the shared 409 concurrency conflict; changing a withdrawn plan is a 409 <c>plan_withdrawn</c>. Fees are
/// US dollars and are the practice's catalog fees when each item was proposed: every plan carries <c>estimateLabel</c>, which says they are not an insurance estimate.
/// </summary>
[ApiController]
[Authorize]
public class TreatmentPlansController(TreatmentPlanService plans) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TreatmentPlanException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- reads ----------

    [HttpGet("api/patients/{patientId:guid}/treatment-plans")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> List(Guid patientId, [FromQuery] bool includeWithdrawn, CancellationToken ct) =>
        Run(async () => Ok(new { patientId, plans = await plans.ListAsync(patientId, includeWithdrawn, ct) }));

    [HttpGet("api/treatment-plans/{planId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid planId, CancellationToken ct) => Run(async () => Ok(await plans.GetAsync(planId, ct)));

    [HttpGet("api/treatment-plans/{planId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> History(Guid planId, CancellationToken ct) => Run(async () => Ok(await plans.HistoryAsync(planId, ct)));

    // ---------- changes ----------

    [HttpPost("api/patients/{patientId:guid}/treatment-plans")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Create(Guid patientId, [FromBody] CreatePlanRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.CreateAsync(patientId,
            new PlanInput(request.Title, request.IdempotencyKey, request.Items?.Select(i => new PlanItemInput(i.DiagnosisId, i.ProcedureId, i.ToothKey, i.Surface, null)).ToList()), Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/items")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> AddItem(Guid planId, [FromBody] AddPlanItemRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.AddItemAsync(planId, new PlanItemInput(request.DiagnosisId, request.ProcedureId, request.ToothKey, request.Surface, request.IdempotencyKey), request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/items/{itemId:guid}/withdraw")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> WithdrawItem(Guid planId, Guid itemId, [FromBody] WithdrawPlanRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.WithdrawItemAsync(planId, itemId, request.Reason, request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/rename")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Rename(Guid planId, [FromBody] RenamePlanRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.RenameAsync(planId, request.Title, request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/withdraw")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Withdraw(Guid planId, [FromBody] WithdrawPlanRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.WithdrawAsync(planId, request.Reason, request.RowVersion, Actor, ct)));
}
