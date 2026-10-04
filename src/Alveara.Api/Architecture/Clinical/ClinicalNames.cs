using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>Resolves the staff members' display names for attribution ("reviewed by", "signed by") in one query. An account with no staff profile reads as "Staff member".</summary>
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
        return map;
    }

    public static string? Name(IReadOnlyDictionary<Guid, string> map, Guid? id) => id is null ? null : map.TryGetValue(id.Value, out var n) ? n : Fallback;
}
