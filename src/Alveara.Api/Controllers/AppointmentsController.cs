using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Controllers;

/// <summary>The start is practice-local wall-clock time as "yyyy-MM-ddTHH:mm" (seconds optional); durationMinutes is optional (the type's default is used).</summary>
public record ScheduleAppointmentBody(Guid? PatientId, Guid? ProviderId, Guid? OperatoryId, Guid? AppointmentTypeId, string? StartLocal, int? DurationMinutes, string? Notes = null);
public record RescheduleBody(Guid? ProviderId, Guid? OperatoryId, string? StartLocal, int? DurationMinutes, string? RowVersion);
public record CancelBody(string? Reason, string? RowVersion);
public record RowVersionBody(string? RowVersion);
public record NotesBody(string? Notes, string? RowVersion);

/// <summary>
/// STORY-004: scheduling. Booking needs <see cref="Permission.ManageAppointments"/> (front desk, office manager, admin); reading the schedule needs
/// <see cref="Permission.ViewSchedule"/>. Booking carries a CSRF token and an Idempotency-Key. A refusal returns a stable <c>error</c> code the UI
/// shows as a message: <c>provider_double_booked</c> / <c>operatory_conflict</c> (409, with the id of the appointment already holding the time),
/// <c>patient_double_booked</c> (409), <c>provider_unavailable</c> (409, with a <c>reason</c>), <c>invalid_duration</c> / <c>invalid_local_time</c> /
/// <c>start_in_past</c> (400), and so on.
///
/// ALV-004-C01 adds reschedule, cancel (with a reason), no-show and notes. Each carries the <c>rowVersion</c> the caller read; a stale one is the shared
/// 409 <c>concurrency_conflict</c>. All of them need <see cref="Permission.ManageAppointments"/> and a CSRF token.
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize]
public class AppointmentsController(AppointmentScheduler scheduler, AppointmentManager manager, IPracticeClock clock) : ControllerBase
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
                new ScheduleAppointmentRequest(body.PatientId.Value, body.ProviderId.Value, body.OperatoryId.Value, body.AppointmentTypeId.Value, startLocal, body.DurationMinutes, body.Notes),
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
    public async Task<IActionResult> List([FromQuery] string? from, [FromQuery] string? to, [FromQuery] Guid? providerId, [FromQuery] Guid? operatoryId, [FromQuery] bool includeAll = false, CancellationToken ct = default)
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
            // includeAll: the calendar also wants cancelled and no-show appointments (shown distinctly); the plain list is what is still booked
            return Ok(includeAll ? await scheduler.CalendarAsync(fromUtc, toUtc, providerId, operatoryId, ct) : await scheduler.ListAsync(fromUtc, toUtc, providerId, operatoryId, ct));
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

    // ---------- ALV-004-C01: what happens to a booked appointment ----------

    [HttpGet("{id:guid}/history")]
    [RequirePermission(Permission.ViewSchedule)]
    public async Task<IActionResult> History(Guid id, CancellationToken ct) =>
        await scheduler.HistoryAsync(id, ct) is { } events ? Ok(events) : NotFound(new { error = "appointment_not_found", message = "That appointment was not found." });

    [HttpPut("{id:guid}/reschedule")]
    [RequirePermission(Permission.ManageAppointments)]
    [RequireCsrfToken]
    public Task<IActionResult> Reschedule(Guid id, [FromBody] RescheduleBody body, CancellationToken ct)
    {
        if (body.ProviderId is null || body.OperatoryId is null)
            return Task.FromResult<IActionResult>(BadRequest(new { error = "validation_failed", message = "The provider and operatory are required." }));
        if (!DateTime.TryParseExact(body.StartLocal, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startLocal))
            return Task.FromResult<IActionResult>(BadRequest(new { error = "invalid_local_time", message = "The start must be a practice-local date and time like 2030-01-14T09:00." }));
        return Run(async () => Ok(await manager.RescheduleAsync(id, new RescheduleRequest(body.ProviderId.Value, body.OperatoryId.Value, startLocal, body.DurationMinutes, body.RowVersion), Actor, ct)));
    }

    [HttpPost("{id:guid}/cancel")]
    [RequirePermission(Permission.ManageAppointments)]
    [RequireCsrfToken]
    public Task<IActionResult> Cancel(Guid id, [FromBody] CancelBody body, CancellationToken ct) =>
        Run(async () => Ok(await manager.CancelAsync(id, body.Reason, body.RowVersion, Actor, ct)));

    [HttpPost("{id:guid}/no-show")]
    [RequirePermission(Permission.ManageAppointments)]
    [RequireCsrfToken]
    public Task<IActionResult> NoShow(Guid id, [FromBody] RowVersionBody body, CancellationToken ct) =>
        Run(async () => Ok(await manager.MarkNoShowAsync(id, body.RowVersion, Actor, ct)));

    [HttpPut("{id:guid}/notes")]
    [RequirePermission(Permission.ManageAppointments)]
    [RequireCsrfToken]
    public Task<IActionResult> Notes(Guid id, [FromBody] NotesBody body, CancellationToken ct) =>
        Run(async () => Ok(await manager.UpdateNotesAsync(id, body.Notes, body.RowVersion, Actor, ct)));

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (SchedulingException ex)
        {
            return Refusal(ex);
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }
}
