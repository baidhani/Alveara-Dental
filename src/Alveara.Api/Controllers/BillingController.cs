using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>A charge names the completed plan procedure to bill and nothing else: the amount is the fee the plan copied, never the caller's. <c>idempotencyKey</c> makes a retried save return the entry the first save made.</summary>
public record ChargeRequest(Guid? PlanItemId, string? IdempotencyKey);

/// <summary>Money received. <c>amount</c> is a positive number of dollars and cents; <c>rowVersion</c> is the account version the caller last read.</summary>
public record PaymentRequest(decimal? Amount, string? Method, string? Note, string? RowVersion, string? IdempotencyKey);

/// <summary>A correction: <c>amount</c> is signed (negative reduces what is owed) and <c>reason</c> is required.</summary>
public record AdjustmentRequest(decimal? Amount, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>
/// STORY-007: a patient's billing account. Reading it (the ledger with its running balance, and the completed procedures not yet billed) needs <see cref="Permission.ViewBilling"/>; recording a charge, a
/// payment or an adjustment needs <see cref="Permission.ManageBilling"/> plus a CSRF token. A refusal returns a stable <c>error</c> code, a message safe to show and, where several fields need
/// attention, <c>fieldErrors</c>; a payment or adjustment made from an out-of-date copy of the account is the shared 409 concurrency conflict; a save that cannot be completed (including its audit entry) is
/// 503 <c>save_failed</c> and stores nothing. Amounts are US dollars.
/// </summary>
[ApiController]
[Authorize]
public class BillingController(BillingService billing) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BillingException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- reads ----------

    [HttpGet("api/patients/{patientId:guid}/billing")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Account(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.GetAccountAsync(patientId, ct)));

    /// <summary>The completed plan procedures not yet billed, with only what billing needs (billing roles cannot read treatment plans).</summary>
    [HttpGet("api/patients/{patientId:guid}/billing/billable-procedures")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Billable(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.ListBillableAsync(patientId, ct)));

    // ---------- changes ----------

    [HttpPost("api/patients/{patientId:guid}/billing/charges")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Charge(Guid patientId, [FromBody] ChargeRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.RecordChargeAsync(patientId, new ChargeInput(request.PlanItemId, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/payments")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Payment(Guid patientId, [FromBody] PaymentRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.RecordPaymentAsync(patientId, new PaymentInput(request.Amount, request.Method, request.Note, request.RowVersion, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/adjustments")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Adjustment(Guid patientId, [FromBody] AdjustmentRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.RecordAdjustmentAsync(patientId, new AdjustmentInput(request.Amount, request.Reason, request.RowVersion, request.IdempotencyKey), Actor, ct)));
}
