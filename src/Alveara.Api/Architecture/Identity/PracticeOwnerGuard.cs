using System.Data.SqlTypes;
using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Identity;

/// <summary>
/// ALV-N030-C01 (brief 18.3, E3 to E5): the operation would break the Practice Owner invariant (the owner's login must stay an enabled Administrator linked to an active
/// staff profile). Nothing was changed; the designation must be transferred first. 409 <c>owner_transfer_required</c>.
/// </summary>
public sealed class OwnerTransferRequiredException(string message) : Exception(message);

/// <summary>One account row read under an update lock (brief 18.3a).</summary>
public sealed record LockedAccount(Guid Id, int Role, bool IsDisabled, byte AccountKind)
{
    public bool IsEnabledAdministrator => Role == (int)Identity.Role.Admin && !IsDisabled;
}

/// <summary>
/// ALV-N030-C01 (brief 18.3 and 18.3a): the shared checks that keep the Practice Owner invariant (OI). Every owner-affecting operation takes, inside its own transaction:
/// the administrator guard first, then the designation rows, then account rows in ascending id order, then staff profile rows in ascending id order. The callers here
/// run after the guard; the reads lock the active designation (<c>UPDLOCK, HOLDLOCK</c>) so a concurrent transfer or designation serializes with them.
/// </summary>
public static class PracticeOwnerGuard
{
    public const string TransferRequired = "owner_transfer_required";
    public const string ActorMustBeAdministrator = "owner_designation_requires_administrator";
    public const string TargetMustBeAdministrator = "owner_must_be_administrator";

    /// <summary>The owner's wording (18.1, Q1) for disabling; the role-change and inactivation variants name their own action.</summary>
    public const string DisableMessage = "Transfer the Practice Owner designation before disabling this account.";
    public const string RoleChangeMessage = "Transfer the Practice Owner designation before changing this account's role.";
    public const string InactivateMessage = "Transfer the Practice Owner designation before inactivating this staff member.";

    /// <summary>True when <paramref name="accountId"/> is the login of the active Practice Owner. Locks the active designation row for the rest of the transaction.</summary>
    public static async Task<bool> IsOwnerAccountAsync(AlveraDbContext db, Guid accountId, CancellationToken ct) =>
        await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM [PracticeOwnerDesignations] d WITH (UPDLOCK, HOLDLOCK)
            JOIN [StaffProfiles] s ON s.[Id] = d.[StaffProfileId]
            WHERE d.[EndedAtUtc] IS NULL AND s.[UserAccountId] = {accountId}
            """).SingleAsync(ct) > 0;

    /// <summary>True when <paramref name="staffProfileId"/> is the active Practice Owner. Locks the active designation row for the rest of the transaction.</summary>
    public static async Task<bool> IsOwnerStaffAsync(AlveraDbContext db, Guid staffProfileId, CancellationToken ct) =>
        await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM [PracticeOwnerDesignations] WITH (UPDLOCK, HOLDLOCK)
            WHERE [EndedAtUtc] IS NULL AND [StaffProfileId] = {staffProfileId}
            """).SingleAsync(ct) > 0;

    /// <summary>Throws <see cref="OwnerTransferRequiredException"/> when <paramref name="accountId"/> is the active owner's login.</summary>
    public static async Task EnsureNotOwnerAccountAsync(AlveraDbContext db, Guid accountId, string message, CancellationToken ct)
    {
        if (await IsOwnerAccountAsync(db, accountId, ct)) throw new OwnerTransferRequiredException(message);
    }

    /// <summary>
    /// Locks the given account rows one by one in ascending SQL Server <c>uniqueidentifier</c> order (18.3a) and returns them by id. Missing ids are absent from the result.
    /// Call inside the operation's transaction, after the administrator guard and the designation lock.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, LockedAccount>> LockAccountsAsync(AlveraDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var result = new Dictionary<Guid, LockedAccount>();
        foreach (var id in ids.Distinct().OrderBy(i => new SqlGuid(i)))
        {
            var row = await db.Database.SqlQuery<LockedAccount>($"""
                SELECT [Id], [Role], [IsDisabled], [AccountKind] FROM [UserAccounts] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}
                """).SingleOrDefaultAsync(ct);
            if (row is not null) result[id] = row;
        }
        return result;
    }

    /// <summary>An actor that may perform a protected designation (E1a): an enabled Administrator, staff or Deployment Administrator.</summary>
    public static bool IsEligibleDesignator(LockedAccount? actor) =>
        actor is not null && actor.IsEnabledAdministrator
        && (actor.AccountKind == (byte)Identity.AccountKind.Staff || actor.AccountKind == (byte)Identity.AccountKind.DeploymentAdministrator);
}
