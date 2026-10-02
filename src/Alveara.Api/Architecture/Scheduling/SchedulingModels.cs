namespace Alveara.Api.Architecture.Scheduling;

/// <summary>What the scheduler asks for. The start is practice-local wall-clock time ("2026-11-02T09:00"), as a person at the front desk thinks of it.</summary>
public record ScheduleAppointmentRequest(Guid PatientId, Guid ProviderId, Guid OperatoryId, Guid AppointmentTypeId, DateTime StartLocal, int? DurationMinutes);

public record ScheduleResult(AppointmentView Appointment, bool Created);

public record AppointmentView(
    Guid Id, Guid PatientId, string PatientName, Guid ProviderId, string ProviderName, Guid OperatoryId, string OperatoryName,
    Guid AppointmentTypeId, string AppointmentTypeName, DateTimeOffset StartUtc, DateTimeOffset EndUtc, string StartLocal, string EndLocal,
    int DurationMinutes, string Status, DateTimeOffset CreatedAtUtc);

public static class SchedulingAuditEvents
{
    public const string Scheduled = "AppointmentScheduled";
    /// <summary>A scheduling attempt the system refused because of a conflict or unavailability (invalid input is not an "attempt" and is not logged).</summary>
    public const string Rejected = "AppointmentRejected";
}

/// <summary>A scheduling request was refused. <see cref="Code"/> is stable for the UI.</summary>
public class SchedulingException(string code, string message, int statusCode, Guid? conflictingAppointmentId = null, string? reason = null) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
    /// <summary>For a provider/operatory conflict: the id of the appointment already holding the time (no patient details).</summary>
    public Guid? ConflictingAppointmentId { get; } = conflictingAppointmentId;
    /// <summary>For provider_unavailable: why (outside_working_hours, blocked_time, provider_inactive, crosses_dst_transition).</summary>
    public string? Reason { get; } = reason;

    /// <summary>True for a refusal that is a real scheduling decision (conflict/unavailable), as opposed to a malformed request.</summary>
    public bool IsSchedulingDecision => Code is "provider_double_booked" or "operatory_conflict" or "provider_unavailable";
}
