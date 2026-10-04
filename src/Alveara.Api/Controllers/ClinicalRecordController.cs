using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record AddRecordItemRequest(string? Kind, string? Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency, string? Status, Guid? EncounterId)
{
    public EntryFields Fields => new(Name, Detail, Reaction, Severity, Dose, Frequency);
}
public record UpdateRecordItemRequest(string? Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency, string? RowVersion, string? Reason, Guid? EncounterId)
{
    public EntryFields Fields => new(Name, Detail, Reaction, Severity, Dose, Frequency);
}
public record RecordItemStatusRequest(string? Status, string? RowVersion, string? Reason, Guid? EncounterId);
public record RemoveRecordItemRequest(string? RowVersion, string? Reason, Guid? EncounterId);
public record SectionReviewRequest(string? State, Guid? EncounterId);

/// <summary>
/// ALV-005-C01: a patient's longitudinal clinical record - medical and dental history, allergies and medications across encounters, with a status for each item, who
/// reviewed each section and when, and the full history of every item. Reading needs <see cref="Permission.ViewClinicalDocumentation"/>; every change needs
/// <see cref="Permission.ManageClinicalNotes"/> and a CSRF token, and a change to an item echoes the item's row version (a stale edit is the shared 409 concurrency conflict).
/// A refusal returns a stable <c>error</c> code, a message safe to show and, where several things need attention, <c>fieldErrors</c>.
/// </summary>
[ApiController]
[Authorize]
public class ClinicalRecordController(ClinicalRecordService service, ClinicalRecordReader reader) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ClinicalException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    [HttpGet("api/patients/{patientId:guid}/clinical-record")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid patientId, CancellationToken ct) => Run(async () => Ok(await reader.GetAsync(patientId, ct)));

    [HttpGet("api/clinical-record/items/{itemId:guid}/history")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> History(Guid itemId, CancellationToken ct) => Run(async () => Ok(await reader.HistoryAsync(itemId, ct)));

    [HttpPost("api/patients/{patientId:guid}/clinical-record/items")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> AddItem(Guid patientId, [FromBody] AddRecordItemRequest request, CancellationToken ct) =>
        Run(async () => Ok(await service.AddItemAsync(patientId, request.Kind, request.Fields, request.Status, request.EncounterId, Actor, ct)));

    [HttpPut("api/clinical-record/items/{itemId:guid}")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateRecordItemRequest request, CancellationToken ct) =>
        Run(async () => Ok(await service.UpdateItemAsync(itemId, request.Fields, request.RowVersion, request.Reason, request.EncounterId, Actor, ct)));

    [HttpPost("api/clinical-record/items/{itemId:guid}/status")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> SetStatus(Guid itemId, [FromBody] RecordItemStatusRequest request, CancellationToken ct) =>
        Run(async () => Ok(await service.SetStatusAsync(itemId, request.Status, request.RowVersion, request.Reason, request.EncounterId, Actor, ct)));

    /// <summary>Marks an item as entered in error (it is kept, with its history and the reason).</summary>
    [HttpPost("api/clinical-record/items/{itemId:guid}/remove")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Remove(Guid itemId, [FromBody] RemoveRecordItemRequest request, CancellationToken ct) =>
        Run(async () => Ok(await service.RemoveInErrorAsync(itemId, request.RowVersion, request.Reason, request.EncounterId, Actor, ct)));

    /// <summary>States what a clinician says about a section: Reviewed, NoneKnown, Unknown, or NotReviewed (withdraws the statement).</summary>
    [HttpPut("api/patients/{patientId:guid}/clinical-record/sections/{section}/review")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> SetReview(Guid patientId, string section, [FromBody] SectionReviewRequest request, CancellationToken ct) =>
        Run(async () => Ok(await service.SetReviewAsync(patientId, section, request.State, request.EncounterId, Actor, ct)));
}
