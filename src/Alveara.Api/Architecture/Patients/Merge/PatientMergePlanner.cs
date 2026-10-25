using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>What a merge of one patient into another would do: the field decisions it needs, the rows it moves, the records that stay with the retired patient, and what (if anything) stops it.</summary>
internal sealed record MergePlan(List<MergeFieldConflict> Conflicts, List<MergeAutoFill> AutoFills, List<MergeMoveCount> Moves, Dictionary<string, Dictionary<string, string>> Left, List<MergeBlocker> Blockers)
{
    public int LeftCount => Left.Values.Sum(v => v.Count);
}

/// <summary>
/// STORY-017: works out, from the database as it is, what a merge would do. It only reads. A row of the retired patient collides when the surviving patient already has the row a uniqueness rule allows only one of; such a row cannot
/// move, so it stays with the retired patient together with everything that depends on it (rows that point at it, and so on), and the merge is blocked when something that must stay points at something that must move, because
/// that would split one record between two patients.
/// </summary>
internal sealed class PatientMergePlanner(AlveraDbContext db, PatientMergeCatalog catalog)
{
    public async Task<MergePlan> PlanAsync(Patient source, Patient survivor, CancellationToken ct)
    {
        var (conflicts, fills) = PatientMergeFields.Compare(source, survivor);
        var left = await LeftBehindAsync(source.Id, survivor.Id, ct);
        var blockers = new List<MergeBlocker>();
        if (left.Count > 0) await CloseOverDependentsAsync(source.Id, left, blockers, ct);

        var moves = new List<MergeMoveCount>();
        foreach (var column in PatientOwnedData.All)
        {
            var sql = $"SELECT {catalog.KeyExpr(column.Table, "t")} FROM {PatientMergeCatalog.Q(column.Table)} t WHERE t.{PatientMergeCatalog.Q(column.Column)} = @src";
            var rows = left.TryGetValue(column.Table, out var stay)
                ? (await PatientMergeCatalog.RowsAsync(db, sql, ct, ("@src", source.Id))).Count(r => !stay.ContainsKey((string)r[0]!))
                : await PatientMergeCatalog.CountAsync(db, $"SELECT COUNT(*) FROM {PatientMergeCatalog.Q(column.Table)} WHERE {PatientMergeCatalog.Q(column.Column)} = @src", ct, ("@src", source.Id));
            moves.Add(new MergeMoveCount(column.Table, column.Column, rows));
        }
        return new MergePlan(conflicts, fills, moves, left, blockers);
    }

    /// <summary>The rows of the retired patient that break a uniqueness rule of the surviving patient, by table and row key, with the reason.</summary>
    private async Task<Dictionary<string, Dictionary<string, string>>> LeftBehindAsync(Guid source, Guid survivor, CancellationToken ct)
    {
        var left = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var u in catalog.Uniques)
        {
            var filter = u.Filter is null ? "" : $" AND ({u.Filter})";
            var table = PatientMergeCatalog.Q(u.Table);
            var match = u.OtherColumns.Count == 0 ? "1 = 1"
                : string.Join(" AND ", u.OtherColumns.Select(c => $"(v.{PatientMergeCatalog.Q(c)} = s.{PatientMergeCatalog.Q(c)} OR (v.{PatientMergeCatalog.Q(c)} IS NULL AND s.{PatientMergeCatalog.Q(c)} IS NULL))"));
            var sql = $"SELECT {catalog.KeyExpr(u.Table, "s")} FROM (SELECT * FROM {table} WHERE [PatientId] = @src{filter}) s " +
                      $"WHERE EXISTS (SELECT 1 FROM (SELECT * FROM {table} WHERE [PatientId] = @surv{filter}) v WHERE {match})";
            var why = u.OtherColumns.Count == 0 ? "the surviving patient already has its one" : $"the surviving patient already has one with the same {string.Join(" and ", u.OtherColumns)}";
            foreach (var row in await PatientMergeCatalog.RowsAsync(db, sql, ct, ("@src", source), ("@surv", survivor)))
            {
                if (!left.TryGetValue(u.Table, out var keys)) left[u.Table] = keys = new Dictionary<string, string>(StringComparer.Ordinal);
                keys.TryAdd((string)row[0]!, why);
            }
        }
        return left;
    }

    /// <summary>Adds to <paramref name="left"/> every row that depends (directly or through other rows) on a row that stays, and reports a blocker for each staying row that depends on a row that would move.</summary>
    private async Task CloseOverDependentsAsync(Guid source, Dictionary<string, Dictionary<string, string>> left, List<MergeBlocker> blockers, CancellationToken ct)
    {
        var owned = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var table in catalog.Edges.SelectMany(e => new[] { e.Child, e.Parent }).Distinct())
        {
            var rows = await PatientMergeCatalog.RowsAsync(db, $"SELECT {catalog.KeyExpr(table, "t")} FROM {PatientMergeCatalog.Q(table)} t WHERE t.[PatientId] = @src", ct, ("@src", source));
            owned[table] = rows.Select(r => (string)r[0]!).ToHashSet(StringComparer.Ordinal);
        }
        var references = new Dictionary<MergeEdge, List<(string Child, string Parent)>>();
        foreach (var edge in catalog.Edges)
        {
            var sql = $"SELECT {catalog.KeyExpr(edge.Child, "t")}, {PatientMergeCatalog.Concat("t", edge.ChildColumns)} FROM {PatientMergeCatalog.Q(edge.Child)} t WHERE t.[PatientId] = @src";
            // the parent is named by the key the foreign key points at, which is not always its primary key: translate it to the primary key the rest of the plan uses
            var parentSql = $"SELECT {catalog.KeyExpr(edge.Parent, "t")}, {PatientMergeCatalog.Concat("t", edge.ParentColumns)} FROM {PatientMergeCatalog.Q(edge.Parent)} t WHERE t.[PatientId] = @src";
            var parentKey = (await PatientMergeCatalog.RowsAsync(db, parentSql, ct, ("@src", source))).Where(r => r[1] is not null).ToDictionary(r => (string)r[1]!, r => (string)r[0]!, StringComparer.Ordinal);
            references[edge] = (await PatientMergeCatalog.RowsAsync(db, sql, ct, ("@src", source))).Where(r => r[1] is not null && parentKey.ContainsKey((string)r[1]!)).Select(r => ((string)r[0]!, parentKey[(string)r[1]!])).ToList();
        }

        bool Stays(string table, string key) => left.TryGetValue(table, out var keys) && keys.ContainsKey(key);
        for (var changed = true; changed;)
        {
            changed = false;
            foreach (var (edge, pairs) in references)
                foreach (var (child, parent) in pairs)
                    if (Stays(edge.Parent, parent) && !Stays(edge.Child, child))
                    {
                        if (!left.TryGetValue(edge.Child, out var keys)) left[edge.Child] = keys = new Dictionary<string, string>(StringComparer.Ordinal);
                        keys[child] = $"it depends on a {edge.Parent} record that stays";
                        changed = true;
                    }
        }
        foreach (var (edge, pairs) in references)
            foreach (var (child, parent) in pairs)
                if (Stays(edge.Child, child) && owned[edge.Parent].Contains(parent) && !Stays(edge.Parent, parent))
                    blockers.Add(new MergeBlocker("shared_record", $"A {edge.Child} record that must stay with the retired patient is linked to a {edge.Parent} record that would move. Resolve it before merging."));
    }
}
