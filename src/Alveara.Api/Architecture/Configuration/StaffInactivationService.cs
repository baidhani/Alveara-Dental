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

/// <summary>What the alert carries: ids, the error class, the correlation id and the id of the durable failure audit entry (the alert exists only after that entry
/// does); never a message text from the exception.</summary>
public record StaffInactivationFailure(Guid StaffProfileId, string StaffDisplayName, Guid? LinkedAccountId, Guid ActorId, string ErrorClass, string CorrelationId, Guid FailureRecordId);

/// <summary>
/// ALV-N020 (F-021, PKG-03): inactivating a staff profile also disables its linked login and ends every session of it, in ONE transaction, so a person who has left
/// can no longer sign in and no partial state can remain.
/// <list type="bullet">
/// <item>Success: the profile is inactive, the login is disabled with a rotated security stamp (existing sessions get 401), and <c>AccountDisabled</c> plus
/// <see cref="ConfigurationAuditEvents.StaffLoginDisabledOnInactivation"/> are recorded. A login that was already disabled is left as it is.</item>
/// <item>Ordinary refusals of the profile change (not found, stale version, active provider profile) roll back and return as before; they are not failures.</item>
/// <item>The login could not be disabled (last administrator, administrator guard busy, target rules, or any unexpected error): everything is rolled back, a
/// <see cref="ConfigurationAuditEvents.StaffInactivationFailed"/> entry is written afterwards in its own short transaction (it never claims the inactivation happened),
/// and only then is the alert attempted (record before notify). The caller gets a clear refusal (or, for an unexpected error, the original exception, which the
/// error middleware answers with 500 and the correlation id).</item>
/// <item>The failure record itself cannot be written (an infrastructure fault): no alert is created, because it would then be the only evidence. The fault is
/// logged with the correlation id, and the refusal tells the caller the failure could not be recorded and gives that id as the reference.</item>
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
            var errorClass = ex.GetType().Name;
            var correlation = Activity.Current?.Id ?? Guid.NewGuid().ToString("N");                     // always a reference, even outside a request
            var recordId = await RecordFailureAsync(staffProfileId, actor, errorClass, correlation);
            if (recordId is { } id)
                await TryAlertAsync(new StaffInactivationFailure(staffProfileId, profile.DisplayName, profile.UserAccountId, actor, errorClass, correlation, id));
            throw Translate(ex, recordId is null ? correlation : null);
        }
    }

    private static Exception Translate(Exception ex, string? unrecordedReference)
    {
        var notRecorded = unrecordedReference is null ? "" : $" The failure could not be recorded; give this reference to your administrator: {unrecordedReference}.";
        return ex switch
        {
            LastAdminException => new ConfigurationException("staff_login_last_admin",
                "This staff member's login is the last enabled administrator. Enable another administrator first. Nothing was changed." + notRecorded, 409),
            AdminGuardBusyException => new ConfigurationException("staff_login_busy",
                "Another administrator change is in progress. Try again in a moment. Nothing was changed." + notRecorded, 409),
            AccountTargetNotAllowedException => new ConfigurationException("staff_login_not_allowed",
                "You may not disable this staff member's login, so the profile was not inactivated. Nothing was changed." + notRecorded, 403),
            _ => ex,                                                                                    // 500 with the correlation id from the error middleware
        };
    }

    /// <summary>
    /// The durable record of the failure, in its own short transaction after the rollback; returns its audit entry id, or null when it could not be written. A failure
    /// here is logged with the correlation id (an infrastructure fault, distinct from the inactivation failure), never thrown over the original.
    /// </summary>
    private async Task<Guid?> RecordFailureAsync(Guid staffProfileId, Guid actor, string errorClass, string correlation)
    {
        try
        {
            ConfigurationWrite.Audit(db, ConfigurationAuditEvents.StaffInactivationFailed, nameof(StaffProfile), staffProfileId, actor,
                $"Staff profile was NOT inactivated: its linked login could not be disabled ({errorClass}). Correlation {correlation}.");
            var entry = db.ChangeTracker.Entries<AuditLogEntry>().Single(e => e.State == EntityState.Added).Entity;   // the tracker was cleared after the rollback
            await db.SaveChangesAsync(CancellationToken.None);
            return entry.Id;
        }
        catch (Exception recordError)
        {
            db.ChangeTracker.Clear();
            log.LogError("staff_inactivation_failure_not_recorded staff={StaffProfileId} error_class={ErrorClass} original_error_class={OriginalErrorClass} correlation={CorrelationId} alert=not_created",
                staffProfileId, recordError.GetType().Name, errorClass, correlation);
            return null;
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
