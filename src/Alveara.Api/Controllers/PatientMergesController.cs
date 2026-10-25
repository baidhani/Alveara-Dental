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
/// record versions the screen showed, so a record changed since is a 409 and nothing is merged; <c>GET log</c> lists the merges with who and when; <c>dismissals</c> list, make and restore "not a duplicate" decisions; <c>{mergeId}/unmerge-preview</c> shows what undoing a merge would do (and changes nothing) and <c>{mergeId}/unmerge</c> undoes it, with the same confirmation, key and record-version rules. Refusals carry a stable <c>error</c> code (see <see cref="PatientMergeService"/>).
/// </summary>
[ApiController]
[Route("api/patient-merges")]
[Authorize]
public class PatientMergesController(PatientDuplicateScanner scanner, PatientMergeService merges, PatientDuplicateReviewService review, PatientUnmergeService unmerges) : ControllerBase
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

    // ---------- pairs dismissed as not a duplicate (ALV-017-C01) ----------

    [HttpGet("dismissals")]
    [RequirePermission(Permission.MergePatients)]
    public Task<IActionResult> Dismissals(CancellationToken ct) => Run(async () => await review.ListAsync(ct));

    [HttpPost("dismissals")]
    [RequirePermission(Permission.MergePatients)]
    [RequireCsrfToken]
    public Task<IActionResult> Dismiss([FromBody] DismissRequest request, CancellationToken ct) =>
        Run(async () => await review.DismissAsync(Actor, request ?? throw new PatientException("body_required", "Send the pair to dismiss.", 400), ct));

    [HttpPost("dismissals/{id:guid}/restore")]
    [RequirePermission(Permission.MergePatients)]
    [RequireCsrfToken]
    public Task<IActionResult> RestoreDismissal(Guid id, CancellationToken ct) => Run(async () => await review.RestoreAsync(Actor, id, ct));

    // ---------- undoing a merge (ALV-017-C01) ----------

    [HttpGet("{mergeId:guid}/unmerge-preview")]
    [RequirePermission(Permission.MergePatients)]
    public Task<IActionResult> UnmergePreview(Guid mergeId, CancellationToken ct) => Run(async () => await unmerges.PreviewAsync(mergeId, ct));

    [HttpPost("{mergeId:guid}/unmerge")]
    [RequirePermission(Permission.MergePatients)]
    [RequireCsrfToken]
    public Task<IActionResult> Unmerge(Guid mergeId, [FromBody] UnmergeRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () => await unmerges.UnmergeAsync(Actor, (request ?? throw new PatientException("body_required", "Send the undo request.", 400)) with { MergeId = mergeId }, idempotencyKey, ct));

    [HttpPost]
    [RequirePermission(Permission.MergePatients)]
    [RequireCsrfToken]
    public Task<IActionResult> Merge([FromBody] MergeRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () => await merges.MergeAsync(Actor, request ?? throw new PatientException("body_required", "Send the merge request.", 400), idempotencyKey, ct));
}
