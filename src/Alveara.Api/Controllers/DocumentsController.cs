using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Documents;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

/// <summary>An edit states everything that can be edited: a context field (encounter, tooth, region) left out or null is cleared.</summary>
public record DocumentEditRequest(Guid? CategoryId, DateOnly? DocumentDate, string? Description, string? RowVersion, Guid? EncounterId = null, string? ToothKey = null, string? RegionKey = null);
public record InactivateDocumentRequest(string? Reason, string? RowVersion);
public record ReactivateDocumentRequest(string? RowVersion);
public record AddCategoryRequest(string? Name);
public record EditCategoryRequest(string? Name, bool? IsActive, string? RowVersion);
public record SizeLimitRequest(long? MaxUploadBytes, string? RowVersion);
public record BlockedExtensionRequest(string? Extension);

/// <summary>
/// STORY-010: documents (docs/DOCUMENTS.md). Reading the library, a document and its file needs <see cref="Permission.ViewDocuments"/>; importing and editing need <see cref="Permission.ImportDocuments"/>
/// (administrator, office manager) plus a CSRF token; the categories' changes, the size limit and the blocked types need <see cref="Permission.ManageDocumentSettings"/> (administrator). A refusal returns a stable
/// <c>error</c> code, a message safe to show and, where there are any, <c>problems</c> (each naming its field) and a <c>detail</c> (the existing document for <c>duplicate_document</c>): <c>file_empty</c>,
/// <c>file_required</c>, <c>file_type_blocked</c>, <c>file_type_mismatch</c>, <c>executable_content</c>, <c>file_name_invalid</c>, <c>metadata_invalid</c>, <c>idempotency_key_required</c>,
/// <c>row_version_required</c> and <c>row_version_invalid</c> (400), <c>file_too_large</c> (413), <c>*_not_found</c> and <c>file_missing</c> (404), <c>duplicate_document</c>,
/// <c>duplicate_confirmation_invalid</c>, <c>idempotency_key_reused</c>, <c>category_exists</c>, <c>last_active_category</c>, <c>built_in_locked</c> and <c>concurrency_conflict</c> (409), <c>store_failed</c> and
/// <c>save_failed</c> (503). Nothing is saved on any refusal. Responses never name where a file is stored.
///
/// Every file is served as a download (<c>Content-Disposition: attachment</c>, a generic type, <c>nosniff</c>) except PDF, PNG and JPEG, which may be shown inline under <c>Content-Security-Policy: sandbox</c>;
/// <c>?download=true</c> forces a download of those too.
/// </summary>
[ApiController]
[Authorize]
public class DocumentsController(DocumentImportService importer, DocumentLibraryService library, DocumentEditService editor, DocumentStatusService status, DocumentSettingsService settings) : ControllerBase
{
    /// <summary>Room for the multipart boundaries and the small text fields around the file when the request size is compared with the limit.</summary>
    private const long FormOverheadBytes = 1_048_576;

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
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- the library ----------

    [HttpGet("api/patients/{patientId:guid}/documents")]
    [RequirePermission(Permission.ViewDocuments)]
    public Task<IActionResult> List(Guid patientId, [FromQuery] string? categoryId, [FromQuery] string? encounterId, [FromQuery] string? toothKey, [FromQuery] string? regionKey, [FromQuery] string? from,
        [FromQuery] string? to, [FromQuery] string? text, [FromQuery] string? status, [FromQuery] bool includeInactive, CancellationToken ct) => Run(async () =>
    {
        await library.EnsurePatientAsync(patientId, ct);
        var problems = new List<DocumentProblem>();
        var filter = new DocumentLibraryFilter(ParseGuid(categoryId, "category", problems), ParseGuid(encounterId, "encounter", problems), toothKey, regionKey, ParseDate(from, "from", problems), ParseDate(to, "to", problems), text,
            status ?? (includeInactive ? DocumentLibraryFilter.StatusAll : null));
        if (problems.Count > 0) throw new DocumentException(DocumentErrorCodes.MetadataInvalid, "The filter has entries that need correcting.", 400, problems);
        return Ok(new { patientId, entries = await library.ListAsync(patientId, filter, ct), maxUploadBytes = (await settings.GetSettingsAsync(ct)).MaxUploadBytes });
    });

    [HttpGet("api/documents/{documentId:guid}")]
    [RequirePermission(Permission.ViewDocuments)]
    public Task<IActionResult> Get(Guid documentId, CancellationToken ct) => Run(async () => Ok(await library.GetAsync(documentId, ct)));

    /// <summary>The file. Opening is logged first; if it cannot be logged nothing is returned.</summary>
    [HttpGet("api/documents/{documentId:guid}/content")]
    [RequirePermission(Permission.ViewDocuments)]
    public async Task<IActionResult> Content(Guid documentId, [FromQuery] bool download, CancellationToken ct)
    {
        OpenedDocument opened;
        try
        {
            opened = await library.OpenAsync(documentId, Actor, ct);
        }
        catch (DocumentException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }

        var inline = opened.InlineSafe && !download;
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue(inline ? "inline" : "attachment") { FileNameStar = opened.FileName }.ToString();
        if (inline) Response.Headers["Content-Security-Policy"] = "sandbox";
        return new FileStreamResult(opened.Content, opened.InlineSafe ? opened.ContentType : "application/octet-stream");        // the result disposes the stream
    }

    /// <summary>The picture of a PNG or JPEG for the library's list: the stored file itself (up to 5 MB), the browser scales it. Not logged as an open. <c>not_an_image</c> (415) or <c>thumbnail_too_large</c> (413) otherwise.</summary>
    [HttpGet("api/documents/{documentId:guid}/thumbnail")]
    [RequirePermission(Permission.ViewDocuments)]
    public async Task<IActionResult> Thumbnail(Guid documentId, CancellationToken ct)
    {
        OpenedDocument opened;
        try
        {
            opened = await library.OpenThumbnailAsync(documentId, ct);
        }
        catch (DocumentException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("inline") { FileNameStar = opened.FileName }.ToString();
        Response.Headers["Content-Security-Policy"] = "sandbox";
        return new FileStreamResult(opened.Content, opened.ContentType);
    }

    // ---------- import and edit ----------

    /// <summary>
    /// Multipart: <c>file</c>, <c>categoryId</c>, <c>documentDate</c> (yyyy-MM-dd), <c>description</c> and, to add a duplicate anyway, <c>confirmDuplicateOf</c>; the <c>Idempotency-Key</c> header is required.
    /// 201 when imported, 200 when the same request was already imported. A request larger than the configured limit is refused (413) before its body is read.
    /// </summary>
    [HttpPost("api/patients/{patientId:guid}/documents")]
    [RequirePermission(Permission.ImportDocuments)]
    [RequireCsrfToken]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue)]
    public Task<IActionResult> Import(Guid patientId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) => Run(async () =>
    {
        var limit = (await settings.GetSettingsAsync(ct)).MaxUploadBytes;
        if (Request.ContentLength > limit + FormOverheadBytes)
            throw new DocumentException(DocumentErrorCodes.FileTooLarge, $"The file is larger than the {limit / (1024 * 1024)} MB limit. Nothing was imported.", StatusCodes.Status413PayloadTooLarge);
        if (!Request.HasFormContentType) throw FileRequired();

        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file") ?? throw FileRequired();
        var problems = new List<DocumentProblem>();
        var category = ParseGuid(form["categoryId"], "category", problems);
        var confirm = ParseGuid(form["confirmDuplicateOf"], "confirmDuplicateOf", problems);
        var date = ParseDate(form["documentDate"], problems);
        var encounter = ParseGuid(form["encounterId"], "encounter", problems);
        if (problems.Count > 0) throw new DocumentException(DocumentErrorCodes.MetadataInvalid, "The document has entries that need correcting. Nothing was imported.", 400, problems);

        await using var content = file.OpenReadStream();
        var result = await importer.ImportAsync(new DocumentImportRequest(patientId, content, file.Length, file.FileName, category, date, form["description"].ToString(), idempotencyKey, confirm, encounter, form["toothKey"].ToString(), form["regionKey"].ToString()), Actor, ct);
        return StatusCode(result.Replayed ? StatusCodes.Status200OK : StatusCodes.Status201Created, new { document = result.Document, replayed = result.Replayed });
    });

    [HttpPut("api/documents/{documentId:guid}")]
    [RequirePermission(Permission.ImportDocuments)]
    [RequireCsrfToken]
    public Task<IActionResult> Edit(Guid documentId, [FromBody] DocumentEditRequest request, CancellationToken ct) =>
        Run(async () => Ok(new { document = await editor.UpdateAsync(documentId, new DocumentEditInput(request.CategoryId, request.DocumentDate, request.Description, request.RowVersion, request.EncounterId, request.ToothKey, request.RegionKey), Actor, ct) }));

    /// <summary>Hides the document from the library (it is never deleted). Needs a reason; repeating it changes nothing.</summary>
    [HttpPost("api/documents/{documentId:guid}/inactivate")]
    [RequirePermission(Permission.ImportDocuments)]
    [RequireCsrfToken]
    public Task<IActionResult> Inactivate(Guid documentId, [FromBody] InactivateDocumentRequest request, CancellationToken ct) =>
        Run(async () => Ok(new { document = await status.InactivateAsync(documentId, request.RowVersion, request.Reason, Actor, ct) }));

    [HttpPost("api/documents/{documentId:guid}/reactivate")]
    [RequirePermission(Permission.ImportDocuments)]
    [RequireCsrfToken]
    public Task<IActionResult> Reactivate(Guid documentId, [FromBody] ReactivateDocumentRequest request, CancellationToken ct) =>
        Run(async () => Ok(new { document = await status.ReactivateAsync(documentId, request.RowVersion, Actor, ct) }));

    // ---------- categories ----------

    [HttpGet("api/document-categories")]
    [RequirePermission(Permission.ViewDocuments)]
    public Task<IActionResult> Categories([FromQuery] bool includeInactive, CancellationToken ct) => Run(async () => Ok(new { categories = await settings.ListCategoriesAsync(includeInactive, ct) }));

    [HttpPost("api/document-categories")]
    [RequirePermission(Permission.ManageDocumentSettings)]
    [RequireCsrfToken]
    public Task<IActionResult> AddCategory([FromBody] AddCategoryRequest request, CancellationToken ct) =>
        Run(async () => StatusCode(StatusCodes.Status201Created, new { category = await settings.AddCategoryAsync(request.Name, Actor, ct) }));

    [HttpPut("api/document-categories/{categoryId:guid}")]
    [RequirePermission(Permission.ManageDocumentSettings)]
    [RequireCsrfToken]
    public Task<IActionResult> EditCategory(Guid categoryId, [FromBody] EditCategoryRequest request, CancellationToken ct) =>
        Run(async () => Ok(new { category = await settings.UpdateCategoryAsync(categoryId, new CategoryEditInput(request.Name, request.IsActive, request.RowVersion), Actor, ct) }));

    // ---------- the size limit and the blocked types ----------

    [HttpGet("api/document-settings")]
    [RequirePermission(Permission.ManageDocumentSettings)]
    public Task<IActionResult> GetSettings(CancellationToken ct) => Run(async () => Ok(await settings.GetSettingsAsync(ct)));

    [HttpPut("api/document-settings")]
    [RequirePermission(Permission.ManageDocumentSettings)]
    [RequireCsrfToken]
    public Task<IActionResult> SetSizeLimit([FromBody] SizeLimitRequest request, CancellationToken ct) =>
        Run(async () => Ok(await settings.SetMaxUploadBytesAsync(request.MaxUploadBytes, request.RowVersion, Actor, ct)));

    [HttpGet("api/document-settings/blocked-extensions")]
    [RequirePermission(Permission.ViewDocuments)]
    public Task<IActionResult> BlockedExtensions(CancellationToken ct) => Run(async () => Ok(new { blockedExtensions = await settings.ListBlockedAsync(ct) }));

    [HttpPost("api/document-settings/blocked-extensions")]
    [RequirePermission(Permission.ManageDocumentSettings)]
    [RequireCsrfToken]
    public Task<IActionResult> AddBlockedExtension([FromBody] BlockedExtensionRequest request, CancellationToken ct) =>
        Run(async () => Ok(new { blockedExtension = await settings.AddBlockedAsync(request.Extension, Actor, ct) }));

    [HttpDelete("api/document-settings/blocked-extensions/{extension}")]
    [RequirePermission(Permission.ManageDocumentSettings)]
    [RequireCsrfToken]
    public Task<IActionResult> RemoveBlockedExtension(string extension, CancellationToken ct) => Run(async () =>
    {
        await settings.RemoveBlockedAsync(extension, Actor, ct);
        return NoContent();
    });

    // ---------- helpers ----------

    private static DocumentException FileRequired() => new("file_required", "Choose a file to import. Nothing was imported.", 400);

    private static Guid? ParseGuid(string? raw, string field, List<DocumentProblem> problems)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (Guid.TryParse(raw, out var id)) return id;
        problems.Add(new DocumentProblem(field, "invalid", "That value is not valid."));
        return null;
    }

    private static DateOnly? ParseDate(string? raw, List<DocumentProblem> problems, string field = "documentDate")
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        problems.Add(new DocumentProblem(field, "invalid", "Enter the date as year-month-day, for example 2026-10-01."));
        return null;
    }

    private static DateOnly? ParseDate(string? raw, string field, List<DocumentProblem> problems) => ParseDate(raw, problems, field);
}
