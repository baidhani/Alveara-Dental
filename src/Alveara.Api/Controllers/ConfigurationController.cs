using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Controllers;

public record SavePracticeRequest(string? Name, string? Phone, string? AddressLine, string? RowVersion);
public record SaveLocationRequest(string? Name, string? RowVersion);
public record SaveOperatoryRequest(string? Name, string? RowVersion);
public record SaveAppointmentTypeRequest(string? Name, int DefaultDurationMinutes, string? RowVersion);
public record SaveStaffRequest(string? DisplayName, string? JobTitle, Guid? UserAccountId, string? RowVersion);
public record SaveProviderRequest(Guid? StaffProfileId, string? Specialty, string? RowVersion);
public record SetActiveRequest(bool IsActive, string? RowVersion);
public record ReplaceAvailabilityRequest(List<AvailabilityWindow>? Windows, int? Revision);
public record AddBlockedTimeRequest(DateTime StartLocal, DateTime EndLocal, string? Reason);

/// <summary>
/// ALV-N003: the configuration API. Reads and writes of practice configuration require
/// <see cref="Permission.ManagePracticeConfiguration"/>; the scheduling read model requires only
/// <see cref="Permission.ViewSchedule"/>. There is deliberately no DELETE route for staff,
/// providers, operatories, locations, or appointment types - they are inactivated, so anything that
/// referenced them keeps resolving. Every failure returns a stable <c>error</c> code the UI maps to
/// an inline message; a stale edit returns the shared 409 concurrency-conflict shape.
/// </summary>
[ApiController]
[Route("api/config")]
[Authorize]
public class ConfigurationController(
    PracticeConfigurationService practice,
    StaffProviderService staffProviders,
    SchedulingConfiguration scheduling) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (ConfigurationException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Code, message = ex.Message });
        }
        catch (ConcurrencyConflictException ex)
        {
            return Conflict(ex.ToProblem());
        }
    }

    private static string Version(byte[] rowVersion) => Convert.ToBase64String(rowVersion);

    // ---------- Practice + location ----------

    [HttpGet("practice")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetPractice(CancellationToken ct) => Run(async () => Ok(await practice.GetPracticeAsync(ct)));

    [HttpPut("practice")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SavePractice([FromBody] SavePracticeRequest request, CancellationToken ct) =>
        Run(async () => Ok(await practice.SavePracticeAsync(request.Name, request.Phone, request.AddressLine, request.RowVersion, Actor, ct)));

    private static object LocationDto(PracticeLocation l) => new { l.Id, l.Name, l.IsActive, rowVersion = Version(l.RowVersion) };

    [HttpGet("locations")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListLocations([FromQuery] bool includeInactive, CancellationToken ct) =>
        Run(async () => Ok((await practice.ListLocationsAsync(includeInactive, ct)).Select(LocationDto)));

    [HttpGet("locations/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetLocation(Guid id, CancellationToken ct) => Run(async () => Ok(LocationDto(await practice.GetLocationAsync(id, ct))));

    [HttpPost("locations")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateLocation([FromBody] SaveLocationRequest request, CancellationToken ct) =>
        Run(async () => StatusCode(StatusCodes.Status201Created, LocationDto(await practice.CreateLocationAsync(request.Name, Actor, ct))));

    [HttpPut("locations/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateLocation(Guid id, [FromBody] SaveLocationRequest request, CancellationToken ct) =>
        Run(async () => Ok(LocationDto(await practice.UpdateLocationAsync(id, request.Name, request.RowVersion, Actor, ct))));

    [HttpPut("locations/{id:guid}/active")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SetLocationActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(LocationDto(await practice.SetLocationActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct))));

    // ---------- Operatories ----------

    private static object OperatoryDto(Operatory o) => new { o.Id, o.LocationId, o.Name, o.IsActive, rowVersion = Version(o.RowVersion) };

    [HttpGet("operatories")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListOperatories([FromQuery] bool includeInactive, CancellationToken ct) =>
        Run(async () => Ok((await practice.ListOperatoriesAsync(includeInactive, ct)).Select(OperatoryDto)));

    [HttpGet("operatories/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetOperatory(Guid id, CancellationToken ct) => Run(async () => Ok(OperatoryDto(await practice.GetOperatoryAsync(id, ct))));

    [HttpPost("operatories")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateOperatory([FromBody] SaveOperatoryRequest request, CancellationToken ct) =>
        Run(async () => StatusCode(StatusCodes.Status201Created, OperatoryDto(await practice.CreateOperatoryAsync(request.Name, Actor, ct))));

    [HttpPut("operatories/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateOperatory(Guid id, [FromBody] SaveOperatoryRequest request, CancellationToken ct) =>
        Run(async () => Ok(OperatoryDto(await practice.UpdateOperatoryAsync(id, request.Name, request.RowVersion, Actor, ct))));

    [HttpPut("operatories/{id:guid}/active")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SetOperatoryActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(OperatoryDto(await practice.SetOperatoryActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct))));

    // ---------- Appointment types ----------

    private static object TypeDto(AppointmentType t) => new { t.Id, t.Name, t.DefaultDurationMinutes, t.IsActive, rowVersion = Version(t.RowVersion) };

    [HttpGet("appointment-types")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListAppointmentTypes([FromQuery] bool includeInactive, CancellationToken ct) =>
        Run(async () => Ok((await practice.ListAppointmentTypesAsync(includeInactive, ct)).Select(TypeDto)));

    [HttpGet("appointment-types/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetAppointmentType(Guid id, CancellationToken ct) => Run(async () => Ok(TypeDto(await practice.GetAppointmentTypeAsync(id, ct))));

    [HttpPost("appointment-types")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateAppointmentType([FromBody] SaveAppointmentTypeRequest request, CancellationToken ct) =>
        Run(async () => StatusCode(StatusCodes.Status201Created, TypeDto(await practice.CreateAppointmentTypeAsync(request.Name, request.DefaultDurationMinutes, Actor, ct))));

    [HttpPut("appointment-types/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateAppointmentType(Guid id, [FromBody] SaveAppointmentTypeRequest request, CancellationToken ct) =>
        Run(async () => Ok(TypeDto(await practice.UpdateAppointmentTypeAsync(id, request.Name, request.DefaultDurationMinutes, request.RowVersion, Actor, ct))));

    [HttpPut("appointment-types/{id:guid}/active")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SetAppointmentTypeActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(TypeDto(await practice.SetAppointmentTypeActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct))));

    // ---------- Staff ----------

    private static object StaffDto(StaffProfile s) => new { s.Id, s.DisplayName, s.JobTitle, s.UserAccountId, s.IsActive, rowVersion = Version(s.RowVersion) };

    [HttpGet("staff")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListStaff([FromQuery] bool includeInactive, CancellationToken ct) =>
        Run(async () => Ok((await staffProviders.ListStaffAsync(includeInactive, ct)).Select(StaffDto)));

    [HttpGet("linkable-accounts")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListLinkableAccounts(CancellationToken ct) =>
        Run(async () => Ok(await staffProviders.ListLinkableAccountsAsync(ct)));

    [HttpGet("staff/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetStaff(Guid id, CancellationToken ct) => Run(async () => Ok(StaffDto(await staffProviders.GetStaffAsync(id, ct))));

    [HttpPost("staff")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateStaff([FromBody] SaveStaffRequest request, CancellationToken ct) =>
        Run(async () => StatusCode(StatusCodes.Status201Created, StaffDto(await staffProviders.CreateStaffAsync(request.DisplayName, request.JobTitle, request.UserAccountId, Actor, ct))));

    [HttpPut("staff/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateStaff(Guid id, [FromBody] SaveStaffRequest request, CancellationToken ct) =>
        Run(async () => Ok(StaffDto(await staffProviders.UpdateStaffAsync(id, request.DisplayName, request.JobTitle, request.UserAccountId, request.RowVersion, Actor, ct))));

    [HttpPut("staff/{id:guid}/active")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SetStaffActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(StaffDto(await staffProviders.SetStaffActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct))));

    // ---------- Providers ----------

    private static object ProviderDto(ProviderProfile p) => new
    {
        p.Id, p.StaffProfileId, displayName = p.StaffProfile?.DisplayName, p.Specialty, p.IsActive, rowVersion = Version(p.RowVersion),
    };

    [HttpGet("providers")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListProviders([FromQuery] bool includeInactive, CancellationToken ct) =>
        Run(async () => Ok((await staffProviders.ListProvidersAsync(includeInactive, ct)).Select(ProviderDto)));

    [HttpGet("providers/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetProvider(Guid id, CancellationToken ct) => Run(async () => Ok(ProviderDto(await staffProviders.GetProviderAsync(id, ct))));

    [HttpPost("providers")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> CreateProvider([FromBody] SaveProviderRequest request, CancellationToken ct) =>
        Run(async () =>
        {
            if (request.StaffProfileId is null)
                throw new ConfigurationException("staff_not_found", "Select the staff member this provider profile belongs to.");
            return StatusCode(StatusCodes.Status201Created, ProviderDto(await staffProviders.CreateProviderAsync(request.StaffProfileId.Value, request.Specialty, Actor, ct)));
        });

    [HttpPut("providers/{id:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> UpdateProvider(Guid id, [FromBody] SaveProviderRequest request, CancellationToken ct) =>
        Run(async () => Ok(ProviderDto(await staffProviders.UpdateProviderAsync(id, request.Specialty, request.RowVersion, Actor, ct))));

    [HttpPut("providers/{id:guid}/active")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> SetProviderActive(Guid id, [FromBody] SetActiveRequest request, CancellationToken ct) =>
        Run(async () => Ok(ProviderDto(await staffProviders.SetProviderActiveAsync(id, request.IsActive, request.RowVersion, Actor, ct))));

    // ---------- Availability + blocked time ----------

    private static object WindowDto(ProviderWeeklyAvailability a) => new { dayOfWeek = (int)a.DayOfWeek, startLocal = a.StartLocal.ToString("HH:mm"), endLocal = a.EndLocal.ToString("HH:mm") };

    private static object ScheduleDto(ProviderAvailabilitySchedule s) => new { revision = s.Revision, windows = s.Windows.Select(WindowDto) };

    [HttpGet("providers/{id:guid}/availability")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> GetAvailability(Guid id, CancellationToken ct) =>
        Run(async () => Ok(ScheduleDto(await staffProviders.GetAvailabilityScheduleAsync(id, ct))));

    [HttpPut("providers/{id:guid}/availability")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> ReplaceAvailability(Guid id, [FromBody] ReplaceAvailabilityRequest request, CancellationToken ct) =>
        Run(async () => Ok(ScheduleDto(await staffProviders.ReplaceAvailabilityAsync(id, request.Windows ?? [], request.Revision, Actor, ct))));

    private object BlockedDto(ProviderBlockedTime b, IPracticeClock clock) => new
    {
        b.Id, b.StartUtc, b.EndUtc, b.Reason,
        startLocal = clock.ToPracticeLocal(b.StartUtc).ToString("yyyy-MM-dd'T'HH:mm"),
        endLocal = clock.ToPracticeLocal(b.EndUtc).ToString("yyyy-MM-dd'T'HH:mm"),
    };

    [HttpGet("providers/{id:guid}/blocked-time")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    public Task<IActionResult> ListBlockedTime(Guid id, [FromServices] IPracticeClock clock, CancellationToken ct) =>
        Run(async () => Ok((await staffProviders.ListBlockedTimeAsync(id, ct)).Select(b => BlockedDto(b, clock))));

    [HttpPost("providers/{id:guid}/blocked-time")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> AddBlockedTime(Guid id, [FromBody] AddBlockedTimeRequest request, [FromServices] IPracticeClock clock, CancellationToken ct) =>
        Run(async () => StatusCode(StatusCodes.Status201Created, BlockedDto(await staffProviders.AddBlockedTimeAsync(id, request.StartLocal, request.EndLocal, request.Reason, Actor, ct), clock)));

    [HttpDelete("providers/{id:guid}/blocked-time/{blockId:guid}")]
    [RequirePermission(Permission.ManagePracticeConfiguration)]
    [RequireCsrfToken]
    public Task<IActionResult> RemoveBlockedTime(Guid id, Guid blockId, CancellationToken ct) =>
        Run(async () =>
        {
            await staffProviders.RemoveBlockedTimeAsync(id, blockId, Actor, ct);
            return NoContent();
        });

    // ---------- Scheduling consumption seam ----------

    [HttpGet("scheduling")]
    [RequirePermission(Permission.ViewSchedule)]
    public Task<IActionResult> GetSchedulingSnapshot(CancellationToken ct) => Run(async () => Ok(await scheduling.GetSnapshotAsync(ct)));

    [HttpGet("scheduling/availability-check")]
    [RequirePermission(Permission.ViewSchedule)]
    public Task<IActionResult> CheckAvailability([FromQuery] Guid providerId, [FromQuery] DateTimeOffset startUtc, [FromQuery] int durationMinutes, CancellationToken ct) =>
        Run(async () => Ok(await scheduling.CheckProviderAvailabilityAsync(providerId, startUtc, durationMinutes, ct)));
}
