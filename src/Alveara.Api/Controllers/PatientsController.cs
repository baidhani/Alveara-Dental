using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Controllers;

/// <summary>
/// STORY-003: patient registration. Registering requires <see cref="Permission.RegisterPatients"/> and a CSRF
/// token; the caller supplies an <c>Idempotency-Key</c> header so a retried request is safe. Failures return a
/// stable <c>error</c> code (and per-field messages for validation) the form maps to inline prompts.
/// </summary>
[ApiController]
[Route("api/patients")]
[Authorize]
public class PatientsController(PatientRegistrationService registration) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static object Dto(Patient p) => new
    {
        p.Id, p.FirstName, p.MiddleName, p.LastName, dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"), p.Sex,
        p.Phone, p.Email, p.AddressLine1, p.AddressLine2, p.City, p.State, p.PostalCode, p.CreatedAtUtc,
    };

    [HttpPost]
    [RequirePermission(Permission.RegisterPatients)]
    [RequireCsrfToken]
    public async Task<IActionResult> Register([FromBody] RegisterPatientRequest request, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            var result = await registration.RegisterAsync(request, idempotencyKey, Actor, ct);
            return result.Created
                ? Created($"/api/patients/{result.Patient.Id}", Dto(result.Patient))
                : Ok(Dto(result.Patient)); // a replay of an already-completed registration
        }
        catch (PatientRegistrationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, fieldErrors = ex.FieldErrors, existingPatientId = ex.ExistingPatientId });
        }
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.RegisterPatients)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await registration.FindAsync(id, ct) is { } patient ? Ok(Dto(patient)) : NotFound(new { error = "patient_not_found" });
}
