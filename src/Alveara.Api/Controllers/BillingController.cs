using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>A charge names the completed plan procedure to bill and nothing else: the amount is the fee the plan copied, never the caller's. <c>idempotencyKey</c> makes a retried save return the entry the first save made.</summary>
public record ChargeRequest(Guid? PlanItemId, string? IdempotencyKey);

/// <summary>One charge, by entry number, and how much of a payment goes to it.</summary>
public record AllocationRequest(int? ChargeEntryNumber, decimal? Amount);

/// <summary>
/// Money received. <c>amount</c> is a positive number of dollars and cents; <c>method</c> names one of the practice's active payment methods; <c>rowVersion</c> is the account version the caller last read.
/// <c>allocations</c> name the charges the payment pays (left out, it is applied to the oldest open charges first); <c>paidByPatientId</c> is who paid when that is not the patient.
/// </summary>
public record PaymentRequest(decimal? Amount, string? Method, string? Note, string? RowVersion, string? IdempotencyKey, List<AllocationRequest>? Allocations = null, Guid? PaidByPatientId = null);

/// <summary>Apply what is left of an earlier payment (by entry number) to the charges named, or to the oldest open ones when none are.</summary>
public record ApplyPaymentRequest(int? PaymentEntryNumber, List<AllocationRequest>? Allocations, string? RowVersion, string? IdempotencyKey);

/// <summary>Undo one earlier entry (by entry number) with a new entry that says why.</summary>
public record ReversalRequest(int? EntryNumber, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>Who is responsible for the account (the patient or another active patient), from a date that is not in the future; the account version is needed once the account exists.</summary>
public record ResponsibilityRequest(Guid? ResponsiblePatientId, DateOnly? EffectiveFrom, string? Reason, string? RowVersion);

public record PaymentMethodRequest(string? Name, string? Kind);

public record PaymentMethodActiveRequest(bool? Active, string? RowVersion);

/// <summary>A correction: <c>amount</c> is signed (negative reduces what is owed) and <c>reason</c> is required.</summary>
public record AdjustmentRequest(decimal? Amount, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>
/// STORY-007 and ALV-007-C01: a patient's billing account. Reading it (the ledger with its running balance, the completed procedures not yet billed, the check that the history adds up, who is responsible for the
/// account, a guarantor's summary and the payment methods) needs <see cref="Permission.ViewBilling"/>; recording a charge, a payment, applying a payment, an adjustment, a reversal, who is responsible or a payment
/// method needs <see cref="Permission.ManageBilling"/> plus a CSRF token. A refusal returns a stable <c>error</c> code, a message safe to show and, where several fields need
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

    /// <summary>Rebuilds the balance and every derived number from the preserved entries and reports anything that does not obey the ledger rules.</summary>
    [HttpGet("api/patients/{patientId:guid}/billing/verification")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Verification(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.VerifyLedgerAsync(patientId, ct)));

    [HttpGet("api/patients/{patientId:guid}/billing/responsibility")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Responsibility(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.GetResponsibilityAsync(patientId, ct)));

    /// <summary>Everything this patient is financially responsible for, with each account's own balance and the total.</summary>
    [HttpGet("api/patients/{patientId:guid}/billing/guarantor-summary")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> GuarantorSummary(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.GetGuarantorSummaryAsync(patientId, ct)));

    [HttpGet("api/billing/payment-methods")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> PaymentMethods([FromQuery] bool includeInactive, CancellationToken ct) => Run(async () => Ok(await billing.ListPaymentMethodsAsync(includeInactive, ct)));

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
        Run(async () => Ok(await billing.RecordPaymentAsync(patientId,
            new PaymentInput(request.Amount, request.Method, request.Note, request.RowVersion, request.IdempotencyKey, request.Allocations?.Select(a => new AllocationInput(a.ChargeEntryNumber, a.Amount)).ToList(), request.PaidByPatientId), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/payments/apply")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> ApplyPayment(Guid patientId, [FromBody] ApplyPaymentRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.ApplyPaymentAsync(patientId,
            new ApplyPaymentInput(request.PaymentEntryNumber, request.Allocations?.Select(a => new AllocationInput(a.ChargeEntryNumber, a.Amount)).ToList(), request.RowVersion, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/reversals")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Reverse(Guid patientId, [FromBody] ReversalRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.ReverseEntryAsync(patientId, new ReversalInput(request.EntryNumber, request.Reason, request.RowVersion, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/responsibility")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> RecordResponsibility(Guid patientId, [FromBody] ResponsibilityRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.RecordResponsibilityAsync(patientId, new ResponsibilityInput(request.ResponsiblePatientId, request.EffectiveFrom, request.Reason, request.RowVersion), Actor, ct)));

    [HttpPost("api/billing/payment-methods")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> AddPaymentMethod([FromBody] PaymentMethodRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.AddPaymentMethodAsync(new PaymentMethodInput(request.Name, request.Kind), Actor, ct)));

    [HttpPost("api/billing/payment-methods/{methodId:guid}/active")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> SetPaymentMethodActive(Guid methodId, [FromBody] PaymentMethodActiveRequest request, CancellationToken ct) =>
        Run(async () => request.Active is null
            ? BadRequest(new { error = "validation_failed", message = "Some entries need attention.", fieldErrors = new Dictionary<string, string> { ["active"] = "Say whether the method is in use (true) or no longer used (false)." } })
            : Ok(await billing.SetPaymentMethodActiveAsync(methodId, request.Active.Value, request.RowVersion, Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/adjustments")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Adjustment(Guid patientId, [FromBody] AdjustmentRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.RecordAdjustmentAsync(patientId, new AdjustmentInput(request.Amount, request.Reason, request.RowVersion, request.IdempotencyKey), Actor, ct)));
}
