using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Permissions;

/// <summary>Told each time a new policy snapshot is published, so invalid stored rows are reported without the decision path knowing anything about auditing.</summary>
public interface IPolicyObserver
{
    Task OnPublishedAsync(PolicySnapshot snapshot, CancellationToken ct);
}

/// <summary>
/// ALV-N017: reports invalid stored policy rows. A privacy-safe log line carries only counts; one audit entry <c>PermissionPolicyInvalidRowDetected</c> is written per (row id, row version), found again by its
/// exact text, so a restart, a reload or a second host never floods the audit trail. The entry holds the row id, the reason code and the row version, never a role or permission string taken from the row.
/// Failure to report is logged with its error class and never affects a decision.
/// </summary>
public sealed class AuditingPolicyObserver(IServiceScopeFactory scopes, ILogger<AuditingPolicyObserver> logger) : IPolicyObserver
{
    public static readonly TimeSpan Budget = TimeSpan.FromSeconds(3);

    public async Task OnPublishedAsync(PolicySnapshot snapshot, CancellationToken ct)
    {
        if (snapshot.InvalidRows.Count == 0 && !snapshot.Degraded) return;
        logger.LogWarning("permission_policy_invalid_rows count={Count} degraded={Degraded} roles_needing_repair={Roles} policy_version={Version}",
            snapshot.InvalidRows.Count, snapshot.Degraded, snapshot.RoleHealth.Count(kv => kv.Value == RoleHealth.RepairRequired), snapshot.Version);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Budget);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
            // one query per batch of rows, not one per row: which of these rows were already reported, by the exact text of the entry
            var reported = new HashSet<(Guid, string)>();
            foreach (var batch in snapshot.InvalidRows.Chunk(500))
            {
                var ids = batch.Select(r => r.Id).ToList();
                var found = await db.AuditLogEntries.AsNoTracking()
                    .Where(a => a.EventType == AuditEventTypes.PermissionPolicyInvalidRowDetected && ids.Contains(a.TargetUserAccountId))
                    .Select(a => new { a.TargetUserAccountId, a.Details }).ToListAsync(timeout.Token);
                foreach (var f in found) reported.Add((f.TargetUserAccountId, f.Details));
            }
            foreach (var row in snapshot.InvalidRows)
            {
                var details = $"Invalid permission policy row detected: {row.Reason} (row version {Convert.ToHexString(row.RowVersion ?? [])}).";
                if (!reported.Contains((row.Id, details))) AuditService.Record(db, AuditEventTypes.PermissionPolicyInvalidRowDetected, "PermissionPolicyRow", row.Id, null, details);
            }
            await db.SaveChangesAsync(timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning("permission_policy_invalid_row_report_failed error_class={ErrorClass}", ex.GetType().Name);
        }
    }
}
