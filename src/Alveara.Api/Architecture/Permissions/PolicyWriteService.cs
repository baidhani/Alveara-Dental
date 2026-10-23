using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Idempotency;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Permissions;

public sealed record SetOverrideRequest(string? Role, string? Permission, string? Effect, string? Reason, long? ExpectedVersion, bool Confirmed, string? TypedConfirmation);
public sealed record DelegatedGrantRequest(string? TargetRole, string? Permission, bool? Allowed, string? Reason, long? ExpectedVersion, bool Confirmed);
public sealed record DelegationRowDto(string DelegateRole, string Permission, string TargetRole);
public sealed record SetDelegationsRequest(List<DelegationRowDto>? Rows, string? Reason, long? ExpectedVersion, bool Confirmed);

/// <summary>What a policy write did. <see cref="Change"/> is one of Granted, Revoked, Reverted, DelegationAdded, DelegationRemoved or DelegationsChanged; <see cref="EnabledUsers"/> is how many enabled accounts hold the role that was changed.</summary>
public sealed record PolicyWriteResult(long PolicyVersion, bool Replayed, string Change, string? Role, string? Permission, bool? Effective, string? Source, int EnabledUsers);

/// <summary>
/// ALV-N018: the rules of a policy change that are about the permission itself and not about who may ask. High-impact permissions (the reviewer-approved list, proposal R4) need the person to type the permission name
/// before a change to them is accepted; every other change needs an explicit confirmation (the screen shows how many enabled accounts it affects).
/// </summary>
public static class PolicyWriteRules
{
    public static readonly IReadOnlySet<Permission> HighImpact = new HashSet<Permission>
    {
        Permission.ManageAccountStatus, Permission.RevokeSessions, Permission.ViewPatientRecords, Permission.ViewAuditLog, Permission.ManageBackups, Permission.ManageBilling,
        Permission.ManagePrescriptions, Permission.ManagePracticeConfiguration, Permission.ManageDocumentSettings, Permission.ManageDocumentStorage,
    };

    public const int MaxReasonLength = 500;
}

/// <summary>
/// ALV-N018: the only writers of the stored policy besides the invalid-row repair of ALV-N017. Every write: validates a closed set of role and permission names (exact, never case-folded, never numbers), refuses the
/// protected permissions, the administrator's and an unassigned account's cells, and a role that needs repair; needs a bounded reason, an explicit confirmation (typed for the high-impact list), an
/// <c>Idempotency-Key</c> and the policy version the caller was shown (a stale caller gets 409 and nothing is stored); and then, in ONE transaction, moves the version, changes the rows, writes the immutable history and the
/// audit entry and records the idempotency receipts, so any failure (the audit write included) changes nothing. The same key with the same request replays quietly; the same key with a different request is refused.
/// An administrator edits any non-protected cell of a working role; a delegate (today only the office manager, through the delegation table) may only grant or revoke a delegated permission, for a delegated target role,
/// that the delegate itself holds, and never for its own role. Errors are stable codes (see <see cref="PolicyAdminException"/>).
/// </summary>
public class PolicyWriteService(AlveraDbContext db, IEffectivePermissions permissions)
{
    private const string OverrideCommand = "PermissionOverride";
    private const string DelegatedCommand = "PermissionDelegatedGrant";
    private const string DelegationsCommand = "PermissionDelegations";

    // ---------- the administrator's override write ----------

    public async Task<PolicyWriteResult> SetOverrideAsync(Guid adminId, SetOverrideRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var key = RequireKey(idempotencyKey);
        var reason = RequireReason(request.Reason);
        var expected = request.ExpectedVersion ?? throw Bad("expected_version_required", "Send the policy version you were shown.");
        var role = ParseRole(request.Role);
        if (PermissionProtection.IsFixedRole(role)) throw Bad("role_not_editable", "The administrator and unassigned cells cannot be changed.");
        var permission = ParsePermission(request.Permission);
        if (!PermissionProtection.IsGrantable(permission)) throw Bad("permission_protected", "That permission can never be granted to another role.");
        var effect = request.Effect is OverrideEffects.Grant or OverrideEffects.Revoke or "Revert" ? request.Effect : throw Bad("invalid_effect", "The effect must be Grant, Revoke or Revert.");
        RequireConfirmation(permission, request.Confirmed, request.TypedConfirmation);

        var hash = Hash(OverrideCommand, role, permission, effect, reason, expected);
        if (await ReplayAsync(adminId, OverrideCommand, key, hash, ct) is { } replay) return replay with { Role = role.ToString(), Permission = permission.ToString() };

        return await ApplyAsync(adminId, OverrideCommand, key, hash, expected, delegated: false, reason, async (current, rows, version) =>
        {
            Guard(current, role);
            var existing = rows.Overrides.FirstOrDefault(o => o.Role == role.ToString() && o.Permission == permission.ToString());
            var defaultHolds = PolicySnapshotBuilder.DefaultHolds(role, permission);
            return await Cell(role, permission, effect, existing, defaultHolds, version, adminId, reason, delegated: false, ct);
        }, ct);
    }

    // ---------- a delegate's grant or revoke ----------

    public async Task<PolicyWriteResult> DelegatedGrantAsync(Guid actorId, Role actorRole, DelegatedGrantRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var key = RequireKey(idempotencyKey);
        var reason = RequireReason(request.Reason);
        var expected = request.ExpectedVersion ?? throw Bad("expected_version_required", "Send the policy version you were shown.");
        var target = ParseRole(request.TargetRole);
        var permission = ParsePermission(request.Permission);
        var allowed = request.Allowed ?? throw Bad("allowed_required", "Say whether the role may hold the permission.");
        if (!request.Confirmed) throw Bad("confirmation_required", "Confirm the change.");

        var hash = Hash(DelegatedCommand, target, permission, allowed ? "Allow" : "Deny", reason, expected);
        if (await ReplayAsync(actorId, DelegatedCommand, key, hash, ct) is { } replay) return replay with { Role = target.ToString(), Permission = permission.ToString() };

        return await ApplyAsync(actorId, DelegatedCommand, key, hash, expected, delegated: true, reason, async (current, rows, version) =>
        {
            // the closed delegation table: this delegate, this permission, this target; and the delegate itself must hold the permission (nobody grants what they do not hold)
            if (!current.Delegations.Any(d => d.DelegateRole == actorRole && d.Permission == permission && d.TargetRole == target))
                throw new PolicyAdminException("delegation_not_allowed", "You may not change that permission for that role.", 403);
            if (!current.EffectiveFor(actorRole).Contains(permission) || current.HealthOf(actorRole) != RoleHealth.Healthy || current.Degraded)
                throw new PolicyAdminException("delegation_not_allowed", "You may not change a permission you do not hold.", 403);
            Guard(current, target);
            var existing = rows.Overrides.FirstOrDefault(o => o.Role == target.ToString() && o.Permission == permission.ToString());
            var defaultHolds = PolicySnapshotBuilder.DefaultHolds(target, permission);
            // the delegate says what the role may do; the stored change is whatever moves the cell there from the default
            var effect = allowed == defaultHolds ? "Revert" : allowed ? OverrideEffects.Grant : OverrideEffects.Revoke;
            return await Cell(target, permission, effect, existing, defaultHolds, version, actorId, reason, delegated: true, ct);
        }, ct);
    }

    // ---------- the administrator's delegation table ----------

    public async Task<PolicyWriteResult> SetDelegationsAsync(Guid adminId, SetDelegationsRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var key = RequireKey(idempotencyKey);
        var reason = RequireReason(request.Reason);
        var expected = request.ExpectedVersion ?? throw Bad("expected_version_required", "Send the policy version you were shown.");
        if (!request.Confirmed) throw Bad("confirmation_required", "Confirm the change.");
        var wanted = new List<(Role Delegate, Permission Permission, Role Target)>();
        foreach (var row in request.Rows ?? throw Bad("rows_required", "Send the delegation rows."))
        {
            var delegateRole = ParseRole(row.DelegateRole);
            var permission = ParsePermission(row.Permission);
            var target = ParseRole(row.TargetRole);
            if (!PermissionProtection.MayDelegate(delegateRole)) throw Bad("delegation_not_allowed", "Only the office manager can be given a delegation.");
            if (!PermissionProtection.IsDelegable(permission)) throw Bad("delegation_not_allowed", "That permission can never be delegated.");
            if (!PermissionProtection.IsDelegationTarget(target)) throw Bad("delegation_not_allowed", "A delegation can only name Dentist, Hygienist, Assistant, FrontDesk or Billing as its target.");
            if (!wanted.Contains((delegateRole, permission, target))) wanted.Add((delegateRole, permission, target));
        }
        var canonical = string.Join(";", wanted.Select(w => $"{w.Delegate}|{w.Permission}|{w.Target}").OrderBy(x => x, StringComparer.Ordinal));
        var hash = Hash(DelegationsCommand, canonical, reason, expected);
        if (await ReplayAsync(adminId, DelegationsCommand, key, hash, ct) is { } replay) return replay;

        return await ApplyAsync(adminId, DelegationsCommand, key, hash, expected, delegated: false, reason, (current, rows, version) =>
        {
            var validNow = rows.Delegations.Where(d => current.Delegations.Any(a => a.DelegateRole.ToString() == d.DelegateRole && a.Permission.ToString() == d.Permission && a.TargetRole.ToString() == d.TargetRole)).ToList();
            var adds = wanted.Where(w => !validNow.Any(d => d.DelegateRole == w.Delegate.ToString() && d.Permission == w.Permission.ToString() && d.TargetRole == w.Target.ToString())).ToList();
            var removes = validNow.Where(d => !wanted.Any(w => w.Delegate.ToString() == d.DelegateRole && w.Permission.ToString() == d.Permission && w.Target.ToString() == d.TargetRole)).ToList();
            if (adds.Count == 0 && removes.Count == 0) throw new PolicyAdminException("no_change", "That is already the delegation table.", 409);
            if (adds.Any(a => rows.Delegations.Any(d => d.DelegateRole == a.Delegate.ToString() && d.Permission == a.Permission.ToString() && d.TargetRole == a.Target.ToString())))
                throw new PolicyAdminException("policy_needs_repair", "A stored delegation row is invalid; remove it first.", 409);   // the unique index would refuse a row that is already stored but invalid
            var now = DateTimeOffset.UtcNow;
            foreach (var a in adds)
            {
                db.PermissionDelegations.Add(new PermissionDelegation { Id = Guid.NewGuid(), DelegateRole = a.Delegate.ToString(), Permission = a.Permission.ToString(), TargetRole = a.Target.ToString(), ChangedByUserId = adminId, ChangedAtUtc = now });
                db.RolePermissionHistories.Add(History(version, PolicyChangeKinds.DelegationAdded, a.Delegate.ToString(), a.Permission.ToString(), a.Target.ToString(), false, true, adminId, reason, now, delegated: false));
            }
            foreach (var r in removes)
            {
                db.PermissionDelegations.Remove(r);
                db.RolePermissionHistories.Add(History(version, PolicyChangeKinds.DelegationRemoved, r.DelegateRole, r.Permission, r.TargetRole, true, false, adminId, reason, now, delegated: false));
            }
            AuditService.Record(db, AuditEventTypes.DelegationChanged, "PermissionPolicy", adminId, adminId,
                $"The delegation table changed (policy version {version}): {adds.Count} added, {removes.Count} removed.", reason);
            return Task.FromResult(new PolicyWriteResult(version, false, "DelegationsChanged", null, null, null, null, 0));
        }, ct);
    }

    // ---------- shared machinery ----------

    private delegate Task<PolicyWriteResult> Change(PolicySnapshot current, PolicyRows rows, long version);

    /// <summary>The one transaction: the version moves only if it is still the one the caller saw (otherwise 409 and nothing is stored); the change runs against the rows as they are now; receipts, row, history and audit commit together.</summary>
    private async Task<PolicyWriteResult> ApplyAsync(Guid actorId, string command, string key, string hash, long expected, bool delegated, string reason, Change change, CancellationToken ct)
    {
        _ = delegated; _ = reason;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        var moved = await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1 AND [Version] = {expected}", ct);
        if (moved == 0) throw new PolicyAdminException("policy_changed", "The policy changed since you looked at it. Reload and try again.", 409);
        var version = await db.Database.SqlQuery<long>($"SELECT [Version] AS [Value] FROM [PermissionPolicyState] WHERE [Id] = 1").SingleAsync(ct);
        var rows = new PolicyRows(await db.RolePermissionOverrides.ToListAsync(ct), await db.PermissionDelegations.ToListAsync(ct));
        var current = PolicySnapshotBuilder.Build(version, rows.Overrides, rows.Delegations);

        var result = await change(current, rows, version);
        IdempotencyGuard.MarkProcessed(db, $"{command}:{actorId:N}", key);
        IdempotencyGuard.MarkProcessed(db, $"{command}:{actorId:N}:{hash}", key);
        try
        {
            await db.SaveChangesAsync(ct);                                                                  // a failure here (the audit write included) rolls the whole change back
        }
        catch (DbUpdateException ex) when (IdempotencyGuard.IsDuplicateReceiptViolation(ex))
        {
            db.ChangeTracker.Clear();
            await tx.RollbackAsync(ct);
            return await ReplayAsync(actorId, command, key, hash, ct) ?? throw new PolicyAdminException("idempotency_key_reused", "That key was already used for a different request.", 409);
        }
        catch (DbUpdateException)
        {
            throw new PolicyAdminException("policy_write_failed", "The change could not be saved, so nothing was changed. Try again.", 503);
        }
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>The same key and the same request is a quiet replay; the same key with another request is refused; a new key is null (go ahead).</summary>
    private async Task<PolicyWriteResult?> ReplayAsync(Guid actorId, string command, string key, string hash, CancellationToken ct)
    {
        if (!await IdempotencyGuard.AlreadyProcessedAsync(db, $"{command}:{actorId:N}", key, ct)) return null;
        if (!await IdempotencyGuard.AlreadyProcessedAsync(db, $"{command}:{actorId:N}:{hash}", key, ct))
            throw new PolicyAdminException("idempotency_key_reused", "That key was already used for a different request.", 409);
        var version = await db.PermissionPolicyStates.AsNoTracking().Select(s => s.Version).SingleAsync(ct);
        return new PolicyWriteResult(version, true, "Replayed", null, null, null, null, 0);
    }

    private static void Guard(PolicySnapshot current, Role role)
    {
        if (current.Degraded || current.HealthOf(role) == RoleHealth.RepairRequired)
            throw new PolicyAdminException("policy_needs_repair", "The stored policy for that role needs repair first.", 409);
    }

    /// <summary>Applies one change to one cell: Grant or Revoke adds or keeps a row that differs from the default, Revert removes it; a change that would leave the cell as it is, is refused.</summary>
    private async Task<PolicyWriteResult> Cell(Role role, Permission permission, string effect, RolePermissionOverride? existing, bool defaultHolds, long version, Guid actorId, string reason, bool delegated, CancellationToken ct)
    {
        var oldEffective = existing is null ? defaultHolds : existing.Effect == OverrideEffects.Grant;
        var now = DateTimeOffset.UtcNow;
        string kind;
        bool newEffective;
        if (effect == "Revert")
        {
            if (existing is null) throw new PolicyAdminException("no_change", "That cell is already at its default.", 409);
            db.RolePermissionOverrides.Remove(existing);
            kind = PolicyChangeKinds.Reverted; newEffective = defaultHolds;
        }
        else
        {
            var wantsGrant = effect == OverrideEffects.Grant;
            if (wantsGrant == defaultHolds) throw new PolicyAdminException("matches_default", "That is the default for this role: revert the cell instead.", 409);
            if (existing is not null) throw new PolicyAdminException("no_change", "That cell is already set that way.", 409);
            db.RolePermissionOverrides.Add(new RolePermissionOverride { Id = Guid.NewGuid(), Role = role.ToString(), Permission = permission.ToString(), Effect = effect, Reason = reason, ChangedByUserId = actorId, ChangedAtUtc = now });
            kind = wantsGrant ? PolicyChangeKinds.Granted : PolicyChangeKinds.Revoked; newEffective = wantsGrant;
        }
        db.RolePermissionHistories.Add(History(version, kind, role.ToString(), permission.ToString(), null, oldEffective, newEffective, actorId, reason, now, delegated));
        var auditType = kind == PolicyChangeKinds.Granted ? AuditEventTypes.PermissionGranted : kind == PolicyChangeKinds.Revoked ? AuditEventTypes.PermissionRevoked : AuditEventTypes.PermissionReverted;
        AuditService.Record(db, auditType, "PermissionPolicy", actorId, actorId, $"{kind} {permission} for {role}{(delegated ? " (delegated)" : "")}: {(oldEffective ? "held" : "not held")} to {(newEffective ? "held" : "not held")} (policy version {version}).", reason);
        var enabled = await db.UserAccounts.CountAsync(u => u.Role == role && !u.IsDisabled, ct);
        var source = kind == PolicyChangeKinds.Reverted ? "default" : kind == PolicyChangeKinds.Granted ? "granted" : "revoked";
        return new PolicyWriteResult(version, false, kind, role.ToString(), permission.ToString(), newEffective, source, enabled);
    }

    private static RolePermissionHistory History(long version, string kind, string role, string permission, string? target, bool oldEffective, bool newEffective, Guid by, string reason, DateTimeOffset at, bool delegated) =>
        new() { Id = Guid.NewGuid(), PolicyVersion = version, Kind = kind, Role = role, Permission = permission, TargetRole = target, OldEffective = oldEffective, NewEffective = newEffective, Reason = reason, ChangedByUserId = by, ViaDelegation = delegated, ChangedAtUtc = at };

    private static void RequireConfirmation(Permission permission, bool confirmed, string? typed)
    {
        if (!confirmed) throw Bad("confirmation_required", "Confirm the change.");
        if (PolicyWriteRules.HighImpact.Contains(permission) && !string.Equals(typed, permission.ToString(), StringComparison.Ordinal))
            throw Bad("typed_confirmation_required", $"Type {permission} to confirm this change.");
    }

    private static string RequireKey(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length > 64 ? throw Bad("idempotency_key_required", "Send an Idempotency-Key header of up to 64 characters.") : key;

    private static string RequireReason(string? reason)
    {
        var r = reason?.Trim() ?? "";
        return r.Length == 0 || r.Length > PolicyWriteRules.MaxReasonLength || r.Any(char.IsControl) ? throw Bad("reason_required", "Write the reason for the change (up to 500 characters).") : r;
    }

    private static Role ParseRole(string? name) =>
        name is not null && Enum.TryParse<Role>(name, ignoreCase: false, out var role) && Enum.IsDefined(role) && role.ToString() == name && !int.TryParse(name, out _) ? role : throw Bad("invalid_role", "That role is not known.");

    private static Permission ParsePermission(string? name) =>
        name is not null && PermissionRegistry.IsActive(name) && Enum.TryParse<Permission>(name, ignoreCase: false, out var p) && Enum.IsDefined(p) && p.ToString() == name ? p : throw Bad("invalid_permission", "That permission is not known.");

    private static PolicyAdminException Bad(string code, string message) => new(code, message, 400);

    private static string Hash(params object[] parts) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\u001f", parts.Select(p => p.ToString()))))).ToLowerInvariant()[..32];

    // ---------- reads for the screens that write ----------

    /// <summary>What the caller may delegate and where each delegated cell stands now (empty for a role that holds no delegation).</summary>
    public async Task<object> MyDelegationsAsync(Role actorRole, CancellationToken ct)
    {
        var read = await permissions.SnapshotAsync(ct);
        var snapshot = read.Snapshot;
        var mine = snapshot.Delegations.Where(d => d.DelegateRole == actorRole && snapshot.EffectiveFor(actorRole).Contains(d.Permission) && snapshot.HealthOf(actorRole) == RoleHealth.Healthy && !snapshot.Degraded).ToList();
        var groups = new List<object>();
        foreach (var g in mine.GroupBy(d => d.Permission))
        {
            var targets = new List<object>();
            foreach (var d in g.OrderBy(x => x.TargetRole))
            {
                var enabled = await db.UserAccounts.CountAsync(u => u.Role == d.TargetRole && !u.IsDisabled, ct);
                targets.Add(new { role = d.TargetRole.ToString(), allowed = snapshot.EffectiveFor(d.TargetRole).Contains(g.Key), enabledUsers = enabled });
            }
            groups.Add(new { permission = g.Key.ToString(), targets });
        }
        return new { policyVersion = snapshot.Version, delegations = groups };
    }

    /// <summary>The delegation table as an administrator edits it, with what may be put in it.</summary>
    public async Task<object> DelegationsAsync(CancellationToken ct)
    {
        var read = await permissions.SnapshotAsync(ct);
        var snapshot = read.Snapshot;
        return new
        {
            policyVersion = snapshot.Version,
            rows = snapshot.Delegations.OrderBy(d => d.DelegateRole).ThenBy(d => d.Permission).ThenBy(d => d.TargetRole).Select(d => new DelegationRowDto(d.DelegateRole.ToString(), d.Permission.ToString(), d.TargetRole.ToString())).ToList(),
            delegableDelegates = Enum.GetValues<Role>().Where(PermissionProtection.MayDelegate).Select(r => r.ToString()).ToList(),
            delegablePermissions = Enum.GetValues<Permission>().Where(PermissionProtection.IsDelegable).Select(p => p.ToString()).ToList(),
            targetRoles = Enum.GetValues<Role>().Where(PermissionProtection.IsDelegationTarget).Select(r => r.ToString()).ToList(),
        };
    }
}
