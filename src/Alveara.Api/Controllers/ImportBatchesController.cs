using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Controllers;

/// <summary>The body of a mapping save: column name to patient field key (an empty target ignores the column).</summary>
public sealed record SaveMappingRequest(Dictionary<string, string?>? Mapping);

/// <summary>A person's decision about one likely duplicate: RegisterAsNew or Skip.</summary>
public sealed record DecideRowRequest(string? Decision);

/// <summary>The confirmation of a commit: the policy (AllOrNothing or CommitValidRows) and the number of valid rows the operator was shown.</summary>
public sealed record CommitRequest(string? Policy, int? ExpectedValidRows);

/// <summary>
/// ALV-018-C02: bulk import batches. Everything here needs <see cref="Permission.BulkImportData"/>. The upload is a POST with a CSRF token whose body is the CSV file itself, with the kind and the operator's label in
/// the query string (a file can be large, so it is read up to one byte past the limit and an oversized file is refused without reading it all). Responses are <c>no-store</c> because a batch describes a file of personal
/// details. Failures return the stable <c>error</c> code the screens map to words; a refused file returns <c>import_file_rejected</c> with its problems (row, column name, code, message), never a value from the file.
/// </summary>
[ApiController]
[Route("api/import-batches")]
[Authorize]
[RequirePermission(Permission.BulkImportData)]
public class ImportBatchesController(ImportIntakeService intake, ImportMappingService mappings, ImportDryRunService dryRuns, ImportDecisionService decisions, ImportCommitService commits, IOptions<ImportOptions> options) : ControllerBase
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

    [HttpGet("{id:guid}/mapping-suggestion")]
    public Task<IActionResult> MappingSuggestion(Guid id, CancellationToken ct) => GuardedAsync(async () => Ok(await mappings.GetSuggestionAsync(id, ct)));

    [HttpPut("{id:guid}/mapping")]
    [RequireCsrfToken]
    public Task<IActionResult> SaveMapping(Guid id, [FromBody] SaveMappingRequest? request, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await mappings.SaveAsync(id, request?.Mapping, Actor, ct)));

    [HttpPost("{id:guid}/dry-run")]
    [RequireCsrfToken]
    public Task<IActionResult> DryRun(Guid id, CancellationToken ct) => GuardedAsync(async () => Ok(await dryRuns.RunAsync(id, Actor, ct)));

    [HttpGet("{id:guid}/rows")]
    public Task<IActionResult> Rows(Guid id, [FromQuery] string? status, [FromQuery] int? limit, [FromQuery] int? after, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await dryRuns.ListRowsAsync(id, status, limit ?? 0, after ?? 1, ct)));

    [HttpPost("{id:guid}/rows/{rowNumber:int}/decision")]
    [RequireCsrfToken]
    public Task<IActionResult> DecideRow(Guid id, int rowNumber, [FromBody] DecideRowRequest? request, CancellationToken ct) =>
        GuardedAsync(async () => Ok(await decisions.DecideAsync(id, rowNumber, request?.Decision, Actor, ct)));

    /// <summary>Confirms a commit. The work runs on the background queue, so the answer is 202 with the batch as it now stands (Committing); its counters and outcome settle when the jobs have run.</summary>
    [HttpPost("{id:guid}/commit")]
    [RequireCsrfToken]
    public Task<IActionResult> Commit(Guid id, [FromBody] CommitRequest? request, CancellationToken ct) =>
        GuardedAsync(async () => Accepted(await commits.CommitAsync(id, request?.Policy, request?.ExpectedValidRows, Actor, ct)));

    /// <summary>Runs one of the mapping, dry-run, decision and commit calls: no-store, and a refusal becomes its stable <c>error</c> code (a rejected mapping lists its problems).</summary>
    private async Task<IActionResult> GuardedAsync(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            return await action();
        }
        catch (ImportMappingRejectedException ex)
        {
            return BadRequest(new { error = "mapping_invalid", message = ex.Message, problems = ex.Problems });
        }
        catch (ImportException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
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
