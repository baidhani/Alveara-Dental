using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Patients.Merge;

/// <summary>
/// STORY-017: the controlled merge of two patient records. <see cref="PreviewAsync"/> shows what a merge would do and changes nothing; <see cref="MergeAsync"/> does it in ONE transaction or not at all: it moves every
/// row that holds the retired patient's id to the surviving patient (<see cref="PatientOwnedData"/> lists them; the database lets that one change through only while the merge's session value is set, and only for the patient id),
/// applies the administrator's answer to each field conflict, keeps rows that cannot move with the retired patient, checks that every moved row still agrees with the row it points at, writes the lineage, the history
/// and the audit entries (who and when), and marks the retired patient merged. Any failure, the audit write included, rolls the whole thing back. The retired patient is never deleted. A repeat of the same request (the
/// same Idempotency-Key) answers with the merge already made; the same key for a different pair is <c>key_reused</c>. Failure modes handled: a changed patient (<c>patient_changed</c>), a patient already merged
/// (<c>already_merged</c>, <c>survivor_merged</c>), an unanswered conflict (<c>choice_required</c>), a merge the data cannot support (<c>merge_blocked</c>), a move that left a row disagreeing with its parent
/// (<c>merge_inconsistent</c>, rolled back). Not handled: a retry after a crash between commit and response is answered by the key, not by this code. Who may merge is decided by the permission on the controller.
/// </summary>
public class PatientMergeService(AlveraDbContext db, IPracticeClock clock)
{
    private const string Flag = PatientOwnedData.MergeFlag;
    private const int InsertChunk = 400;

    // ---------- preview ----------

    public async Task<MergePreview> PreviewAsync(Guid sourceId, Guid survivorId, CancellationToken ct)
    {
        var (source, survivor) = await LoadPairAsync(sourceId, survivorId, tracking: false, ct);
        var plan = await new PatientMergePlanner(db, new PatientMergeCatalog(db.Model)).PlanAsync(source, survivor, ct);
        var left = plan.Left.SelectMany(t => t.Value.Select(k => new MergeLeftBehind(t.Key, k.Key, k.Value))).OrderBy(l => l.Table).ThenBy(l => l.RowKey).ToList();
        return new MergePreview(Brief(source), Brief(survivor), plan.Conflicts, plan.AutoFills, plan.Moves, left, Distinct(plan.Blockers), plan.Blockers.Count == 0,
            Convert.ToBase64String(source.RowVersion), Convert.ToBase64String(survivor.RowVersion), IdentityConflicts(source, survivor));
    }

    /// <summary>The ways two records that are about to be merged look like different people: a different date of birth, a different last name, or a first name more than two typos apart (a nickname). A genuine duplicate has none; any is a conflict of identity the administrator must confirm on purpose.</summary>
    public static IReadOnlyList<string> IdentityConflicts(Patient a, Patient b)
    {
        var conflicts = new List<string>();
        if (a.DateOfBirth != b.DateOfBirth) conflicts.Add("date of birth");
        if (Patient.NormalizeName(a.LastName) != Patient.NormalizeName(b.LastName)) conflicts.Add("last name");
        if (PatientDuplicateDetector.EditDistance(Patient.NormalizeName(a.FirstName), Patient.NormalizeName(b.FirstName)) > 2) conflicts.Add("first name");
        return conflicts;
    }

    // ---------- a patient's own lineage ----------

    /// <summary>Where this record stands in the merge lineage: merged into another (retired), and/or the survivor of earlier merges. Read-only.</summary>
    public async Task<PatientMergeHistory> HistoryAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PatientException("patient_not_found", "The patient was not found.", 404);
        var merges = await db.PatientMerges.AsNoTracking().Where(m => m.SourcePatientId == patientId || m.SurvivorPatientId == patientId).OrderByDescending(m => m.MergedAtUtc).ThenBy(m => m.Id).ToListAsync(ct);
        var ids = merges.SelectMany(m => new[] { m.SourcePatientId, m.SurvivorPatientId }).Where(i => i != patientId).Distinct().ToList();
        var people = await db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => PatientMergeViewsName(p), ct);
        var userIds = merges.Select(m => m.MergedByUserId).Distinct().ToList();
        var users = await db.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        MergeLinkView Link(PatientMerge m, Guid other) => new(m.Id, other, people[other], m.MergedAtUtc, m.Reason, users.GetValueOrDefault(m.MergedByUserId, ""));
        var into = merges.FirstOrDefault(m => m.SourcePatientId == patientId);
        return new PatientMergeHistory(into is null ? null : Link(into, into.SurvivorPatientId), merges.Where(m => m.SurvivorPatientId == patientId).Select(m => Link(m, m.SourcePatientId)).ToList());
    }

    private static string PatientMergeViewsName(Patient p) => string.Join(" ", new[] { p.FirstName, p.MiddleName, p.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

    // ---------- the log ----------

    /// <summary>The most recent merges, newest first, for the administrator's review: who, when, why, between which records, and how much moved or stayed.</summary>
    public async Task<IReadOnlyList<MergeLogEntry>> LogAsync(int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, 200);
        var merges = await db.PatientMerges.AsNoTracking().OrderByDescending(m => m.MergedAtUtc).ThenBy(m => m.Id).Take(take).ToListAsync(ct);
        var ids = merges.Select(m => m.Id).ToList();
        var moved = await db.PatientMergeMoves.AsNoTracking().Where(m => ids.Contains(m.MergeId)).GroupBy(m => m.MergeId).Select(g => new { g.Key, Rows = g.Sum(m => m.RowsMoved) }).ToDictionaryAsync(x => x.Key, x => x.Rows, ct);
        var left = await db.PatientMergeResolutions.AsNoTracking().Where(x => ids.Contains(x.MergeId) && x.Kind == PatientMergeResolutionKinds.Record).GroupBy(x => x.MergeId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var patientIds = merges.SelectMany(m => new[] { m.SourcePatientId, m.SurvivorPatientId }).Distinct().ToList();
        var patients = await db.Patients.AsNoTracking().Where(p => patientIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var userIds = merges.Select(m => m.MergedByUserId).Distinct().ToList();
        var users = await db.UserAccounts.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);
        return merges.Select(m => new MergeLogEntry(m.Id, Brief(patients[m.SourcePatientId]), Brief(patients[m.SurvivorPatientId]), m.MergedByUserId, users.GetValueOrDefault(m.MergedByUserId, ""), m.MergedAtUtc, m.Reason,
            moved.GetValueOrDefault(m.Id), left.GetValueOrDefault(m.Id))).ToList();
    }

    // ---------- merge ----------

    public async Task<MergeResult> MergeAsync(Guid actor, MergeRequest r, string? idempotencyKey, CancellationToken ct)
    {
        var key = Key(idempotencyKey);
        var reason = Reason(r.Reason);
        var sourceId = r.SourcePatientId ?? throw new PatientException("source_required", "Choose the record to merge away.", 400);
        var survivorId = r.SurvivorPatientId ?? throw new PatientException("survivor_required", "Choose the record to keep.", 400);

        if (await ReplayAsync(key, sourceId, survivorId, ct) is { } replay) return replay;
        if (r.Confirmed != true) throw new PatientException("confirmation_required", "A merge needs the administrator's explicit confirmation. Nothing was merged.", 400);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT 1 FROM [Patients] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] IN (@a, @b)", [new SqlParameter("@a", sourceId), new SqlParameter("@b", survivorId)], ct);
        if (await ReplayAsync(key, sourceId, survivorId, ct) is { } raced) return raced;                         // the same request got in first while this one waited for the lock

        var (source, survivor) = await LoadPairAsync(sourceId, survivorId, tracking: false, ct);
        RequireVersion(source, r.SourceRowVersion, "source"); RequireVersion(survivor, r.SurvivorRowVersion, "survivor");
        var identity = IdentityConflicts(source, survivor);
        if (identity.Count > 0 && r.ConfirmIdentityDifference != true)
            throw new PatientException("identity_conflict", $"These records differ in {string.Join(" and ", identity)}, so they may not be the same person. Confirm that they are the same person to merge them. Nothing was merged.", 409);

        var catalog = new PatientMergeCatalog(db.Model);
        var plan = await new PatientMergePlanner(db, catalog).PlanAsync(source, survivor, ct);
        if (plan.Blockers.Count > 0) throw new PatientException("merge_blocked", string.Join(" ", Distinct(plan.Blockers).Select(b => b.Message)), 409);
        var decisions = Decide(plan, r.FieldChoices);

        await MoveAsync(catalog, plan, source.Id, survivor.Id, ct);
        var moves = plan.Moves;                                                                                     // counted before, by SELECT: the rows-affected of an UPDATE is not reliable through INSTEAD OF triggers
        await VerifyAsync(catalog, plan, source.Id, survivor.Id, ct);

        db.ChangeTracker.Clear();                                                                                   // the raw moves changed rows the context may hold
        var now = clock.UtcNow;
        var mergeId = Guid.NewGuid();
        var trackedSource = await db.Patients.SingleAsync(p => p.Id == source.Id, ct);
        var trackedSurvivor = await db.Patients.SingleAsync(p => p.Id == survivor.Id, ct);
        var resolutions = Apply(trackedSource, trackedSurvivor, decisions, plan, now, actor);

        PatientWrite.History(db, source.Id, now, actor, "Inactivated", "IsActive", "True", "False");           // before the retired record is marked: the database takes no new record for a merged-away patient
        db.PatientMerges.Add(new PatientMerge { Id = mergeId, MergeKey = key, SourcePatientId = source.Id, SurvivorPatientId = survivor.Id, MergedByUserId = actor, MergedAtUtc = now, Reason = reason });
        await SaveAsync(ct);                                                                                         // the lineage first: the database marks a patient merged only when it exists
        db.PatientMergeMoves.AddRange(moves.Select(m => new PatientMergeMove { MergeId = mergeId, TableName = m.Table, ColumnName = m.Column, RowsMoved = m.Rows }));
        db.PatientMergeResolutions.AddRange(resolutions.Select(x => new PatientMergeResolution { Id = Guid.NewGuid(), MergeId = mergeId, Kind = x.Kind, Subject = x.Subject, Choice = x.Choice, ReplacedValue = x.ReplacedValue }));
        trackedSource.IsActive = false; trackedSource.MergedAtUtc = now; PatientWrite.Touch(trackedSource, now, actor);
        var detail = $"Patient record merged: {moves.Sum(m => m.Rows)} rows moved to the surviving record, {plan.LeftCount} left with the retired record, {resolutions.Count(x => x.Kind == PatientMergeResolutionKinds.Field)} field decisions{Decisions(resolutions)}.{(identity.Count > 0 ? $" Identity differences confirmed on purpose: {string.Join(", ", identity)}." : "")}";
        AuditService.Record(db, AuditEventTypes.PatientMerged, nameof(Patient), source.Id, actor, detail + " This record is the retired one.", reason, mergeId);
        AuditService.Record(db, AuditEventTypes.PatientMerged, nameof(Patient), survivor.Id, actor, detail + " This record is the surviving one.", reason, mergeId);
        await SaveAsync(ct);
        await transaction.CommitAsync(ct);
        return new MergeResult(mergeId, source.Id, survivor.Id, actor, now, reason, moves, resolutions.Select(x => new MergeResolutionView(x.Kind, x.Subject, x.Choice, x.ReplacedValue)).ToList(), false);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new PatientException("patient_changed", "A patient record changed while it was being merged. Nothing was merged; review both records and try again.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new PatientException("merge_conflict", "These records were merged by another request at the same moment. Nothing was merged by this one.", 409);
        }
    }

    // ---------- the moves ----------

    private async Task MoveAsync(PatientMergeCatalog catalog, MergePlan plan, Guid source, Guid survivor, CancellationToken ct)
    {
        await db.Database.ExecuteSqlRawAsync("CREATE TABLE #MergeLeft ([Tbl] nvarchar(100) NOT NULL, [RowKey] nvarchar(400) NOT NULL, PRIMARY KEY ([Tbl], [RowKey]))", ct);
        var stay = plan.Left.SelectMany(t => t.Value.Keys.Select(k => (Table: t.Key, Key: k))).ToList();
        for (var i = 0; i < stay.Count; i += InsertChunk)
        {
            var chunk = stay.Skip(i).Take(InsertChunk).ToList();
            var values = string.Join(", ", chunk.Select((_, n) => $"(@t{n}, @k{n})"));
            var parameters = chunk.SelectMany((c, n) => new object[] { new SqlParameter($"@t{n}", c.Table), new SqlParameter($"@k{n}", c.Key) }).ToArray();
            await db.Database.ExecuteSqlRawAsync($"INSERT INTO #MergeLeft ([Tbl], [RowKey]) VALUES {values}", parameters, ct);
        }

        await db.Database.ExecuteSqlRawAsync($"EXEC sp_set_session_context N'{Flag}', 1", ct);
        // a survivor whose guarantor is the retired record would become its own guarantor
        await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [GuarantorPatientId] = NULL WHERE [Id] = @surv AND [GuarantorPatientId] = @src", [new SqlParameter("@surv", survivor), new SqlParameter("@src", source)], ct);
        foreach (var column in PatientOwnedData.All)
        {
            var table = PatientMergeCatalog.Q(column.Table); var name = PatientMergeCatalog.Q(column.Column);
            var exclude = plan.Left.ContainsKey(column.Table) ? $" AND NOT EXISTS (SELECT 1 FROM #MergeLeft l WHERE l.[Tbl] = @tn AND l.[RowKey] = {catalog.KeyExpr(column.Table, "t")})" : "";
            await db.Database.ExecuteSqlRawAsync($"UPDATE t SET {name} = @surv FROM {table} t WHERE t.{name} = @src{exclude}",
                [new SqlParameter("@surv", survivor), new SqlParameter("@src", source), new SqlParameter("@tn", column.Table)], ct);
        }
        await db.Database.ExecuteSqlRawAsync($"EXEC sp_set_session_context N'{Flag}', NULL", ct);
        await db.Database.ExecuteSqlRawAsync("DROP TABLE #MergeLeft", ct);
    }

    /// <summary>Nothing of the retired patient is left but what was meant to stay, and every row that points at another row of the same chart still agrees with it about whose chart it is.</summary>
    internal async Task VerifyAsync(PatientMergeCatalog catalog, MergePlan plan, Guid source, Guid survivor, CancellationToken ct)
    {
        foreach (var table in PatientOwnedData.Owned.Select(c => c.Table).Distinct())
        {
            var remaining = await PatientMergeCatalog.CountAsync(db, $"SELECT COUNT(*) FROM {PatientMergeCatalog.Q(table)} WHERE [PatientId] = @src", ct, ("@src", source));
            var expected = plan.Left.TryGetValue(table, out var stay) ? stay.Count : 0;
            if (remaining != expected) throw new PatientException("merge_inconsistent", $"{table} still holds {remaining} rows of the retired patient where {expected} were expected. Nothing was merged.", 500);
        }
        foreach (var edge in catalog.Edges)
        {
            var join = string.Join(" AND ", edge.ChildColumns.Zip(edge.ParentColumns, (c, p) => $"c.{PatientMergeCatalog.Q(c)} = p.{PatientMergeCatalog.Q(p)}"));
            var sql = $"SELECT COUNT(*) FROM {PatientMergeCatalog.Q(edge.Child)} c JOIN {PatientMergeCatalog.Q(edge.Parent)} p ON {join} " +
                      "WHERE c.[PatientId] <> p.[PatientId] AND (c.[PatientId] IN (@src, @surv) OR p.[PatientId] IN (@src, @surv))";
            var wrong = await PatientMergeCatalog.CountAsync(db, sql, ct, ("@src", source), ("@surv", survivor));
            if (wrong > 0) throw new PatientException("merge_inconsistent", $"{wrong} {edge.Child} rows would disagree with the {edge.Parent} rows they point at. Nothing was merged.", 500);
        }
    }

    // ---------- the answers and what they change on the surviving record ----------

    private sealed record Decision(string Field, bool TakeSource);

    private static Dictionary<string, Decision> Decide(MergePlan plan, IReadOnlyDictionary<string, string>? choices)
    {
        choices ??= new Dictionary<string, string>();
        var known = PatientMergeFields.All.Select(f => f.Name).ToHashSet();
        var errors = new Dictionary<string, string>();
        var invalid = false;
        foreach (var (field, choice) in choices)
            if (!known.Contains(field) || choice is not ("Survivor" or "Source")) { errors[field] = "Choose Survivor or Source for a field that can be reconciled."; invalid = true; }
        foreach (var c in plan.Conflicts)
            if (!choices.ContainsKey(c.Field)) errors[c.Field] = "Choose which value the surviving record keeps.";
        if (errors.Count > 0)
            throw new PatientException(invalid ? "choice_invalid" : "choice_required", "Every field that differs needs an answer: keep the surviving record's value or take the retired record's.", 400, errors);
        return plan.Conflicts.ToDictionary(c => c.Field, c => new Decision(c.Field, choices[c.Field] == "Source"));
    }

    private sealed record Resolution(string Kind, string Subject, string Choice, string? ReplacedValue);

    private List<Resolution> Apply(Patient source, Patient survivor, Dictionary<string, Decision> decisions, MergePlan plan, DateTimeOffset now, Guid actor)
    {
        var resolutions = new List<Resolution>();
        foreach (var f in PatientMergeFields.All)
        {
            var fromSource = PatientMergeFields.Clean(f.Get(source)); var current = PatientMergeFields.Clean(f.Get(survivor));
            if (fromSource is null) continue;
            if (current is null) { f.Set(survivor, fromSource); resolutions.Add(new Resolution(PatientMergeResolutionKinds.Field, f.Name, PatientMergeChoices.TookSource, null)); PatientWrite.History(db, survivor.Id, now, actor, "Updated", f.Name, null, fromSource); }
            else if (!PatientMergeFields.Same(f, fromSource, current))
            {
                if (decisions[f.Name].TakeSource)
                {
                    f.Set(survivor, fromSource); resolutions.Add(new Resolution(PatientMergeResolutionKinds.Field, f.Name, PatientMergeChoices.TookSource, Clip(current)));
                    PatientWrite.History(db, survivor.Id, now, actor, "Updated", f.Name, current, fromSource);
                }
                else resolutions.Add(new Resolution(PatientMergeResolutionKinds.Field, f.Name, PatientMergeChoices.KeptSurvivor, null));
            }
        }
        if (survivor.HouseholdId is null && source.HouseholdId is { } household)                                       // the household is carried over when the surviving record has none
        {
            survivor.HouseholdId = household; survivor.HouseholdRelationship = source.HouseholdRelationship;
            resolutions.Add(new Resolution(PatientMergeResolutionKinds.Field, "Household", PatientMergeChoices.TookSource, null));
            PatientWrite.History(db, survivor.Id, now, actor, "HouseholdChanged", "HouseholdId", null, household.ToString());
        }
        foreach (var (table, keys) in plan.Left.OrderBy(t => t.Key, StringComparer.Ordinal))
            foreach (var key in keys.Keys.Order(StringComparer.Ordinal))
                resolutions.Add(new Resolution(PatientMergeResolutionKinds.Record, Clip($"{table}:{key}", 200)!, PatientMergeChoices.LeftWithSource, null));
        PatientWrite.Touch(survivor, now, actor);
        return resolutions;
    }

    // ---------- checks and reads ----------

    private async Task<(Patient Source, Patient Survivor)> LoadPairAsync(Guid sourceId, Guid survivorId, bool tracking, CancellationToken ct)
    {
        if (sourceId == survivorId) throw new PatientException("same_patient", "A record cannot be merged into itself.", 400);
        var query = tracking ? db.Patients : db.Patients.AsNoTracking();
        var pair = await query.Where(p => p.Id == sourceId || p.Id == survivorId).ToListAsync(ct);
        var source = pair.SingleOrDefault(p => p.Id == sourceId) ?? throw new PatientException("patient_not_found", "The record to merge away was not found.", 404);
        var survivor = pair.SingleOrDefault(p => p.Id == survivorId) ?? throw new PatientException("patient_not_found", "The record to keep was not found.", 404);
        if (source.MergedAtUtc is not null) throw new PatientException("already_merged", "The record to merge away has already been merged into another record.", 409);
        if (survivor.MergedAtUtc is not null) throw new PatientException("survivor_merged", "The record to keep has itself been merged into another record. Keep that one instead.", 409);
        return (source, survivor);
    }

    private static void RequireVersion(Patient patient, string? supplied, string which)
    {
        if (string.IsNullOrWhiteSpace(supplied))
            throw new PatientException("row_version_required", "The versions of both records you reviewed are required, so a change made meanwhile is never merged over. Reload and try again.", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(supplied); }
        catch (FormatException) { throw new PatientException("row_version_invalid", "A supplied version is not valid. Reload and try again.", 400); }
        if (!expected.AsSpan().SequenceEqual(patient.RowVersion))
            throw new PatientException("patient_changed", $"The {which} record changed after you reviewed it. Nothing was merged; review both records again.", 409);
    }

    private async Task<MergeResult?> ReplayAsync(string key, Guid sourceId, Guid survivorId, CancellationToken ct)
    {
        var existing = await db.PatientMerges.AsNoTracking().SingleOrDefaultAsync(m => m.MergeKey == key, ct);
        if (existing is null) return null;
        if (existing.SourcePatientId != sourceId || existing.SurvivorPatientId != survivorId)
            throw new PatientException("key_reused", "This Idempotency-Key was already used for a different merge.", 409);
        var moves = await db.PatientMergeMoves.AsNoTracking().Where(m => m.MergeId == existing.Id).OrderBy(m => m.TableName).ThenBy(m => m.ColumnName).ToListAsync(ct);
        var resolutions = await db.PatientMergeResolutions.AsNoTracking().Where(x => x.MergeId == existing.Id).OrderBy(x => x.Kind).ThenBy(x => x.Subject).ToListAsync(ct);
        return new MergeResult(existing.Id, existing.SourcePatientId, existing.SurvivorPatientId, existing.MergedByUserId, existing.MergedAtUtc, existing.Reason,
            moves.Select(m => new MergeMoveCount(m.TableName, m.ColumnName, m.RowsMoved)).ToList(), resolutions.Select(x => new MergeResolutionView(x.Kind, x.Subject, x.Choice, x.ReplacedValue)).ToList(), true);
    }

    private static string Key(string? key)
    {
        var k = key?.Trim() ?? "";
        if (k.Length is 0 or > 100 || k.Any(char.IsControl)) throw new PatientException("idempotency_key_invalid", "Send an Idempotency-Key of up to 100 characters on one line, so a repeat of this request is the same merge.", 400);
        return k;
    }

    /// <summary>A reason as the merge and the dismissal both need it: one line, 1 to 500 characters.</summary>
    public static string CleanReason(string? reason) => Reason(reason);

    public static PatientBrief BriefOf(Patient p) => Brief(p);

    private static string Reason(string? reason)
    {
        var r = reason?.Trim() ?? "";
        if (r.Length is 0 or > 500 || r.Any(char.IsControl)) throw new PatientException("reason_invalid", "Write the reason for the merge (up to 500 characters) on one line.", 400);
        return r;
    }

    /// <summary>The fields that were decided and how, in words and without their values, for the audit text.</summary>
    private static string Decisions(IEnumerable<Resolution> resolutions)
    {
        var fields = resolutions.Where(x => x.Kind == PatientMergeResolutionKinds.Field).Select(x => $"{x.Subject} {(x.Choice == PatientMergeChoices.KeptSurvivor ? "kept" : "taken from the retired record")}").ToList();
        return fields.Count == 0 ? "" : " (" + string.Join("; ", fields) + ")";
    }

    private static string? Clip(string? value, int max = 500) => value is { } v && v.Length > max ? v[..max] : value;

    private static List<MergeBlocker> Distinct(IEnumerable<MergeBlocker> blockers) => blockers.DistinctBy(b => b.Message).ToList();

    private static PatientBrief Brief(Patient p) =>
        new(p.Id, p.FirstName, p.MiddleName, p.LastName, p.DateOfBirth.ToString("yyyy-MM-dd"), p.Phone, p.Email, p.City, p.State, p.IsActive, p.CreatedAtUtc, p.HouseholdId);
}
