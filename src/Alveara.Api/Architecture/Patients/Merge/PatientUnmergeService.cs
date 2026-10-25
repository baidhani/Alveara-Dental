using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>
/// ALV-017-C01: undoing a merge, where it is practical and safe. It moves back exactly the rows the merge's ledger names (and nothing else), restores a field value only if nobody has edited it since, makes the retired
/// record active again and writes its own lineage, history and audit entries (user and time); the original merge's lineage stays as it was. Everything happens in ONE transaction. It REFUSES, changing nothing, when:
/// the merge was made before ledgers existed (<c>unmerge_unavailable</c>), was already undone (<c>already_unmerged</c>), no longer holds (<c>merge_not_held</c>), the surviving record was itself merged into another
/// (<c>survivor_merged</c>: undo that merge first), or records made after the merge now depend on rows that would move back (<c>unmerge_blocked</c>, found by moving the rows and checking that every record still agrees with the
/// one it points at, inside the same transaction). <see cref="PreviewAsync"/> does exactly that work and then rolls it back, so what it promises is what an undo does. A repeat with the same key is the undo already made.
/// Not restored, by design: a guarantor link from the surviving record to the retired one, which the merge cleared; and any field edited since. Who may undo is decided by the permission on the controller.
/// </summary>
public class PatientUnmergeService(AlveraDbContext db, IPracticeClock clock)
{
    private const string MergeFlag = PatientOwnedData.MergeFlag;
    private const string UnmergeFlag = "alveara.patient_unmerge";

    private sealed record Plan(PatientMerge Merge, Patient Source, Patient Survivor, List<MergeMoveCount> Moves, List<MergeBlocker> Blockers);

    // ---------- preview ----------

    public async Task<UnmergePreview> PreviewAsync(Guid mergeId, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);                 // never committed: the rows are moved to find out, and put back by the rollback
        var plan = await PlanAsync(mergeId, ct);
        var (restore, kept) = await FieldDecisionsAsync(plan, ct);
        return new UnmergePreview(mergeId, PatientMergeService.BriefOf(plan.Source), PatientMergeService.BriefOf(plan.Survivor), plan.Blockers.Count == 0, plan.Blockers, plan.Moves, restore, kept,
            Convert.ToBase64String(plan.Source.RowVersion), Convert.ToBase64String(plan.Survivor.RowVersion));
    }

    // ---------- undo ----------

    public async Task<UnmergeResult> UnmergeAsync(Guid actor, UnmergeRequest r, string? idempotencyKey, CancellationToken ct)
    {
        var key = PatientMergeService.CleanKey(idempotencyKey);
        var reason = PatientMergeService.CleanReason(r.Reason);
        var mergeId = r.MergeId ?? throw new PatientException("merge_required", "Choose the merge to undo.", 400);
        if (await ReplayAsync(key, mergeId, ct) is { } replay) return replay;
        if (r.Confirmed != true) throw new PatientException("confirmation_required", "Undoing a merge needs the administrator's explicit confirmation. Nothing was changed.", 400);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var plan = await PlanAsync(mergeId, ct);
        if (await ReplayAsync(key, mergeId, ct) is { } raced) return raced;
        PatientMergeService.RequireVersionOf(plan.Source, r.SourceRowVersion, "retired"); PatientMergeService.RequireVersionOf(plan.Survivor, r.SurvivorRowVersion, "surviving");
        if (plan.Blockers.Count > 0) throw new PatientException("unmerge_blocked", string.Join(" ", plan.Blockers.Select(b => b.Message).Distinct()), 409);

        var (restore, kept) = await FieldDecisionsAsync(plan, ct);
        db.ChangeTracker.Clear();                                                                    // the raw moves changed rows the context may hold
        var now = clock.UtcNow;
        var survivor = await db.Patients.SingleAsync(p => p.Id == plan.Survivor.Id, ct);
        var source = await db.Patients.SingleAsync(p => p.Id == plan.Source.Id, ct);
        var rows = plan.Moves.Sum(m => m.Rows);
        var unmerge = new PatientUnmerge { Id = Guid.NewGuid(), MergeId = mergeId, UnmergeKey = key, UnmergedByUserId = actor, UnmergedAtUtc = now, Reason = reason, RowsMovedBack = rows };

        var restored = new List<string>();
        foreach (var f in PatientMergeFields.All.Where(f => restore.Contains(f.Name)))
        {
            var resolution = await db.PatientMergeResolutions.AsNoTracking().SingleAsync(x => x.MergeId == mergeId && x.Kind == PatientMergeResolutionKinds.Field && x.Subject == f.Name, ct);
            var current = PatientMergeFields.Clean(f.Get(survivor));
            if (resolution.ReplacedValue is { } before) f.Set(survivor, before); else f.Clear?.Invoke(survivor);
            PatientWrite.History(db, survivor.Id, now, actor, "Updated", f.Name, current, resolution.ReplacedValue);
            restored.Add(f.Name);
        }
        if (restore.Contains("Household"))
        {
            survivor.HouseholdId = null; survivor.HouseholdRelationship = null;
            PatientWrite.History(db, survivor.Id, now, actor, "HouseholdChanged", "HouseholdId", source.HouseholdId?.ToString(), null);
            restored.Add("Household");
        }
        PatientWrite.Touch(survivor, now, actor);
        db.PatientUnmerges.Add(unmerge);
        var detail = $"Patient merge undone: {rows} rows moved back to the record that was merged away; field values restored: {(restored.Count == 0 ? "none" : string.Join(", ", restored))}; kept because they were edited since: {(kept.Count == 0 ? "none" : string.Join(", ", kept))}.";
        AuditService.Record(db, AuditEventTypes.PatientUnmerged, nameof(Patient), survivor.Id, actor, detail + " This record is the surviving one.", reason, unmerge.Id);
        await SaveAsync(ct);                                                                         // the undo record first: the database lets a merged record come back only through it

        await db.Database.ExecuteSqlRawAsync($"EXEC sp_set_session_context N'{UnmergeFlag}', 1", ct);
        source.IsActive = true; source.MergedAtUtc = null; PatientWrite.Touch(source, now, actor);
        await SaveAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"EXEC sp_set_session_context N'{UnmergeFlag}', NULL", ct);
        PatientWrite.History(db, source.Id, now, actor, "Activated", "IsActive", "False", "True");     // after the record is active again: a merged-away record takes no new records
        AuditService.Record(db, AuditEventTypes.PatientUnmerged, nameof(Patient), source.Id, actor, detail + " This record is the one that was merged away and is active again.", reason, unmerge.Id);
        await SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new UnmergeResult(unmerge.Id, mergeId, source.Id, survivor.Id, actor, now, reason, rows, restored, kept, false);
    }

    // ---------- the work both share ----------

    /// <summary>Checks the merge can be undone at all, then moves its ledger's rows back (inside the caller's transaction) and checks that every record still agrees with the one it points at; what stops it is returned, not thrown.</summary>
    private async Task<Plan> PlanAsync(Guid mergeId, CancellationToken ct)
    {
        var merge = await db.PatientMerges.AsNoTracking().SingleOrDefaultAsync(m => m.Id == mergeId, ct) ?? throw new PatientException("merge_not_found", "That merge was not found.", 404);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM [Patients] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] IN (@a, @b)", [new SqlParameter("@a", merge.SourcePatientId), new SqlParameter("@b", merge.SurvivorPatientId)], ct);
        if (await db.PatientUnmerges.AsNoTracking().AnyAsync(u => u.MergeId == mergeId, ct)) throw new PatientException("already_unmerged", "That merge was already undone.", 409);
        if (!merge.HasLedger) throw new PatientException("unmerge_unavailable", "That merge was made before merges kept a ledger of the rows they moved, so it cannot be undone here. Recovery is by restoring a backup.", 409);
        var source = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == merge.SourcePatientId, ct);
        var survivor = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == merge.SurvivorPatientId, ct);
        if (source.MergedAtUtc is null || source.MergedAtUtc != merge.MergedAtUtc) throw new PatientException("merge_not_held", "That merge no longer holds.", 409);
        if (survivor.MergedAtUtc is not null) throw new PatientException("survivor_merged", "The surviving record was itself merged into another record. Undo that merge first.", 409);

        var catalog = new PatientMergeCatalog(db.Model);
        var groups = await PatientMergeCatalog.RowsAsync(db, "SELECT [TableName], [ColumnName], COUNT(*) FROM [PatientMergeMovedRows] WHERE [MergeId] = @m GROUP BY [TableName], [ColumnName]", ct, ("@m", mergeId));
        var expected = groups.ToDictionary(g => ((string)g[0]!, (string)g[1]!), g => Convert.ToInt32(g[2]));
        var moves = new List<MergeMoveCount>(); var blockers = new List<MergeBlocker>();

        await db.Database.ExecuteSqlRawAsync($"EXEC sp_set_session_context N'{MergeFlag}', 1", ct);
        foreach (var column in PatientOwnedData.All)
        {
            if (!expected.TryGetValue((column.Table, column.Column), out var count)) continue;
            var table = PatientMergeCatalog.Q(column.Table); var name = PatientMergeCatalog.Q(column.Column);
            var where = $"t.{name} = @surv AND EXISTS (SELECT 1 FROM [PatientMergeMovedRows] l WHERE l.[MergeId] = @mid AND l.[TableName] = @tn AND l.[ColumnName] = @cn AND l.[RowKey] = {catalog.KeyExpr(column.Table, "t")})";
            var parameters = new[] { ("@surv", (object)survivor.Id), ("@mid", mergeId), ("@tn", column.Table), ("@cn", column.Column) };
            var present = await PatientMergeCatalog.CountAsync(db, $"SELECT COUNT(*) FROM {table} t WHERE {where}", ct, parameters);
            if (present != count) throw new PatientException("unmerge_inconsistent", $"{column.Table} does not hold the {count} rows the merge moved (it holds {present}). Nothing was undone.", 500);
            await db.Database.ExecuteSqlRawAsync($"UPDATE t SET {name} = @src FROM {table} t WHERE {where}", [new SqlParameter("@src", source.Id), .. parameters.Select(p => new SqlParameter(p.Item1, p.Item2))], ct);
            moves.Add(new MergeMoveCount(column.Table, column.Column, count));
        }
        await db.Database.ExecuteSqlRawAsync($"EXEC sp_set_session_context N'{MergeFlag}', NULL", ct);

        foreach (var edge in catalog.Edges)                                                         // newer records that point at rows that moved back would now belong to another patient than the rows they point at
        {
            var join = string.Join(" AND ", edge.ChildColumns.Zip(edge.ParentColumns, (c, p) => $"c.{PatientMergeCatalog.Q(c)} = p.{PatientMergeCatalog.Q(p)}"));
            var wrong = await PatientMergeCatalog.CountAsync(db, $"SELECT COUNT(*) FROM {PatientMergeCatalog.Q(edge.Child)} c JOIN {PatientMergeCatalog.Q(edge.Parent)} p ON {join} WHERE c.[PatientId] <> p.[PatientId] AND (c.[PatientId] IN (@a, @b) OR p.[PatientId] IN (@a, @b))", ct, ("@a", source.Id), ("@b", survivor.Id));
            if (wrong > 0) blockers.Add(new MergeBlocker("newer_records", $"{wrong} {edge.Child} record(s) made after the merge depend on {edge.Parent} rows that would move back. Resolve or remove them first."));
        }
        return new Plan(merge, source, survivor, moves, blockers);
    }

    /// <summary>Which field values an undo puts back and which it keeps because someone edited them since: a value is restored only while the surviving record still holds exactly what the merge put there.</summary>
    private async Task<(List<string> Restore, List<string> Kept)> FieldDecisionsAsync(Plan plan, CancellationToken ct)
    {
        var resolutions = await db.PatientMergeResolutions.AsNoTracking().Where(x => x.MergeId == plan.Merge.Id && x.Kind == PatientMergeResolutionKinds.Field && x.Choice == PatientMergeChoices.TookSource).ToListAsync(ct);
        var restore = new List<string>(); var kept = new List<string>();
        foreach (var x in resolutions)
        {
            if (x.Subject == "Household")
            {
                if (plan.Survivor.HouseholdId is not null && plan.Survivor.HouseholdId == plan.Source.HouseholdId) restore.Add("Household"); else kept.Add("Household");
                continue;
            }
            var field = PatientMergeFields.All.FirstOrDefault(f => f.Name == x.Subject);
            if (field is null) continue;
            var untouched = PatientMergeFields.Same(field, PatientMergeFields.Clean(field.Get(plan.Survivor)) ?? "", PatientMergeFields.Clean(field.Get(plan.Source)) ?? "");
            var canPutBack = x.ReplacedValue is not null || field.Clear is not null;
            if (untouched && canPutBack) restore.Add(x.Subject); else kept.Add(x.Subject);
        }
        return (restore, kept);
    }

    private async Task<UnmergeResult?> ReplayAsync(string key, Guid mergeId, CancellationToken ct)
    {
        var existing = await db.PatientUnmerges.AsNoTracking().SingleOrDefaultAsync(u => u.UnmergeKey == key, ct);
        if (existing is null) return null;
        if (existing.MergeId != mergeId) throw new PatientException("key_reused", "This Idempotency-Key was already used for a different undo.", 409);
        var merge = await db.PatientMerges.AsNoTracking().SingleAsync(m => m.Id == mergeId, ct);
        return new UnmergeResult(existing.Id, mergeId, merge.SourcePatientId, merge.SurvivorPatientId, existing.UnmergedByUserId, existing.UnmergedAtUtc, existing.Reason, existing.RowsMovedBack, [], [], true);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PatientException("patient_changed", "A patient record changed while the merge was being undone. Nothing was undone; review it and try again.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new PatientException("unmerge_conflict", "This merge was undone by another request at the same moment. Nothing was changed by this one.", 409);
        }
    }
}
