using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Documents;

/// <summary>
/// Issues <see cref="DocumentEvent.EventNumber"/> (docs/DOCUMENTS.md, "Seam for the notification story"). One UPDATE on the single counter row takes a row lock that is held until the caller's transaction ends, so
/// event numbers become visible in commit order: a reader that has seen N has seen every number below N, a rolled-back transaction leaves no gap and a number is never reused. The caller must therefore already be in
/// a transaction (refused otherwise, because without one the lock would be released at once and the order guarantee lost) and should call this as late as possible, just before it writes its event row.
/// </summary>
public static class DocumentEventNumbers
{
    public static async Task<long> IssueAsync(AlveraDbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("A document event number must be issued inside the transaction that writes the event, so that the counter's row lock is held until commit.");
        var changed = await db.Database.ExecuteSqlAsync($"UPDATE [DocumentEventCounter] SET [LastEventNumber] = [LastEventNumber] + 1 WHERE [Id] = 1", ct);
        if (changed != 1) throw new InvalidOperationException("The document event counter row is missing.");
        return await db.Database.SqlQuery<long>($"SELECT [LastEventNumber] AS [Value] FROM [DocumentEventCounter] WHERE [Id] = 1").SingleAsync(ct);
    }
}
