using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Auditing;

/// <summary>
/// ALV-002-C01: the shared audit-event primitive every domain writes through, not just Identity.
/// STORY-002 proved the concept (role-change auditing); this generalizes it to actor, timestamp,
/// action, target/entity identity, reason/context, and correlation metadata, so a future clinical
/// or financial domain adopts this instead of inventing its own trust mechanism.
///
/// This method only stages the entry on the change tracker (<see cref="Microsoft.EntityFrameworkCore.DbSet{TEntity}.Add"/>)
/// - it deliberately does not call SaveChanges itself. The caller adds their own business-entity
/// changes to the same <see cref="AlveraDbContext"/> and saves once, so the audit write and the
/// business change either both commit or neither does (see
/// AuditLogImmutabilityTests.A_business_change_cannot_commit_if_its_coupled_audit_write_is_invalid
/// for the test proving this coupling, and AlveraDbContext's SaveChanges guard for the separate
/// "audit entries can never be altered after the fact" guarantee).
/// </summary>
public static class AuditService
{
    public static void Record(
        AlveraDbContext db,
        string eventType,
        string entityType,
        Guid entityId,
        Guid? performedByUserAccountId,
        string details,
        string? reason = null,
        Guid? correlationId = null)
    {
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            EntityType = entityType,
            TargetUserAccountId = entityId,
            PerformedByUserAccountId = performedByUserAccountId,
            Details = details,
            Reason = reason,
            CorrelationId = correlationId,
            TimestampUtc = DateTimeOffset.UtcNow,
        });
    }
}
