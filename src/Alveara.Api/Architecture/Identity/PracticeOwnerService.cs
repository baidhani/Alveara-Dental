using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

public sealed record PracticeOwnerView(Guid StaffProfileId, string DisplayName, DateTimeOffset DesignatedAtUtc, string Method);

/// <summary>
/// ALV-N030 (brief sections 4 and 5, D-3, D-7): the Practice Owner is a human staff member - an active staff profile with a linked, enabled
/// <see cref="AccountKind.Staff"/> account. It carries no extra permission in ACCT. <see cref="DesignateInitialAsync"/> succeeds only while no designation row has
/// EVER existed (a second call gets 409 <c>owner_already_designated</c>); the filtered unique index and the protection trigger (51311) are the database
/// backstops. No route ends or changes a designation. Method: <c>InitialByDeploymentAdministrator</c> when the actor is the Deployment Administrator,
/// otherwise <c>UpgradeTransition</c>.
/// </summary>
public class PracticeOwnerService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<PracticeOwnerView?> CurrentAsync(CancellationToken ct) =>
        await db.PracticeOwnerDesignations.AsNoTracking().Where(d => d.EndedAtUtc == null)
            .Select(d => new PracticeOwnerView(d.StaffProfileId, d.StaffProfile!.DisplayName, d.DesignatedAtUtc, d.Method.ToString()))
            .SingleOrDefaultAsync(ct);

    public async Task<PracticeOwnerView> DesignateInitialAsync(Guid staffProfileId, bool confirm, Guid actor, CancellationToken ct)
    {
        if (!confirm) throw new ConfigurationException("confirmation_required", "Confirm that this person is the Practice Owner.");
        if (await db.PracticeOwnerDesignations.AnyAsync(ct)) throw AlreadyDesignated();

        var target = await db.StaffProfiles.AsNoTracking().Where(s => s.Id == staffProfileId)
            .Select(s => new { s.Id, s.DisplayName, s.IsActive, Account = s.UserAccount == null ? null : new { s.UserAccount.IsDisabled, s.UserAccount.AccountKind } })
            .SingleOrDefaultAsync(ct) ?? throw PracticeConfigurationService.NotFound("Staff profile");
        if (!target.IsActive || target.Account is null || target.Account.IsDisabled || target.Account.AccountKind != AccountKind.Staff)
            throw new ConfigurationException("owner_target_invalid", "The Practice Owner must be an active staff member with an enabled login.", 409);

        var actorKind = await db.UserAccounts.AsNoTracking().Where(u => u.Id == actor).Select(u => (AccountKind?)u.AccountKind).SingleOrDefaultAsync(ct);
        var row = new PracticeOwnerDesignation
        {
            Id = Guid.NewGuid(), StaffProfileId = target.Id, DesignatedAtUtc = clock.UtcNow, DesignatedByAccountId = actor,
            Method = actorKind == AccountKind.DeploymentAdministrator ? PracticeOwnerDesignationMethod.InitialByDeploymentAdministrator : PracticeOwnerDesignationMethod.UpgradeTransition,
        };
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        try
        {
            // Serializable: the "never designated" read and the insert cannot interleave with another designation.
            if (await db.PracticeOwnerDesignations.AnyAsync(ct)) throw AlreadyDesignated();
            db.PracticeOwnerDesignations.Add(row);
            AuditService.Record(db, AuditEventTypes.PracticeOwnerDesignated, nameof(StaffProfile), target.Id, actor,
                $"{target.DisplayName} was designated the Practice Owner ({row.Method}).");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 or 1205 })
        {
            await tx.RollbackAsync(ct);
            throw AlreadyDesignated();
        }
        return new PracticeOwnerView(target.Id, target.DisplayName, row.DesignatedAtUtc, row.Method.ToString());
    }

    private static ConfigurationException AlreadyDesignated() =>
        new("owner_already_designated", "A Practice Owner has already been designated.", 409);
}
