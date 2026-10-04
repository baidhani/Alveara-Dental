using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Controllers;

public record SaveNoteRequest(string? Body, string? RowVersion);
public record ApplyTemplateRequest(Guid TemplateId, string? RowVersion);
public record RecordVitalsRequest(
    DateTimeOffset? MeasuredAtUtc, int? SystolicMmHg, int? DiastolicMmHg, int? PulseBpm, int? RespirationsPerMinute, decimal? TemperatureC,
    int? OxygenSaturationPercent, decimal? WeightKg, decimal? HeightCm, string? Note, string? RowVersion)
{
    public VitalsInput Input => new(MeasuredAtUtc, SystolicMmHg, DiastolicMmHg, PulseBpm, RespirationsPerMinute, TemperatureC, OxygenSaturationPercent, WeightKg, HeightCm, Note);
}
public record VoidVitalsRequest(string? Reason, string? RowVersion);
public record NoteTemplateSectionRequest(string? Section, bool Required, string? StarterText);
public record SaveNoteTemplateRequest(string? Name, string? Description, List<NoteTemplateSectionRequest>? Sections, string? RowVersion)
{
    public IReadOnlyList<TemplateSectionInput>? SectionInputs => Sections?.Select(s => new TemplateSectionInput(s.Section, s.Required, s.StarterText)).ToList();
}
public record NoteTemplateActiveRequest(bool Active, string? RowVersion);

/// <summary>
/// ALV-005-C01: the notes of an encounter (SOAP, progress, treatment), its vital signs, the note template used, and signing it. Reading needs
/// <see cref="Permission.ViewClinicalDocumentation"/>; every change to an encounter needs <see cref="Permission.ManageClinicalNotes"/>, a CSRF token and the encounter row
/// version the caller read; configuring templates needs <see cref="Permission.ManageClinicalTemplates"/>. Recording vitals takes an Idempotency-Key so a retry never repeats it.
/// A refusal returns a stable <c>error</c> code, a message safe to show and, where several things need attention, <c>fieldErrors</c>.
/// </summary>
[ApiController]
[Authorize]
public class ClinicalNotesController(EncounterNoteService notes, EncounterVitalsService vitals, EncounterSigningService signing, NoteTemplateService templates) : ControllerBase
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

    // ---------- notes and template ----------

    /// <summary>Saves the text of one note section ("Subjective", "Objective", "Assessment", "Plan", "Progress" or "Treatment") of a draft encounter.</summary>
    [HttpPut("api/encounters/{encounterId:guid}/notes/{section}")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> SaveNote(Guid encounterId, string section, [FromBody] SaveNoteRequest request, CancellationToken ct) =>
        Run(async () => Ok(await notes.SaveNoteAsync(encounterId, section, request.Body, request.RowVersion, Actor, ct)));

    [HttpPost("api/encounters/{encounterId:guid}/template")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> ApplyTemplate(Guid encounterId, [FromBody] ApplyTemplateRequest request, CancellationToken ct) =>
        Run(async () => Ok(await notes.ApplyTemplateAsync(encounterId, request.TemplateId, request.RowVersion, Actor, ct)));

    // ---------- vitals ----------

    /// <summary>Records one set of vital signs (201), or returns the one already recorded with that Idempotency-Key (200).</summary>
    [HttpPost("api/encounters/{encounterId:guid}/vitals")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> RecordVitals(Guid encounterId, [FromBody] RecordVitalsRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () =>
        {
            var (detail, created) = await vitals.RecordAsync(encounterId, request.Input, idempotencyKey, request.RowVersion, Actor, ct);
            return created ? Created($"api/encounters/{encounterId}", detail) : Ok(detail);
        });

    [HttpPost("api/encounters/{encounterId:guid}/vitals/{vitalsId:guid}/void")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> VoidVitals(Guid encounterId, Guid vitalsId, [FromBody] VoidVitalsRequest request, CancellationToken ct) =>
        Run(async () => Ok(await vitals.VoidAsync(encounterId, vitalsId, request.Reason, request.RowVersion, Actor, ct)));

    // ---------- signing ----------

    [HttpPost("api/encounters/{encounterId:guid}/sign")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Sign(Guid encounterId, [FromBody] EncounterVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await signing.SignAsync(encounterId, request.RowVersion, Actor, ct)));

    [HttpPost("api/encounters/{encounterId:guid}/unsign")]
    [RequirePermission(Permission.ManageClinicalNotes)]
    [RequireCsrfToken]
    public Task<IActionResult> Unsign(Guid encounterId, [FromBody] EncounterVersionRequest request, CancellationToken ct) =>
        Run(async () => Ok(await signing.UnsignAsync(encounterId, request.RowVersion, Actor, ct)));

    // ---------- templates ----------

    /// <summary>The templates in use; with <c>includeInactive</c> (for those who configure templates) also the ones taken out of use.</summary>
    [HttpGet("api/clinical/templates")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> ListTemplates([FromQuery] bool includeInactive, CancellationToken ct) =>
        Run(async () =>
        {
            var configurator = Enum.TryParse<Role>(User.FindFirstValue(ClaimTypes.Role), out var role) && PermissionMatrix.RoleHas(role, Permission.ManageClinicalTemplates);
            return Ok(await templates.ListAsync(includeInactive && configurator, ct));
        });

    [HttpGet("api/clinical/templates/{templateId:guid}")]
    [RequirePermission(Permission.ViewClinicalDocumentation)]
    public Task<IActionResult> GetTemplate(Guid templateId, CancellationToken ct) => Run(async () => Ok(await templates.GetAsync(templateId, ct)));

    [HttpPost("api/clinical/templates")]
    [RequirePermission(Permission.ManageClinicalTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateTemplate([FromBody] SaveNoteTemplateRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            var created = await templates.CreateAsync(request.Name, request.Description, request.SectionInputs, Actor, ct);
            return Created($"api/clinical/templates/{created.Id}", created);
        });

    [HttpPut("api/clinical/templates/{templateId:guid}")]
    [RequirePermission(Permission.ManageClinicalTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateTemplate(Guid templateId, [FromBody] SaveNoteTemplateRequest request, CancellationToken ct) =>
        Run(async () => Ok(await templates.UpdateAsync(templateId, request.Name, request.Description, request.SectionInputs, request.RowVersion, Actor, ct)));

    [HttpPost("api/clinical/templates/{templateId:guid}/active")]
    [RequirePermission(Permission.ManageClinicalTemplates)]
    [RequireCsrfToken]
    public Task<IActionResult> SetTemplateActive(Guid templateId, [FromBody] NoteTemplateActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(await templates.SetActiveAsync(templateId, request.Active, request.RowVersion, Actor, ct)));
}
