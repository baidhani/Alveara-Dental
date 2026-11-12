using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Configuration;

/// <summary>Told when a staff inactivation could not disable the linked login (best effort; never the only evidence: the failure audit entry is).</summary>
public interface IStaffInactivationAlert
{
    Task SendAsync(StaffInactivationFailure failure, CancellationToken ct);
}

/// <summary>What the alert and the failure record carry: ids, the error class and the correlation id; never a message text from the exception.</summary>
public record StaffInactivationFailure(Guid StaffProfileId, string StaffDisplayName, Guid? LinkedAccountId, Guid ActorId, string ErrorClass, string? CorrelationId);

/// <summary>
/// ALV-N020 (F-021, PKG-03): inactivating a staff profile also disables its linked login and ends every session of it, in ONE transaction, so a person who has left
/// can no longer sign in and no partial state can remain.
/// <list type="bullet">
/// <item>Success: the profile is inactive, the login is disabled with a rotated security stamp (existing sessions get 401), and <c>AccountDisabled</c> plus
/// <see cref="ConfigurationAuditEvents.StaffLoginDisabledOnInactivation"/> are recorded. A login that was already disabled is left as it is.</item>
/// <item>Ordinary refusals of the profile change (not found, stale version, active provider profile) roll back and return as before; they are not failures.</item>
/// <item>The login could not be disabled (last administrator, administrator guard busy, target rules, or any unexpected error): everything is rolled back, a
/// <see cref="ConfigurationAuditEvents.StaffInactivationFailed"/> entry is written afterwards in its own short transaction (it never claims the inactivation happened),
/// the alert is attempted, and the caller gets a clear refusal (or, for an unexpected error, the original exception, which the error middleware answers with 500
/// and the correlation id).</item>
/// </list>
/// Failure modes not handled here: a crash between the rollback and the failure record (the inactivation is then simply not applied, and the person can retry).
/// No retry: the operation is a single user action; a retry is the user's. Idempotent: inactivating an inactive profile changes nothing.
/// Reactivation never re-enables the login (it stays in <see cref="StaffProviderService.SetStaffActiveAsync"/>, which does not touch accounts).
/// </summary>
public class StaffInactivationService(AlveraDbContext db, StaffProviderService staff, AccountService accounts, IStaffInactivationAlert alert, ILogger<StaffInactivationService> log)
{
    public async Task<StaffProfile> InactivateAsync(Guid staffProfileId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var profile = await db.StaffProfiles.AsNoTracking().SingleOrDefaultAsync(s => s.Id == staffProfileId, ct)
            ?? throw PracticeConfigurationService.NotFound("Staff profile");
        if (!profile.IsActive) return profile;                                                          // already inactive: nothing to do

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var accountStep = false;
        try
        {
            var loginDisabled = false;
            if (profile.UserAccountId is { } accountId)
            {
                accountStep = true;
                loginDisabled = await accounts.DisableForStaffInactivationAsync(accountId, actor, profile.DisplayName, ct);
                accountStep = false;
            }
            var result = await staff.SetStaffActiveAsync(staffProfileId, false, rowVersion, actor, ct);
            if (loginDisabled)
            {
                ConfigurationWrite.Audit(db, ConfigurationAuditEvents.StaffLoginDisabledOnInactivation, nameof(StaffProfile), staffProfileId, actor,
                    "Staff profile inactivated; its linked login was disabled and its sessions ended.");
                await ConfigurationWrite.SaveAsync(db, nameof(StaffProfile), staffProfileId, ct);
            }
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (Exception ex) when (ex is ConfigurationException or ConcurrencyConflictException && !accountStep)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;                                                                                      // an ordinary refusal of the profile change
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            var failure = new StaffInactivationFailure(staffProfileId, profile.DisplayName, profile.UserAccountId, actor, ex.GetType().Name, Activity.Current?.Id);
            await RecordFailureAsync(failure);
            await TryAlertAsync(failure);
            throw Translate(ex);
        }
    }

    private static Exception Translate(Exception ex) => ex switch
    {
        LastAdminException => new ConfigurationException("staff_login_last_admin",
            "This staff member's login is the last enabled administrator. Enable another administrator first. Nothing was changed.", 409),
        AdminGuardBusyException => new ConfigurationException("staff_login_busy",
            "Another administrator change is in progress. Try again in a moment. Nothing was changed.", 409),
        AccountTargetNotAllowedException => new ConfigurationException("staff_login_not_allowed",
            "You may not disable this staff member's login, so the profile was not inactivated. Nothing was changed.", 403),
        _ => ex,
    };

    /// <summary>The durable record of the failure, in its own short transaction after the rollback. A failure here is logged, never thrown over the original.</summary>
    private async Task RecordFailureAsync(StaffInactivationFailure failure)
    {
        try
        {
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.StaffInactivationFailed, nameof(StaffProfile), failure.StaffProfileId, failure.ActorId,
                $"Staff profile was NOT inactivated: its linked login could not be disabled ({failure.ErrorClass}). Correlation {failure.CorrelationId ?? "none"}.");
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception recordError)
        {
            db.ChangeTracker.Clear();
            log.LogError("staff_inactivation_failure_record_failed staff={StaffProfileId} error_class={ErrorClass} original_error_class={OriginalErrorClass} correlation={CorrelationId}",
                failure.StaffProfileId, recordError.GetType().Name, failure.ErrorClass, failure.CorrelationId);
        }
    }

    private async Task TryAlertAsync(StaffInactivationFailure failure)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await alert.SendAsync(failure, timeout.Token);
        }
        catch (Exception alertError)
        {
            log.LogError("staff_inactivation_alert_failed staff={StaffProfileId} error_class={ErrorClass} correlation={CorrelationId}",
                failure.StaffProfileId, alertError.GetType().Name, failure.CorrelationId);
        }
    }
}
