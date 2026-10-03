using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record StartEncounterRequest(Guid? AppointmentId, DateTimeOffset? EncounterAtUtc);
public record AddEntryRequest(string? Kind, string? Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency, string? RowVersion)
{
    public EntryFields Fields => new(Name, Detail, Reaction, Severity, Dose, Frequency);
}
public record UpdateEntryRequest(string? Name, string? Detail, string? Reaction, string? Severity, string? Dose, string? Frequency, string? RowVersion)
{
    public EntryFields Fields => new(Name, Detail, Reaction, Severity, Dose, Frequency);
}
public record EncounterVersionRequest(string? RowVersion);
public record AddendumRequest(string? Text);

/// <summary>
/// STORY-005: a patient's clinical documentation - encounters holding structured medical and dental history, allergies and medications, finalized and then amended
/// by addendum. Reading needs <see cref="Permission.ViewClinicalDocumentation"/> (held by dentist, hygienist, assistant and admin - not by front desk, billing or the
/// practice manager); every change needs <see cref="Permission.ManageClinicalNotes"/> (dentist, hygienist, admin), a CSRF token and the row version the caller read.
/// Starting and adding an addendum take an Idempotency-Key so a retry never repeats them. A refusal returns a stable <c>error</c> code plus a message safe to show
/// and, where several things need attention, <c>fieldErrors</c>; a stale edit is the shared 409 concurrency conflict.
/// </summary>
[ApiController]
[Authorize]
public class ClinicalController(EncounterService encounters, EncounterReader reader) : ControllerBase
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

    // ---------- reading ----------

    [HttpGet("api/patients/{patientId:guid}/encounters")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> List(Guid patientId, CancellationToken ct) => Run(async () => Ok(await reader.ListForPatientAsync(patientId, ct)));

    [HttpGet("api/encounters/{encounterId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Get(Guid encounterId, CancellationToken ct) => Run(async () => Ok(await reader.GetAsync(encounterId, ct)));

    // ---------- documenting ----------

    /// <summary>Starts a draft encounter (201), or returns the one already started for that appointment or with that Idempotency-Key (200).</summary>
    [HttpPost("api/patients/{patientId:guid}/encounters")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Start(Guid patientId, [FromBody] StartEncounterRequest? request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () =>
        {
            var (detail, created) = await encounters.StartAsync(patientId, request?.AppointmentId, request?.EncounterAtUtc, idempotencyKey, Actor, ct);
            return created ? Created($"api/encounters/{detail.Id}", detail) : Ok(detail);
        });

    [HttpPost("api/encounters/{encounterId:guid}/entries")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> AddEntry(Guid encounterId, [FromBody] AddEntryRequest request, CancellationToken ct) =>
        Run(async () => Ok(await encounters.AddEntryAsync(encounterId, request.Kind, request.Fields, request.RowVersion, Actor, ct)));

    [HttpPut("api/encounters/{encounterId:guid}/entries/{entryId:guid}")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateEntry(Guid encounterId, Guid entryId, [FromBody] UpdateEntryRequest request, CancellationToken ct) =>
        Run(async () => Ok(await encounters.UpdateEntryAsync(encounterId, entryId, request.Fields, request.RowVersion, Actor, ct)));

    /// <summary>Removes an entry from the active documentation (it is kept, never deleted).</summary>
    [HttpPost("api/encounters/{encounterId:guid}/entries/{entryId:guid}/remove")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> RemoveEntry(Guid encounterId, Guid entryId, [FromBody] EncounterVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await encounters.RemoveEntryAsync(encounterId, entryId, request.RowVersion, Actor, ct)));

    /// <summary>Records that a section was reviewed and there is nothing to report ("MedicalHistory", "DentalHistory", "Allergy" or "Medication").</summary>
    [HttpPost("api/encounters/{encounterId:guid}/sections/{kind}/none-reported")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> MarkNoneReported(Guid encounterId, string kind, [FromBody] EncounterVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await encounters.MarkSectionNoneReportedAsync(encounterId, kind, request.RowVersion, Actor, ct)));

    [HttpPost("api/encounters/{encounterId:guid}/sections/{kind}/clear-review")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ClearReview(Guid encounterId, string kind, [FromBody] EncounterVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await encounters.ClearSectionReviewAsync(encounterId, kind, request.RowVersion, Actor, ct)));

    // ---------- finalizing and amending ----------

    [HttpPost("api/encounters/{encounterId:guid}/finalize")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Finalize(Guid encounterId, [FromBody] EncounterVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await encounters.FinalizeAsync(encounterId, request.RowVersion, Actor, ct)));

    /// <summary>Adds an addendum to a finalized encounter (201), or returns the one already added with that Idempotency-Key (200).</summary>
    [HttpPost("api/encounters/{encounterId:guid}/addenda")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> AddAddendum(Guid encounterId, [FromBody] AddendumRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () =>
        {
            var (detail, created) = await encounters.AddAddendumAsync(encounterId, request.Text, idempotencyKey, Actor, ct);
            return created ? Created($"api/encounters/{encounterId}", detail) : Ok(detail);
        });
}
