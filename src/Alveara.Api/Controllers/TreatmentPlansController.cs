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
public record AddPlanItemRequest(string? IdempotencyKey, Guid? DiagnosisId, Guid? ProcedureId, string? ToothKey, string? Surface, string? RowVersion, int? PhaseNumber = null);

/// <summary>ALV-015-C01: a phase of the plan as laid out in a revision; <c>number</c> is its identity across revisions.</summary>
public record PlanPhaseRequest(int? Number, string? Name, string? PlannedVisit, DateOnly? TargetDate);

public record PlanPlacementRequest(Guid? ItemId, int? PhaseNumber);

/// <summary>ALV-015-C01: a patient's answer. <c>decision</c> is Accepted or Declined; a decline needs a reason; <c>decidedByName</c> is who gave the answer (the signed-in account is recorded as who entered it).</summary>
public record CompleteAcceptedRequest(string? RowVersion);
public record DecisionRequest(string? Decision, string? Reason, string? DecidedByName, string? RowVersion);

public record ScheduleRequest(bool Scheduled, DateOnly? PlannedDate);

/// <summary>ALV-015-C01: an explicit revision (reason required). Null parts are kept as they are; a blank rationale or explanation clears it.</summary>
public record ReviseRequest(string? Reason, string? ClinicalRationale, string? PatientExplanation, List<PlanPhaseRequest>? Phases, List<PlanPlacementRequest>? Placements, string? RowVersion);

public record RenamePlanRequest(string? Title, string? RowVersion);

/// <summary>ALV-015-C01: a plan's reference code, set once, which an old diagnosis's free-text reference can be matched to.</summary>
public record ReferenceCodeRequest(string? Code, string? RowVersion);

/// <summary>ALV-015-C01: <c>apply</c> false only previews which references would be resolved; true resolves them.</summary>
public record ReconcileRequest(bool Apply);

public record ResolveReferenceRequest(Guid? PlanId);

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
public class TreatmentPlansController(TreatmentPlanService plans, DiagnosisReconciliationService reconciliation) : ControllerBase
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

    /// <summary>STORY-008: the completed procedures of a patient, newest first, derived from the plans' completion records.</summary>
    [HttpGet("api/patients/{patientId:guid}/clinical-history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> ClinicalHistory(Guid patientId, CancellationToken ct) =>
        Run(async () => Ok(new { patientId, entries = await plans.ClinicalHistoryAsync(patientId, ct) }));

    [HttpGet("api/treatment-plans/{planId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid planId, CancellationToken ct) => Run(async () => Ok(await plans.GetAsync(planId, ct)));

    [HttpGet("api/treatment-plans/{planId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> History(Guid planId, CancellationToken ct) => Run(async () => Ok(await plans.HistoryAsync(planId, ct)));

    /// <summary>ALV-015-C01: every saved revision of a plan, oldest first, with what each produced and came to.</summary>
    [HttpGet("api/treatment-plans/{planId:guid}/revisions")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Revisions(Guid planId, CancellationToken ct) => Run(async () => Ok(await plans.RevisionsAsync(planId, ct)));

    [HttpGet("api/treatment-plans/{planId:guid}/revisions/{number:int}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Revision(Guid planId, int number, CancellationToken ct) => Run(async () => Ok(await plans.RevisionAsync(planId, number, ct)));

    /// <summary>What changed from revision <c>from</c> to revision <c>to</c>: title, words, phases, procedures added, withdrawn or moved, and the practice fee estimate of each.</summary>
    [HttpGet("api/treatment-plans/{planId:guid}/revisions/compare")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Compare(Guid planId, [FromQuery] int from, [FromQuery] int to, CancellationToken ct) => Run(async () => Ok(await plans.CompareAsync(planId, from, to, ct)));

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
        Run(async () => Ok(await plans.AddItemAsync(planId, new PlanItemInput(request.DiagnosisId, request.ProcedureId, request.ToothKey, request.Surface, request.IdempotencyKey, request.PhaseNumber), request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/decision")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> DecidePlan(Guid planId, [FromBody] DecisionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.DecidePlanAsync(planId, request.Decision, request.Reason, request.DecidedByName, request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/items/{itemId:guid}/decision")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> DecideItem(Guid planId, Guid itemId, [FromBody] DecisionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.DecideItemAsync(planId, itemId, request.Decision, request.Reason, request.DecidedByName, request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/items/{itemId:guid}/schedule")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> ScheduleItem(Guid planId, Guid itemId, [FromBody] ScheduleRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.ScheduleItemAsync(planId, itemId, request.Scheduled, request.PlannedDate, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/complete-accepted")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> CompleteAccepted(Guid planId, [FromBody] CompleteAcceptedRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.CompleteAcceptedAsync(planId, request.RowVersion, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/items/{itemId:guid}/complete")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> CompleteItem(Guid planId, Guid itemId, CancellationToken ct) =>
        Run(async () => Ok(await plans.CompleteItemAsync(planId, itemId, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/reference-code")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> SetReferenceCode(Guid planId, [FromBody] ReferenceCodeRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.SetReferenceCodeAsync(planId, request.Code, request.RowVersion, Actor, ct)));

    /// <summary>Ties the old free-text treatment-plan references of a patient's diagnoses to that patient's plans (by reference code or id). A preview saves nothing; nothing is guessed.</summary>
    [HttpPost("api/patients/{patientId:guid}/treatment-plans/reconcile-references")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Reconcile(Guid patientId, [FromBody] ReconcileRequest request, CancellationToken ct) =>
        Run(async () => Ok(await reconciliation.ReconcileAsync(patientId, request.Apply, Actor, ct)));

    /// <summary>Ties one diagnosis's reference to a plan the person chose (the same patient's plan only).</summary>
    [HttpPost("api/diagnoses/{diagnosisId:guid}/resolve-plan-reference")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> ResolveReference(Guid diagnosisId, [FromBody] ResolveReferenceRequest request, CancellationToken ct) =>
        Run(async () => Ok(await reconciliation.ResolveAsync(diagnosisId, request.PlanId, Actor, ct)));

    [HttpPost("api/treatment-plans/{planId:guid}/revise")]
    [RequirePermission(Permission.ManageTreatmentPlans)]
    [RequireCsrfToken]
    public Task<IActionResult> Revise(Guid planId, [FromBody] ReviseRequest request, CancellationToken ct) =>
        Run(async () => Ok(await plans.ReviseAsync(planId, new ReviseInput(request.Reason, request.ClinicalRationale, request.PatientExplanation,
            request.Phases?.Select(p => new PlanPhaseInput(p.Number, p.Name, p.PlannedVisit, p.TargetDate)).ToList(),
            request.Placements?.Select(p => new PlanPlacementInput(p.ItemId, p.PhaseNumber)).ToList()), request.RowVersion, Actor, ct)));

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
