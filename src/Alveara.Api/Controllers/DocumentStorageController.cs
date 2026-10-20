using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record RemoveOrphansRequest(List<Guid>? BlobIds);

/// <summary>
/// ALV-010-C01: the storage check for documents (docs/DOCUMENTS.md, "Storage integrity"). Every route needs <see cref="Permission.ManageDocumentStorage"/> (administrator, office manager); a check needs a CSRF token.
/// <c>GET /api/document-storage</c> is the current picture without running anything; <c>POST /api/document-storage/check</c> checks every stored file (it hashes each one, so it takes time in proportion to the library);
/// <c>POST /api/document-storage/documents/{id}/check</c> checks one; <c>POST /api/document-storage/orphans/remove</c> removes unreferenced files a reviewer names. A refusal returns a stable <c>error</c> code and a message safe to show: <c>document_not_found</c> (404), <c>file_unreadable</c> and
/// <c>save_failed</c> (503). The report names files because only people who manage the library can see it; the log never does.
/// </summary>
[ApiController]
[Authorize]
[RequirePermission(Permission.ManageDocumentStorage)]
public class DocumentStorageController(DocumentIntegrityService integrity) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (DocumentException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, problems = ex.Problems, detail = ex.Detail });
        }
    }

    [HttpGet("api/document-storage")]
    public Task<IActionResult> Summary(CancellationToken ct) => Run(async () => Ok(await integrity.SummaryAsync(ct)));

    [HttpPost("api/document-storage/check")]
    [RequireCsrfToken]
    public Task<IActionResult> CheckAll(CancellationToken ct) => Run(async () => Ok(await integrity.CheckAllAsync(Actor, ct)));

    /// <summary>
    /// Removes the stored files a reviewer names (up to 500) that no document points to. Each file is re-checked at the moment it is removed (still unreferenced, older than an hour) and gets its own outcome:
    /// Removed, AlreadyGone, StillReferenced, TooNew or Failed (nothing logged and the file kept). Each removal is logged with the file's id. Naming nothing or more than 500 is <c>metadata_invalid</c> (400).
    /// </summary>
    [HttpPost("api/document-storage/orphans/remove")]
    [RequireCsrfToken]
    public Task<IActionResult> RemoveOrphans([FromBody] RemoveOrphansRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            var report = await integrity.RemoveOrphansAsync(request.BlobIds ?? [], Actor, ct);
            return Ok(new { results = report.Results, removed = report.Removed });
        });

    [HttpPost("api/document-storage/documents/{documentId:guid}/check")]
    [RequireCsrfToken]
    public Task<IActionResult> CheckOne(Guid documentId, CancellationToken ct) => Run(async () => Ok(await integrity.CheckDocumentAsync(documentId, Actor, ct)));
}
