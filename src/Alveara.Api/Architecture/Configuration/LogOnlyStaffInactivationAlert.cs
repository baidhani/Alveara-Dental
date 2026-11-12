namespace Alveara.Api.Architecture.Configuration;

/// <summary>
/// ALV-N020: the alert used until the delivery channel is decided. The approved channel is an Urgent in-app notification to enabled Admin and OfficeManager accounts,
/// but every notification kind is fixed by the database check <c>CK_Notifications_Kind</c>, so a new kind needs a migration that this story is not authorized to make
/// (escalated in the ALV-N020 handoff). Until then the failure is an error-level structured log line here, and the durable
/// <see cref="ConfigurationAuditEvents.StaffInactivationFailed"/> audit entry is the evidence.
/// </summary>
public sealed class LogOnlyStaffInactivationAlert(ILogger<LogOnlyStaffInactivationAlert> log) : IStaffInactivationAlert
{
    public Task SendAsync(StaffInactivationFailure failure, CancellationToken ct)
    {
        log.LogError("staff_inactivation_failed staff={StaffProfileId} linked_account={AccountId} actor={ActorId} error_class={ErrorClass} correlation={CorrelationId}",
            failure.StaffProfileId, failure.LinkedAccountId, failure.ActorId, failure.ErrorClass, failure.CorrelationId);
        return Task.CompletedTask;
    }
}
