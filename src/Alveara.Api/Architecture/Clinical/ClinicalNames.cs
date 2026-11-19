using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// Resolves the people named in attribution ("reviewed by", "signed by", "performed by") in at most two queries. ALV-N025 (one actor-name rule; F-029, F-138,
/// F-149, F-172, F-214, U-011): the staff profile's display name; for an account with no staff profile, the account's username; never a bare id. Only an id
/// that is not an account at all reads as <see cref="Fallback"/>. The ids themselves stay in the API for auditors.
/// </summary>
internal static class ClinicalNames
{
    public const string Fallback = "Staff member";

    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(AlveraDbContext db, IEnumerable<Guid?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(i => i.HasValue).Select(i => i!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        var found = await db.StaffProfiles.AsNoTracking().Where(s => s.UserAccountId != null && ids.Contains(s.UserAccountId.Value))
            .Select(s => new { Id = s.UserAccountId!.Value, s.DisplayName }).ToListAsync(ct);
        var map = new Dictionary<Guid, string>();
        foreach (var f in found) map.TryAdd(f.Id, f.DisplayName);
        var missing = ids.Where(i => !map.ContainsKey(i)).ToList();
        if (missing.Count > 0)
            foreach (var u in await db.UserAccounts.AsNoTracking().Where(u => missing.Contains(u.Id)).Select(u => new { u.Id, u.Username }).ToListAsync(ct))
                map.TryAdd(u.Id, u.Username);
        return map;
    }

    public static string? Name(IReadOnlyDictionary<Guid, string> map, Guid? id) => id is null ? null : map.TryGetValue(id.Value, out var n) ? n : Fallback;
}
