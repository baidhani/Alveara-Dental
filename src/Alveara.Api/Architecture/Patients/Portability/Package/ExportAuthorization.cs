using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Portability.Package;

/// <summary>Asks again, in the middle of a long export, whether the person who started it may still do it.</summary>
public interface IExportAuthorization
{
    /// <summary>Returns when <paramref name="actor"/> still holds <paramref name="permission"/>; otherwise throws a <see cref="PatientException"/> (403 <c>permission_revoked</c>, or 503 <c>permission_policy_unavailable</c> when the policy cannot be read).</summary>
    Task EnsureStillAllowedAsync(Guid actor, Permission permission, CancellationToken ct);
}

/// <summary>
/// ALV-018-C01: the real recheck. An all-patients export can take minutes, and the permission it started with may be taken away while it runs (an administrator changes the role's permissions, disables the account or
/// changes its role). The request's own permission service remembers its first answer for the whole request, so asking it again would always say yes: every check here opens a FRESH scope and reads the account and the
/// permission policy again. A missing or disabled account, a role that no longer holds the permission, and a policy that is degraded or in repair all stop the export; an unreadable policy stops it with a different
/// message (503) so the cause is not mistaken for a revocation. The check carries no patient detail.
/// </summary>
public sealed class ExportAuthorization(IServiceScopeFactory scopes) : IExportAuthorization
{
    public async Task EnsureStillAllowedAsync(Guid actor, Permission permission, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
        var permissions = scope.ServiceProvider.GetRequiredService<IEffectivePermissions>();
        var account = await db.UserAccounts.AsNoTracking().Where(u => u.Id == actor).Select(u => new { u.Role, u.IsDisabled }).SingleOrDefaultAsync(ct);
        if (account is null || account.IsDisabled) throw Revoked();
        var outcome = await permissions.CheckAsync(account.Role, permission, ct);
        if (outcome == PermissionOutcome.Unavailable)
            throw new PatientException("permission_policy_unavailable", "The export was stopped because the permission policy cannot be read just now. Try again in a moment.", 503);
        if (outcome != PermissionOutcome.Allowed) throw Revoked();
    }

    private static PatientException Revoked() =>
        new("permission_revoked", "The export was stopped because the permission to run it is no longer held. Nothing was delivered.", 403);
}
