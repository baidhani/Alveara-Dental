using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record FormFieldRequest(string? Id, string? Label, string? Kind, bool Required, List<string>? Options)
{
    public FormFieldDefinition ToDefinition() => new(Id ?? string.Empty, Label ?? string.Empty, Kind ?? string.Empty, Required, Options);
}
public record SaveTemplateRequest(string? Key, string? Category, string? Title, string? Body, List<FormFieldRequest>? Fields, string? ChangeNote, string? RowVersion)
{
    public TemplateContent Content => new(Title, Body, Fields?.Select(f => f.ToDefinition()).ToList(), ChangeNote);
}
public record SetTemplateActiveRequest(bool IsActive, string? RowVersion);
public record SetTemplateRequiredRequest(bool Required, string? RowVersion);
public record StartFormRequest(Guid TemplateId);
public record SaveResponsesRequest(Dictionary<string, string?>? Responses, string? RowVersion);
public record RowVersionRequest(string? RowVersion);
public record SignFormRequest(string? SignerName, string? Relationship, string? RelationshipNote, string? SignatureText, bool Attested, Guid TemplateVersionId, string? RowVersion)
{
    public SignInput Input => new(SignerName, Relationship, RelationshipNote, SignatureText, Attested, TemplateVersionId, RowVersion);
}
public record VoidFormRequest(string? Reason, string? RowVersion);

/// <summary>
/// ALV-N010: form/consent templates (administration), and a patient's forms (start, complete, sign, void, history). Template
/// administration needs <see cref="Permission.ManageFormTemplates"/>; completing, signing and discarding drafts needs
/// <see cref="Permission.CompleteForms"/>; reading forms and signed copies needs <see cref="Permission.ViewSignedForms"/>;
/// voiding a SIGNED form additionally needs <see cref="Permission.VoidForms"/>. Every state-changing call needs a CSRF token and
/// the row version the caller read; signing needs an Idempotency-Key. Failures return a stable <c>error</c> code.
/// </summary>
[ApiController]
[Authorize]
public class FormsController(FormTemplateService templates, PatientFormService forms, PatientFormReader reader, CheckInReadinessService readiness) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private bool Can(Permission permission) =>
        Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) && PermissionMatrix.RoleHas(role, permission);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (FormException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors, existingId = ex.ExistingId });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- Template administration ----------

    [HttpGet("api/forms/templates")]
    [RequirePermission(Permission.ManageFormTemplates)]
    public Task<IActionResult> ListTemplates([FromQuery] bool includeInactive = true, CancellationToken ct = default) =>
        Run(async () => Ok(await templates.ListAsync(includeInactive, ct)));

    /// <summary>The active templates a staff member can start for a patient.</summary>
    [HttpGet("api/forms/templates/available")]
    [RequirePermission(Permission.CompleteForms)]
    public Task<IActionResult> AvailableTemplates(CancellationToken ct) => Run(async () => Ok(await templates.ListAsync(false, ct)));

    [HttpGet("api/forms/templates/{id:guid}")]
    [RequirePermission(Permission.ManageFormTemplates)]
    public Task<IActionResult> GetTemplate(Guid id, CancellationToken ct) => Run(async () => Ok(await templates.GetAsync(id, ct)));

    [HttpPost("api/forms/templates")]
    [RequirePermission(Permission.ManageFormTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateTemplate([FromBody] SaveTemplateRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            var created = await templates.CreateAsync(request.Key, request.Category, request.Content, Actor, ct);
            return Created($"/api/forms/templates/{created.Template.Id}", created);
        });

    /// <summary>Publishes a new version of the template (the previous versions are untouched).</summary>
    [HttpPut("api/forms/templates/{id:guid}")]
    [RequirePermission(Permission.ManageFormTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> PublishVersion(Guid id, [FromBody] SaveTemplateRequest request, CancellationToken ct) =>
        Run(async () => Ok(await templates.PublishVersionAsync(id, request.Content, request.RowVersion, Actor, ct)));

    [HttpPut("api/forms/templates/{id:guid}/active")]
    [RequirePermission(Permission.ManageFormTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> SetTemplateActive(Guid id, [FromBody] SetTemplateActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(await templates.SetActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct)));

    /// <summary>ALV-011-C01: marks the template as required (or not) at check-in. Drives only the readiness cue; publishes no version and changes no form.</summary>
    [HttpPut("api/forms/templates/{id:guid}/required-at-check-in")]
    [RequirePermission(Permission.ManageFormTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> SetTemplateRequired(Guid id, [FromBody] SetTemplateRequiredRequest request, CancellationToken ct) =>
        Run(async () => Ok(await templates.SetRequiredAtCheckInAsync(id, request.Required, request.RowVersion, Actor, ct)));

    // ---------- A patient's forms ----------

    /// <summary>ALV-011-C01: which required forms the patient has completed, for the check-in cue. Read-only; reports status, never form contents.</summary>
    [HttpGet("api/patients/{patientId:guid}/forms/check-in-readiness")]
    [RequirePermission(Permission.ViewSignedForms)]
    public Task<IActionResult> CheckInReadiness(Guid patientId, CancellationToken ct) => Run(async () => Ok(await readiness.ForPatientAsync(patientId, ct)));

    [HttpGet("api/patients/{patientId:guid}/forms")]
    [RequirePermission(Permission.ViewSignedForms)]
    public Task<IActionResult> ListForPatient(Guid patientId, CancellationToken ct) => Run(async () => Ok(await reader.ListForPatientAsync(patientId, ct)));

    /// <summary>What a document library can index: the patient's signed forms (voided ones flagged).</summary>
    [HttpGet("api/patients/{patientId:guid}/signed-documents")]
    [RequirePermission(Permission.ViewSignedForms)]
    public Task<IActionResult> SignedDocuments(Guid patientId, CancellationToken ct) => Run(async () => Ok(await reader.SignedDocumentsAsync(patientId, ct)));

    [HttpPost("api/patients/{patientId:guid}/forms")]
    [RequirePermission(Permission.CompleteForms)]
    [RequireCsrfToken]
    public Task<IActionResult> Start(Guid patientId, [FromBody] StartFormRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            var (detail, created) = await forms.StartAsync(patientId, request.TemplateId, Actor, ct);
            return created ? Created($"/api/forms/{detail.Summary.Id}", detail) : Ok(detail);
        });

    [HttpGet("api/forms/{formId:guid}")]
    [RequirePermission(Permission.ViewSignedForms)]
    public Task<IActionResult> Get(Guid formId, CancellationToken ct) => Run(async () => Ok(await reader.GetAsync(formId, ct)));

    [HttpPut("api/forms/{formId:guid}/responses")]
    [RequirePermission(Permission.CompleteForms)]
    [RequireCsrfToken]
    public Task<IActionResult> SaveResponses(Guid formId, [FromBody] SaveResponsesRequest request, CancellationToken ct) =>
        Run(async () => Ok(await forms.SaveDraftAsync(formId, request.Responses, request.RowVersion, Actor, ct)));

    [HttpPost("api/forms/{formId:guid}/restart")]
    [RequirePermission(Permission.CompleteForms)]
    [RequireCsrfToken]
    public Task<IActionResult> RestartOnLatest(Guid formId, [FromBody] RowVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok((await forms.RestartOnLatestAsync(formId, request.RowVersion, Actor, ct)).Detail));

    [HttpPost("api/forms/{formId:guid}/sign")]
    [RequirePermission(Permission.CompleteForms)]
    [RequireCsrfToken]
    public Task<IActionResult> Sign(Guid formId, [FromBody] SignFormRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () =>
        {
            var (detail, created) = await forms.SignAsync(formId, request.Input, idempotencyKey, Actor, ct);
            return created ? Created($"/api/forms/{formId}", detail) : Ok(detail); // a replay of the same submit returns the same signed form
        });

    [HttpPost("api/forms/{formId:guid}/void")]
    [RequirePermission(Permission.CompleteForms)]
    [RequireCsrfToken]
    public Task<IActionResult> Void(Guid formId, [FromBody] VoidFormRequest request, CancellationToken ct) =>
        Run(async () => Ok(await forms.VoidAsync(formId, request.Reason, request.RowVersion, Can(Permission.VoidForms), Actor, ct)));
}
