using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Periodontal;

namespace Alveara.Api.Controllers;

public record RecordPerioChartRequest(string? IdempotencyKey, List<PerioReadingRequest?>? Readings);

/// <summary>
/// STORY-012: periodontal charting. Reading a patient's charts needs <see cref="Permission.ViewClinicalDocumentation"/>; saving a chart needs <see cref="Permission.ManageClinicalNotes"/> plus a
/// CSRF token - the same permissions as the odontogram and the clinical notes it sits beside. A refusal returns a stable <c>error</c> code, a message safe to show and, for
/// <c>validation_failed</c>, <c>problems</c>: every entry that needs correcting, each with its tooth, site, field, code and message. The tooth is always the FDI key. Saving twice with
/// the same <c>idempotencyKey</c> and readings returns the same chart (200 both times); the same key with different readings is a 409.
/// </summary>
[ApiController]
[Authorize]
public class PerioController(PerioService perio) : PerioControllerBase
{
    [HttpGet("api/patients/{patientId:guid}/periodontal/charts")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> Charts(Guid patientId, [FromQuery] int? take, CancellationToken ct) => Run(async () => Ok(await perio.HistoryAsync(patientId, take, ct)));

    [HttpPost("api/patients/{patientId:guid}/periodontal/charts")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Record(Guid patientId, [FromBody] RecordPerioChartRequest request, CancellationToken ct) =>
        Run(async () => Ok(await perio.RecordAsync(patientId, request.IdempotencyKey, ToInputs(request.Readings), Actor, ct)));
}
