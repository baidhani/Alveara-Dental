using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Concurrency;

/// <summary>
/// ALV-002-C01: saves a context whose entities may carry a version/concurrency token (e.g.
/// UserAccount.RowVersion, configured as EF Core's <c>IsRowVersion()</c> in AlveraDbContext), and
/// translates a stale write into the shared <see cref="ConcurrencyConflictException"/> instead of
/// letting EF's own exception type leak to callers.
/// </summary>
public static class ConcurrencySaveGuard
{
    public static async Task SaveOrThrowConflictAsync(AlveraDbContext db, string entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConcurrencyConflictException(entityType, entityId);
        }
    }
}
