using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Controllers;

/// <summary>
/// ALV-018-C02: bulk import batches. Everything here needs <see cref="Permission.BulkImportData"/>. The upload is a POST with a CSRF token whose body is the CSV file itself, with the kind and the operator's label in
/// the query string (a file can be large, so it is read up to one byte past the limit and an oversized file is refused without reading it all). Responses are <c>no-store</c> because a batch describes a file of personal
/// details. Failures return the stable <c>error</c> code the screens map to words; a refused file returns <c>import_file_rejected</c> with its problems (row, column name, code, message), never a value from the file.
/// </summary>
[ApiController]
[Route("api/import-batches")]
[Authorize]
[RequirePermission(Permission.BulkImportData)]
public class ImportBatchesController(ImportIntakeService intake, IOptions<ImportOptions> options) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    [RequireCsrfToken]
    public async Task<IActionResult> Upload([FromQuery] string? sourceKind, [FromQuery] string? label, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var content = await ReadBodyAsync(ct);
            var result = await intake.UploadAsync(sourceKind, label, content, Actor, ct);
            return result.Created ? Created($"/api/import-batches/{result.Batch.Id}", result) : Ok(result);
        }
        catch (ImportFileRejectedException ex)
        {
            return BadRequest(new { error = "import_file_rejected", message = ex.Message, problems = ex.Problems, totalProblems = ex.TotalProblems });
        }
        catch (ImportException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? limit, [FromQuery] DateTimeOffset? before, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(await intake.ListAsync(limit ?? ImportIntakeService.DefaultLimit, before, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var batch = await intake.GetAsync(id, ct);
        return batch is null ? NotFound(new { error = "batch_not_found", message = "There is no import batch with that id." }) : Ok(batch);
    }

    /// <summary>Reads the request body up to one byte past the file limit, so a file over the limit is detected without reading all of it; the parser then refuses it as too large.</summary>
    private async Task<byte[]> ReadBodyAsync(CancellationToken ct)
    {
        var limit = options.Value.MaxFileBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while (buffer.Length <= limit && (read = await Request.Body.ReadAsync(chunk, ct)) > 0) buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }
}
