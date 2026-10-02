using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Controllers;

/// <summary>The start is practice-local wall-clock time as "yyyy-MM-ddTHH:mm" (seconds optional); durationMinutes is optional (the type's default is used).</summary>
public record ScheduleAppointmentBody(Guid? PatientId, Guid? ProviderId, Guid? OperatoryId, Guid? AppointmentTypeId, string? StartLocal, int? DurationMinutes);

/// <summary>
/// STORY-004: scheduling. Booking needs <see cref="Permission.ManageAppointments"/> (front desk, office manager, admin); reading the schedule needs
/// <see cref="Permission.ViewSchedule"/>. Booking carries a CSRF token and an Idempotency-Key. A refusal returns a stable <c>error</c> code the UI
/// shows as a message: <c>provider_double_booked</c> / <c>operatory_conflict</c> (409, with the id of the appointment already holding the time),
/// <c>provider_unavailable</c> (409, with a <c>reason</c>), <c>invalid_duration</c> / <c>invalid_local_time</c> / <c>start_in_past</c> (400), and so on.
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize]
public class AppointmentsController(AppointmentScheduler scheduler, IPracticeClock clock) : ControllerBase
{
    private const int MaxRangeDays = 31;
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private IActionResult Refusal(SchedulingException ex) =>
        StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message, conflictingAppointmentId = ex.ConflictingAppointmentId, reason = ex.Reason });

    private static readonly string[] LocalFormats = ["yyyy-MM-ddTHH:mm", "yyyy-MM-ddTHH:mm:ss"];

    [HttpPost]
    [RequirePermission(Permission.ManageAppointments)]
    [RequireCsrfToken]
    public async Task<IActionResult> Schedule([FromBody] ScheduleAppointmentBody body, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (body.PatientId is null || body.ProviderId is null || body.OperatoryId is null || body.AppointmentTypeId is null)
            return BadRequest(new { error = "validation_failed", message = "Patient, provider, operatory and appointment type are all required." });
        if (!DateTime.TryParseExact(body.StartLocal, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startLocal))
            return BadRequest(new { error = "invalid_local_time", message = "The start must be a practice-local date and time like 2030-01-14T09:00." });
        try
        {
            var result = await scheduler.ScheduleAsync(
                new ScheduleAppointmentRequest(body.PatientId.Value, body.ProviderId.Value, body.OperatoryId.Value, body.AppointmentTypeId.Value, startLocal, body.DurationMinutes),
                idempotencyKey, Actor, ct);
            return result.Created ? Created($"/api/appointments/{result.Appointment.Id}", result.Appointment) : Ok(result.Appointment); // a replay of a request that already succeeded
        }
        catch (SchedulingException ex)
        {
            return Refusal(ex);
        }
    }

    /// <summary>Appointments on the practice-local days from <c>from</c> up to and including <c>to</c> (yyyy-MM-dd), at most 31 days; optionally one provider or operatory.</summary>
    [HttpGet]
    [RequirePermission(Permission.ViewSchedule)]
    public async Task<IActionResult> List([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid? providerId, [FromQuery] Guid? operatoryId, CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDay)
            || !DateOnly.TryParseExact(to ?? from, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDay))
            return BadRequest(new { error = "invalid_range", message = "Give from (and optionally to) as yyyy-MM-dd." });
        if (toDay < fromDay || toDay.DayNumber - fromDay.DayNumber >= MaxRangeDays)
            return BadRequest(new { error = "invalid_range", message = $"The range must be 1 to {MaxRangeDays} days, ending on or after it starts." });
        try
        {
            var fromUtc = clock.FromPracticeLocal(fromDay.ToDateTime(TimeOnly.MinValue));
            var toUtc = clock.FromPracticeLocal(toDay.AddDays(1).ToDateTime(TimeOnly.MinValue));
            return Ok(await scheduler.ListAsync(fromUtc, toUtc, providerId, operatoryId, ct));
        }
        catch (LocalTimeConversionException ex)
        {
            return BadRequest(new { error = "invalid_range", message = ex.Message });
        }
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permission.ViewSchedule)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        await scheduler.GetAsync(id, ct) is { } appointment ? Ok(appointment) : NotFound(new { error = "appointment_not_found", message = "That appointment was not found." });
}
