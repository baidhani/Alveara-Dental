using System.Data;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>A refused policy-administration request (stable code, status and message safe to show).</summary>
public sealed class PolicyAdminException(string code, string message, int statusCode) : Exception(message)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed record EffectiveCell(string Permission, bool Effective, string Source, bool Grantable, bool Delegable, string? Reason, Guid? ChangedByUserId, DateTimeOffset? ChangedAtUtc, bool RequiresTypedConfirmation = false);
public sealed record EffectiveRole(string Role, string Health, IReadOnlyList<EffectiveCell> Permissions, int EnabledUsers = 0);
public sealed record PolicyHealth(string Status, long Version, bool Stale, int InvalidRowCount, int RetiredRowCount, int OverrideCount, IReadOnlyList<string> RolesNeedingRepair, IReadOnlyList<InvalidRowView> InvalidRows, IReadOnlyList<RetiredRowView> RetiredRows);
public sealed record EffectivePolicy(long Version, PolicyHealth Health, IReadOnlyList<EffectiveRole> Roles);
public sealed record InvalidRowView(Guid Id, string Table, string Role, string Permission, string? TargetRole, string? Effect, string Reason, string RowVersion);
public sealed record RetiredRowView(Guid Id, string Table, string Role, string Permission);
public sealed record PolicyHistoryItem(Guid Id, long PolicyVersion, string Kind, string Role, string Permission, string? TargetRole, bool OldEffective, bool NewEffective, string Reason, Guid ChangedByUserId, bool ViaDelegation, DateTimeOffset ChangedAtUtc);
public sealed record PolicyHistoryPage(IReadOnlyList<PolicyHistoryItem> Items, int Total, int Page, int PageSize);
public sealed record RemoveRowResult(Guid Id, long PolicyVersion);

/// <summary>
/// ALV-N017: what an administrator can read about the policy (the effective matrix with where each cell comes from, the immutable history, the health of the stored rows) and the one write this story allows: removing a
/// stored row that is invalid (or names a retired permission), with a written reason, in one transaction with its history row, its audit entry and the version change, so a failure of any part changes nothing. A valid
/// row cannot be removed here: changing the policy is ALV-N018. No response carries free text from a stored row other than the administrator-written reason.
/// </summary>
public class PolicyAdminService(AlveraDbContext db, IEffectivePermissions permissions)
{
    public const int MaxReasonLength = 500;

    // ---------- reads ----------

    public async Task<PolicyHealth> HealthAsync(CancellationToken ct)
    {
        var read = await permissions.SnapshotAsync(ct);
        return ToHealth(read.Snapshot, read.Stale);
    }

    public async Task<EffectivePolicy> EffectiveAsync(CancellationToken ct)
    {
        var read = await permissions.SnapshotAsync(ct);
        var snapshot = read.Snapshot;
        var overrides = await db.RolePermissionOverrides.AsNoTracking().ToListAsync(ct);
        var enabledByRole = (await db.UserAccounts.AsNoTracking().Where(u => !u.IsDisabled).GroupBy(u => u.Role).Select(g => new { Role = g.Key, Count = g.Count() }).ToListAsync(ct)).ToDictionary(x => x.Role, x => x.Count);
        var roles = new List<EffectiveRole>();
        foreach (var role in Enum.GetValues<Role>())
        {
            // what a request by this role would be answered with: the administrator and an unassigned account are fixed; any other role is denied outright when it needs repair or the store is degraded
            var fixedRole = PermissionProtection.IsFixedRole(role);
            var health = fixedRole ? "Healthy" : snapshot.Degraded ? "Degraded" : snapshot.HealthOf(role) == RoleHealth.RepairRequired ? "RepairRequired" : "Healthy";
            var held = health == "Healthy" ? snapshot.EffectiveFor(role) : new HashSet<Permission>();
            var cells = new List<EffectiveCell>();
            foreach (var permission in Enum.GetValues<Permission>())
            {
                var row = overrides.FirstOrDefault(o => o.Role == role.ToString() && o.Permission == permission.ToString() && IsApplied(snapshot, role, o));
                var source = row is null ? "default" : row.Effect == OverrideEffects.Grant ? "granted" : "revoked";
                cells.Add(new EffectiveCell(permission.ToString(), held.Contains(permission), source, PermissionProtection.IsGrantable(permission) && !fixedRole, PermissionProtection.IsDelegable(permission), row?.Reason, row?.ChangedByUserId, row?.ChangedAtUtc, PolicyWriteRules.HighImpact.Contains(permission)));
            }
            roles.Add(new EffectiveRole(role.ToString(), health, cells, enabledByRole.GetValueOrDefault(role)));
        }
        return new EffectivePolicy(snapshot.Version, ToHealth(snapshot, read.Stale), roles);
    }

    /// <summary>A stored row is applied only when the snapshot did not reject it (a rejected row is invalid and shows as the default).</summary>
    private static bool IsApplied(PolicySnapshot snapshot, Role role, RolePermissionOverride row) =>
        !snapshot.InvalidRows.Any(i => i.Id == row.Id) && !snapshot.RetiredRows.Any(r => r.Id == row.Id) && !PermissionProtection.IsFixedRole(role);

    public async Task<PolicyHistoryPage> HistoryAsync(int? page, int? pageSize, CancellationToken ct)
    {
        var p = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 50, 1, 200);
        var q = db.RolePermissionHistories.AsNoTracking();
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(h => h.PolicyVersion).ThenBy(h => h.Id).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return new PolicyHistoryPage(rows.Select(h => new PolicyHistoryItem(h.Id, h.PolicyVersion, h.Kind, h.Role, h.Permission, h.TargetRole, h.OldEffective, h.NewEffective, h.Reason, h.ChangedByUserId, h.ViaDelegation, h.ChangedAtUtc)).ToList(), total, p, size);
    }

    private static PolicyHealth ToHealth(PolicySnapshot s, bool stale) => new(
        s.Degraded ? "Degraded" : s.InvalidRows.Count > 0 ? "RepairRequired" : "Healthy",
        s.Version, stale, s.InvalidRows.Count, s.RetiredRows.Count, s.OverrideCount,
        s.RoleHealth.Where(kv => kv.Value == RoleHealth.RepairRequired).Select(kv => kv.Key.ToString()).OrderBy(x => x).ToList(),
        s.InvalidRows.Select(r => new InvalidRowView(r.Id, r.Table, r.Role, r.Permission, r.TargetRole, r.Effect, r.Reason.ToString(), Convert.ToBase64String(r.RowVersion ?? []))).ToList(),
        s.RetiredRows.Select(r => new RetiredRowView(r.Id, r.Table, r.Role, r.Permission)).ToList());

    // ---------- the repair write ----------

    /// <summary>
    /// Removes one invalid (or retired) stored row. <paramref name="rowVersion"/> is the value the administrator saw: a changed row is a 409. The row must be invalid or retired in the current policy; a valid row is refused
    /// (<c>row_not_removable</c>). Version bump, delete, history row and audit entry commit together.
    /// </summary>
    public async Task<RemoveRowResult> RemoveRowAsync(Guid adminId, Guid rowId, string? reason, string? rowVersion, CancellationToken ct)
    {
        var why = reason?.Trim() ?? "";
        if (why.Length == 0 || why.Length > MaxReasonLength || why.Any(char.IsControl))
            throw new PolicyAdminException("reason_required", "Write the reason for removing it (up to 500 characters).", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(rowVersion ?? ""); }
        catch (FormatException) { throw new PolicyAdminException("row_version_required", "Send the row version you were shown.", 400); }
        if (expected.Length == 0) throw new PolicyAdminException("row_version_required", "Send the row version you were shown.", 400);

        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        await db.Database.ExecuteSqlAsync($"UPDATE [PermissionPolicyState] SET [Version] = [Version] + 1, [UpdatedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = 1", ct);   // takes the row lock: writers are serialized
        var version = await db.Database.SqlQuery<long>($"SELECT [Version] AS [Value] FROM [PermissionPolicyState] WHERE [Id] = 1").SingleAsync(ct);

        var overrides = await db.RolePermissionOverrides.ToListAsync(ct);
        var delegations = await db.PermissionDelegations.ToListAsync(ct);
        var current = PolicySnapshotBuilder.Build(version, overrides, delegations);
        var invalid = current.InvalidRows.FirstOrDefault(r => r.Id == rowId);
        var retired = current.RetiredRows.FirstOrDefault(r => r.Id == rowId);
        if (invalid is null && retired is null)
        {
            var exists = overrides.Any(o => o.Id == rowId) || delegations.Any(d => d.Id == rowId);
            throw exists ? new PolicyAdminException("row_not_removable", "Only an invalid row can be removed here.", 409) : new PolicyAdminException("row_not_found", "That row was not found.", 404);
        }

        string role, permission; string? target = null; byte[]? stored;
        if (overrides.FirstOrDefault(o => o.Id == rowId) is { } o)
        {
            role = o.Role; permission = o.Permission; stored = o.RowVersion; db.RolePermissionOverrides.Remove(o);
        }
        else
        {
            var d = delegations.First(x => x.Id == rowId);
            role = d.DelegateRole; permission = d.Permission; target = d.TargetRole; stored = d.RowVersion; db.PermissionDelegations.Remove(d);
        }
        if (stored is null || !stored.AsSpan().SequenceEqual(expected))
            throw new PolicyAdminException("row_changed", "That row changed since you looked at it. Reload and try again.", 409);

        db.RolePermissionHistories.Add(new RolePermissionHistory
        {
            Id = Guid.NewGuid(), PolicyVersion = version, Kind = PolicyChangeKinds.InvalidRowRemoved, Role = Clip(role, 32), Permission = Clip(permission, 64), TargetRole = target is null ? null : Clip(target, 32),
            OldEffective = false, NewEffective = false, Reason = why, ChangedByUserId = adminId, ViaDelegation = false, ChangedAtUtc = DateTimeOffset.UtcNow,
        });
        AuditService.Record(db, AuditEventTypes.InvalidPolicyRowRemoved, "PermissionPolicyRow", rowId, adminId, $"A {(invalid is null ? "retired" : "invalid")} permission policy row was removed (policy version {version}).", why);
        try
        {
            await db.SaveChangesAsync(ct);                                                                  // any failure here (the audit write included) rolls the whole change back
        }
        catch (DbUpdateException)
        {
            throw new PolicyAdminException("policy_write_failed", "The change could not be saved, so nothing was changed. Try again.", 503);
        }
        await tx.CommitAsync(ct);
        return new RemoveRowResult(rowId, version);
    }

    private static string Clip(string value, int max) => value.Length <= max ? value : value[..max];
}
