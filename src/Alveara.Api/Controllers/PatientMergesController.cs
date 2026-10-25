using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;

namespace Alveara.Api.Controllers;

/// <summary>
/// STORY-017: finding possible duplicate patient records and merging two of them, all behind <see cref="Permission.MergePatients"/> (the administrator by default). <c>GET duplicates</c> suggests pairs, strongest first;
/// <c>GET preview</c> shows what a merge of one record into another would do and changes nothing; <c>POST</c> merges, needs a CSRF token and an <c>Idempotency-Key</c> (a repeat is the merge already made), and carries the
/// record versions the screen showed, so a record changed since is a 409 and nothing is merged; <c>GET log</c> lists the merges with who and when. Refusals carry a stable <c>error</c> code (see <see cref="PatientMergeService"/>).
/// </summary>
[ApiController]
[Route("api/patient-merges")]
[Authorize]
public class PatientMergesController(PatientDuplicateScanner scanner, PatientMergeService merges) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<object>> action)
    {
        try { return Ok(await action()); }
        catch (PatientException ex) { return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors }); }
        catch (ConcurrencyConflictException ex) { return Conflict(ex.ToProblem()); }
    }

    [HttpGet("duplicates")]
    [RequirePermission(Permission.MergePatients)]
    public Task<IActionResult> Duplicates(CancellationToken ct) => Run(async () => await scanner.ScanAsync(ct));

    [HttpGet("preview")]
    [RequirePermission(Permission.MergePatients)]
    public Task<IActionResult> Preview([FromQuery] Guid sourceId, [FromQuery] Guid survivorId, CancellationToken ct) => Run(async () => await merges.PreviewAsync(sourceId, survivorId, ct));

    [HttpGet("log")]
    [RequirePermission(Permission.MergePatients)]
    public Task<IActionResult> Log([FromQuery] int? take, CancellationToken ct) => Run(async () => await merges.LogAsync(take ?? 50, ct));

    [HttpPost]
    [RequirePermission(Permission.MergePatients)]
    [RequireCsrfToken]
    public Task<IActionResult> Merge([FromBody] MergeRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () => await merges.MergeAsync(Actor, request ?? throw new PatientException("body_required", "Send the merge request.", 400), idempotencyKey, ct));
}
