using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Controllers;

public record UpdatePatientRequest(
    string? FirstName, string? MiddleName, string? LastName, string? DateOfBirth, string? Sex,
    string? Phone, string? Email, string? AddressLine1, string? AddressLine2, string? City, string? State, string? PostalCode,
    string? RowVersion)
{
    public PatientFields Fields => new(FirstName, MiddleName, LastName, DateOfBirth, Sex, Phone, Email, AddressLine1, AddressLine2, City, State, PostalCode);
}
public record SetPatientActiveRequest(bool IsActive, string? RowVersion);
public record SaveRegistrationSettingsRequest(bool RequireEmail, bool RequireSex, string? RowVersion);
public record SetGuarantorRequest(Guid? GuarantorPatientId, string? RowVersion);
public record SetHouseholdRequest(Guid? AnchorPatientId, string? Relationship, string? RowVersion);

/// <summary>
/// STORY-003 / ALV-003-C01: patient registration, search, identity detail, safe edits, active state, household and guarantor.
/// Registering requires <see cref="Permission.RegisterPatients"/>; reading requires <see cref="Permission.ViewPatientRecords"/>;
/// every other change requires <see cref="Permission.EditPatients"/>. Every state-changing call needs a CSRF token and
/// (edits) the row version the caller read, so a stale edit is a 409, never a silent overwrite. Failures return a stable
/// <c>error</c> code (and per-field messages for validation) the forms map to inline prompts.
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientsController(
    PatientRegistrationService registration, PatientEditService edits, PatientRelationshipService relationships, PatientDirectory directory,
    PatientRegistrationSettingsService requirements) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string Version(Patient p) => Convert.ToBase64String(p.RowVersion);

    /// <summary>STORY-003's response shape (unchanged keys), plus the new identity-workspace fields.</summary>
    private static object Dto(Patient p) => new
    {
        p.Id, p.FirstName, p.MiddleName, p.LastName, dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"), p.Sex,
        p.Phone, p.Email, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode, p.CreatedAtUtc,
        p.IsActive, rowVersion = Version(p),
    };

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PatientException ex)
        {
            return StatusCode(ex.StatusCode, new
            {
                error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors,
                existingPatientId = ex.ExistingPatientId, candidates = ex.Candidates,
            });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    // ---------- Registration ----------

    [HttpPost]
    [RequirePermission(Permission.RegisterPatients)]
    [RequireCsrfToken]
    public Task<IActionResult> Register([FromBody] RegisterPatientRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct) =>
        Run(async () =>
        {
            var result = await registration.RegisterAsync(request, idempotencyKey, Actor, ct);
            return result.Created
                ? Created($"/api/patients/{result.Patient.Id}", Dto(result.Patient))
                : Ok(Dto(result.Patient)); // a replay of an already-completed registration
        });

    /// <summary>Check-before-create: the likely and exact duplicates for what has been typed so far. Registers nothing.</summary>
    [HttpPost("duplicate-check")]
    [RequirePermission(Permission.RegisterPatients)]
    [RequireCsrfToken]
    public Task<IActionResult> DuplicateCheck([FromBody] RegisterPatientRequest request, CancellationToken ct) =>
        Run(async () => Ok(new { candidates = await registration.CheckDuplicatesAsync(request.Fields, ct) }));

    // ---------- Practice registration requirements ----------

    /// <summary>Which optional fields this practice requires. Readable by anyone who can see patients so the forms mark the right fields.</summary>
    [HttpGet("registration-settings")]
    [RequirePermission(Permission.ViewPatientRecords)]
    public Task<IActionResult> GetRegistrationSettings(CancellationToken ct) => Run(async () => Ok(await requirements.GetAsync(ct)));

    [HttpPut("registration-settings")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SaveRegistrationSettings([FromBody] SaveRegistrationSettingsRequest request, CancellationToken ct) =>
        Run(async () => Ok(await requirements.UpdateAsync(request.RequireEmail, request.RequireSex, request.RowVersion, Actor, ct)));

    // ---------- Reading ----------

    [HttpGet]
    [RequirePermission(Permission.ViewPatientRecords)]
    public Task<IActionResult> Search([FromQuery] string? q, [FromQuery] bool includeInactive, [FromQuery] int take = 25, CancellationToken ct = default) =>
        Run(async () => Ok(await directory.SearchAsync(q, includeInactive, take, ct)));

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewPatientRecords)]
    public Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Run(async () =>
        {
            var detail = await directory.GetDetailAsync(id, ct);
            if (detail is null) return NotFound(new { error = "patient_not_found" });
            var p = detail.Patient;
            return Ok(new
            {
                p.Id, p.FirstName, p.MiddleName, p.LastName, dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"), age = detail.Age, p.Sex,
                p.Phone, p.Email, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode, p.CreatedAtUtc, p.UpdatedAtUtc,
                p.IsActive, rowVersion = Version(p),
                guarantor = detail.Guarantor, guaranteeFor = detail.GuaranteeFor,
                household = detail.HouseholdId is null ? null : new { id = detail.HouseholdId, relationship = p.HouseholdRelationship, members = detail.HouseholdMembers },
            });
        });

    [HttpGet("{id:guid}/history")]
    [RequirePermission(Permission.ViewPatientRecords)]
    public Task<IActionResult> History(Guid id, CancellationToken ct) =>
        Run(async () => await directory.GetHistoryAsync(id, ct) is { } rows ? Ok(rows) : NotFound(new { error = "patient_not_found" }));

    // ---------- Changes ----------

    [HttpPut("{id:guid}")]
    [RequirePermission(Permission.EditPatients)]
    [RequireCsrfToken]
    public Task<IActionResult> Update(Guid id, [FromBody] UpdatePatientRequest request, CancellationToken ct) =>
        Run(async () => Ok(Dto(await edits.UpdateAsync(id, request.Fields, request.RowVersion, Actor, ct))));

    [HttpPut("{id:guid}/active")]
    [RequirePermission(Permission.EditPatients)]
    [RequireCsrfToken]
    public Task<IActionResult> SetActive(Guid id, [FromBody] SetPatientActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(Dto(await edits.SetActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct))));

    [HttpPut("{id:guid}/guarantor")]
    [RequirePermission(Permission.EditPatients)]
    [RequireCsrfToken]
    public Task<IActionResult> SetGuarantor(Guid id, [FromBody] SetGuarantorRequest request, CancellationToken ct) =>
        Run(async () => Ok(Dto(await relationships.SetGuarantorAsync(id, request.GuarantorPatientId, request.RowVersion, Actor, ct))));

    [HttpPut("{id:guid}/household")]
    [RequirePermission(Permission.EditPatients)]
    [RequireCsrfToken]
    public Task<IActionResult> SetHousehold(Guid id, [FromBody] SetHouseholdRequest request, CancellationToken ct) =>
        Run(async () => Ok(Dto(await relationships.SetHouseholdAsync(id, request.AnchorPatientId, request.Relationship, request.RowVersion, Actor, ct))));
}
