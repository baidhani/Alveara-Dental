using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>Hand money back for one earlier payment (by entry number). There is no recipient: it goes to whoever the payment records as having paid. <c>method</c> is how it is returned; <c>reason</c> is required.</summary>
public record RefundRequest(int? PaymentEntryNumber, decimal? Amount, string? Method, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>Move part of what one payment pays: <c>releases</c> name the charges to take money off, <c>allocations</c> the charges to put it on; the two must add up to the same amount.</summary>
public record MoveAllocationRequest(int? PaymentEntryNumber, List<AllocationRequest>? Releases, List<AllocationRequest>? Allocations, string? Reason, string? RowVersion, string? IdempotencyKey);

/// <summary>Correct a payment or adjustment: reverse it exactly and record a linked replacement. <c>reason</c> explains the reversal; the replacement carries <c>amount</c> (and for a payment <c>method</c>, <c>note</c>, <c>paidByPatientId</c>).</summary>
public record CorrectionRequest(int? EntryNumber, string? Reason, decimal? Amount, string? Method, string? Note, Guid? PaidByPatientId, string? RowVersion, string? IdempotencyKey);

/// <summary>Issue the previewed document: <c>previewToken</c> is what the preview returned; <c>idempotencyKey</c> makes a repeated issue return the same document.</summary>
public record IssueDocumentRequest(string? PreviewToken, string? IdempotencyKey);

/// <summary>
/// ALV-007-C02: refunds, moving what a payment pays, corrections, and the account's timeline, aging, statements and receipts. Reading (the timeline, aging, a preview, an issued document and its CSV) needs
/// <see cref="Permission.ViewBilling"/>; refunding, moving, correcting and issuing need <see cref="Permission.ManageBilling"/> plus a CSRF token. Refusals use the same stable <c>error</c> codes as the rest of billing
/// (including <c>document_preview_stale</c> and <c>document_content_changed</c>); a change from an out-of-date copy of the account is the shared 409 concurrency conflict.
/// </summary>
[ApiController]
[Authorize]
public class BillingStatementsController(BillingService billing) : ControllerBase
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

    private static List<AllocationInput>? Rows(List<AllocationRequest>? rows) => rows?.Select(a => new AllocationInput(a.ChargeEntryNumber, a.Amount)).ToList();

    // ---------- reads ----------

    [HttpGet("api/patients/{patientId:guid}/billing/timeline")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Timeline(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.GetTimelineAsync(patientId, ct)));

    /// <summary>Where a charge came from, for a billing reader (the timeline links to it): the procedure and its completion in billing terms, with nothing clinical. 404 <c>source_not_found</c> for anything that is not a charge of this patient.</summary>
    [HttpGet("api/patients/{patientId:guid}/billing/entries/{entryNumber:int}/source")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> ChargeSource(Guid patientId, int entryNumber, CancellationToken ct) => Run(async () => Ok(await billing.GetChargeSourceAsync(patientId, entryNumber, ct)));

    [HttpGet("api/patients/{patientId:guid}/billing/aging")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Aging(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.GetAgingAsync(patientId, ct)));

    /// <summary>The statement or receipt as it would be issued now, with the token issuing needs. Nothing is stored.</summary>
    [HttpGet("api/patients/{patientId:guid}/billing/documents/preview")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Preview(Guid patientId, [FromQuery] string? type, [FromQuery] int? paymentEntryNumber, CancellationToken ct) =>
        Run(async () => Ok(await billing.PreviewDocumentAsync(patientId, new DocumentPreviewInput(type, paymentEntryNumber), ct)));

    [HttpGet("api/patients/{patientId:guid}/billing/documents")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Documents(Guid patientId, CancellationToken ct) => Run(async () => Ok(await billing.ListDocumentsAsync(patientId, ct)));

    /// <summary>An issued document exactly as issued, with what has happened since listed apart. 409 <c>document_content_changed</c> if it can no longer be rebuilt to match its hash.</summary>
    [HttpGet("api/patients/{patientId:guid}/billing/documents/{documentId:guid}")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> Document(Guid patientId, Guid documentId, CancellationToken ct) => Run(async () => Ok(await billing.GetDocumentAsync(patientId, documentId, Actor, ct)));

    [HttpGet("api/patients/{patientId:guid}/billing/documents/{documentId:guid}/csv")]
    [RequirePermission(Permission.ViewBilling)]
    public Task<IActionResult> DocumentCsv(Guid patientId, Guid documentId, CancellationToken ct) =>
        Run(async () =>
        {
            var (name, csv) = await billing.GetStatementCsvAsync(patientId, documentId, Actor, ct);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", name);
        });

    // ---------- changes ----------

    [HttpPost("api/patients/{patientId:guid}/billing/refunds")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Refund(Guid patientId, [FromBody] RefundRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.RefundPaymentAsync(patientId, new RefundInput(request.PaymentEntryNumber, request.Amount, request.Method, request.Reason, request.RowVersion, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/allocations/move")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Move(Guid patientId, [FromBody] MoveAllocationRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.MoveAllocationsAsync(patientId,
            new MoveAllocationInput(request.PaymentEntryNumber, Rows(request.Releases), Rows(request.Allocations), request.Reason, request.RowVersion, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/corrections")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Correct(Guid patientId, [FromBody] CorrectionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.CorrectEntryAsync(patientId,
            new CorrectionInput(request.EntryNumber, request.Reason, request.Amount, request.Method, request.Note, request.PaidByPatientId, request.RowVersion, request.IdempotencyKey), Actor, ct)));

    [HttpPost("api/patients/{patientId:guid}/billing/documents")]
    [RequirePermission(Permission.ManageBilling)]
    [RequireCsrfToken]
    public Task<IActionResult> Issue(Guid patientId, [FromBody] IssueDocumentRequest request, CancellationToken ct) =>
        Run(async () => Ok(await billing.IssueDocumentAsync(patientId, new IssueDocumentInput(request.PreviewToken, request.IdempotencyKey), Actor, ct)));
}
