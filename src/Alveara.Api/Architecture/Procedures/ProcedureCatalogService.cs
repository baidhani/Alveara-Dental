using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Procedures;

/// <summary>
/// ALV-N005: the procedure catalog treatment planning, completion and billing share (writes here; the reads for screens and for other modules are in <c>ProcedureCatalogService.Queries.cs</c>).
///
/// How the promises are kept:
/// - <b>One authoritative definition.</b> A procedure's Id, code system and code are its identity and never change (a database trigger refuses it); two people adding the same code at the same
///   moment end with one procedure.
/// - <b>History is never rewritten.</b> Anything else a person can change - description, category, where it applies, the fee, the source - appends a new immutable <see cref="ProcedureVersion"/>
///   that starts today or later, so a plan or charge that remembers the version it used keeps reading exactly that, and a fee change cannot alter what was already quoted.
/// - <b>Nothing is deleted.</b> A procedure is inactivated with a reason (after a warning when records still refer to it) and can be reactivated; inactivating never touches those records.
/// - <b>Every change is logged in the same save</b> - the row, its event (who, when, why) and a PHI-free audit entry - so a change cannot exist without its log entry.
/// - <b>Stale changes are refused</b> through the row version. <b>Repeats are quiet</b>: creating the identical procedure, revising to the same values, inactivating an inactive one change nothing.
/// </summary>
public partial class ProcedureCatalogService(AlveraDbContext db, IPracticeClock clock, IEnumerable<IProcedureUsageSource> usageSources)
{
    private DateOnly Today() => DateOnly.FromDateTime(clock.ToPracticeLocal(clock.UtcNow).DateTime);

    public async Task<ProcedureDetailView> CreateAsync(ProcedureInput input, Guid actor, CancellationToken ct)
    {
        var f = ProcedureRules.Validate(input, Today(), null);
        var twin = await FindByCodeAsync(f.CodeSystem, f.Code, ct);
        if (twin is not null) return await SameAsFirstVersionAsync(twin, f, ct) ? await GetAsync(twin.Id, null, ct) : throw Exists(f);

        var now = clock.UtcNow;
        var procedure = new ProcedureDefinition { Id = Guid.NewGuid(), CodeSystem = f.CodeSystem, Code = f.Code, IsActive = true, CurrentVersionNumber = 1, CreatedAtUtc = now, CreatedByUserId = actor };
        db.ProcedureDefinitions.Add(procedure);
        db.ProcedureVersions.Add(NewVersion(procedure.Id, 1, f, null, actor, now));
        StageEvent(procedure.Id, 1, ProcedureChanges.Created, 1, null, actor, now);
        AuditService.Record(db, "ProcedureCreated", nameof(ProcedureDefinition), procedure.Id, actor, $"Procedure {f.CodeSystem} {f.Code} added to the catalog (fee {f.Fee:F2} USD).");
        try
        {
            await SaveAsync(procedure.Id, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // the same code was added at the same moment (the unique key): the first one stands, so this is a retry of it when it is the same definition
            db.ChangeTracker.Clear();
            var winner = await FindByCodeAsync(f.CodeSystem, f.Code, ct);
            if (winner is not null && await SameAsFirstVersionAsync(winner, f, ct)) return await GetAsync(winner.Id, null, ct);
            throw Exists(f);
        }
        return await GetAsync(procedure.Id, null, ct);
    }

    /// <summary>
    /// Appends the next version: a change of description, category, where the procedure applies, its fee or its source. The code and code system cannot change. The new version starts today or
    /// later (never earlier than the version already in effect or scheduled), so nothing that was already in effect is rewritten. A reason is required; revising to the values already in
    /// force changes nothing.
    /// </summary>
    public async Task<ProcedureDetailView> ReviseAsync(Guid id, ProcedureInput input, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var procedure = await LoadAsync(id, rowVersion, ct);
        var versions = await db.ProcedureVersions.AsNoTracking().Where(v => v.ProcedureId == id).ToListAsync(ct);
        var latest = versions.OrderByDescending(v => v.VersionNumber).First();

        if (Differs(input.CodeSystem, procedure.CodeSystem) || Differs(input.Code?.ToUpperInvariant(), procedure.Code))
            throw new ProcedureCatalogException("identity_fixed", "A procedure's code and code system cannot be changed. Inactivate it and add the correct procedure instead.", 400,
                new Dictionary<string, string> { [input.Code is not null && Differs(input.Code.ToUpperInvariant(), procedure.Code) ? "code" : "codeSystem"] = "The code and code system are fixed once a procedure exists." });

        var today = Today();
        var earliest = latest.EffectiveFrom > today ? latest.EffectiveFrom : today;
        var f = ProcedureRules.Validate(input with { CodeSystem = procedure.CodeSystem, Code = procedure.Code }, today, earliest);
        if (SameContent(latest, f) && (input.EffectiveFrom is null || input.EffectiveFrom == latest.EffectiveFrom)) return await GetAsync(id, null, ct);

        var why = ProcedureRules.RequireReason(reason, "Say why this procedure is being changed.");
        var now = clock.UtcNow;
        var number = latest.VersionNumber + 1;
        db.ProcedureVersions.Add(NewVersion(id, number, f, why, actor, now));
        procedure.CurrentVersionNumber = number;
        Touch(procedure, actor, now);
        StageEvent(id, await NextEventAsync(id, ct), ProcedureChanges.Revised, number, why, actor, now);
        AuditService.Record(db, "ProcedureRevised", nameof(ProcedureDefinition), id, actor, $"Procedure {procedure.CodeSystem} {procedure.Code} revised to version {number}: {Changes(latest, f)}.", why);
        await SaveAsync(id, ct);
        return await GetAsync(id, null, ct);
    }

    /// <summary>
    /// Inactivates a procedure: it can no longer be chosen for new work, and everything that already refers to it keeps working. A reason is required. When records still refer to the procedure, the
    /// call is refused with the count until the caller confirms (<paramref name="acknowledgeUsage"/>), so nobody inactivates a procedure in use without having seen that. An inactive one stays inactive.
    /// </summary>
    public async Task<ProcedureDetailView> InactivateAsync(Guid id, string? reason, bool acknowledgeUsage, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = ProcedureRules.RequireReason(reason, "Say why this procedure is being inactivated.");
        var procedure = await LoadAsync(id, rowVersion, ct);
        if (!procedure.IsActive) return await GetAsync(id, null, ct);

        var usage = await UsageAsync(id, ct);
        if (usage.Count > 0 && !acknowledgeUsage)
            throw new ProcedureCatalogException("usage_confirmation_required",
                $"{usage.Count} record{(usage.Count == 1 ? " refers" : "s refer")} to this procedure. Inactivating it keeps {(usage.Count == 1 ? "that record" : "those records")} exactly as {(usage.Count == 1 ? "it is" : "they are")} and stops new work from using it. Confirm to continue.",
                409, null, usage);

        var now = clock.UtcNow;
        procedure.IsActive = false;
        Touch(procedure, actor, now);
        StageEvent(id, await NextEventAsync(id, ct), ProcedureChanges.Inactivated, null, why, actor, now);
        AuditService.Record(db, "ProcedureInactivated", nameof(ProcedureDefinition), id, actor, $"Procedure {procedure.CodeSystem} {procedure.Code} inactivated ({usage.Count} reference{(usage.Count == 1 ? "" : "s")} kept).", why);
        await SaveAsync(id, ct);
        return await GetAsync(id, null, ct);
    }

    /// <summary>Makes an inactive procedure available for new work again. A reason is optional; reactivating an active one changes nothing.</summary>
    public async Task<ProcedureDetailView> ReactivateAsync(Guid id, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = ProcedureRules.OptionalReason(reason);
        var procedure = await LoadAsync(id, rowVersion, ct);
        if (procedure.IsActive) return await GetAsync(id, null, ct);

        var now = clock.UtcNow;
        procedure.IsActive = true;
        Touch(procedure, actor, now);
        StageEvent(id, await NextEventAsync(id, ct), ProcedureChanges.Reactivated, null, why, actor, now);
        AuditService.Record(db, "ProcedureReactivated", nameof(ProcedureDefinition), id, actor, $"Procedure {procedure.CodeSystem} {procedure.Code} reactivated.", why);
        await SaveAsync(id, ct);
        return await GetAsync(id, null, ct);
    }

    // ---------- shared ----------

    private static bool Differs(string? given, string stored) => given is not null && !string.Equals(given.Trim(), stored, StringComparison.Ordinal);

    private static ProcedureCatalogException Exists(ProcedureFields f) =>
        new("procedure_exists", $"There is already a procedure with the code {f.Code} in the {f.CodeSystem} code system. Open it to change it, or use another code.", 409, new Dictionary<string, string> { ["code"] = "That code is already used in this code system." });

    private Task<ProcedureDefinition?> FindByCodeAsync(string system, string code, CancellationToken ct) =>
        db.ProcedureDefinitions.AsNoTracking().SingleOrDefaultAsync(p => p.CodeSystem == system && p.Code == code, ct);

    private async Task<bool> SameAsFirstVersionAsync(ProcedureDefinition procedure, ProcedureFields f, CancellationToken ct)
    {
        var first = await db.ProcedureVersions.AsNoTracking().SingleAsync(v => v.ProcedureId == procedure.Id && v.VersionNumber == 1, ct);
        return SameContent(first, f);
    }

    private static bool SameContent(ProcedureVersion v, ProcedureFields f) =>
        v.Description == f.Description && v.Category == f.Category && v.Scope == f.Scope && v.Dentition == f.Dentition && v.Fee == f.Fee
        && v.SourceName == f.SourceName && v.SourceVersion == f.SourceVersion && v.ValidThrough == f.ValidThrough;

    private static string Changes(ProcedureVersion before, ProcedureFields after)
    {
        var changed = new List<string>();
        if (before.Fee != after.Fee) changed.Add($"fee {before.Fee:F2} to {after.Fee:F2} USD");
        if (before.Description != after.Description) changed.Add("description");
        if (before.Category != after.Category) changed.Add("category");
        if (before.Scope != after.Scope || before.Dentition != after.Dentition) changed.Add("where it applies");
        if (before.SourceName != after.SourceName || before.SourceVersion != after.SourceVersion) changed.Add("source");
        if (before.ValidThrough != after.ValidThrough) changed.Add("last valid date");
        if (before.EffectiveFrom != after.EffectiveFrom) changed.Add($"start date {after.EffectiveFrom:yyyy-MM-dd}");
        return changed.Count == 0 ? "start date" : string.Join(", ", changed);
    }

    private static ProcedureVersion NewVersion(Guid procedureId, int number, ProcedureFields f, string? reason, Guid actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), ProcedureId = procedureId, VersionNumber = number, Description = f.Description, Category = f.Category, Scope = f.Scope, Dentition = f.Dentition, Fee = f.Fee,
        SourceName = f.SourceName, SourceVersion = f.SourceVersion, EffectiveFrom = f.EffectiveFrom, ValidThrough = f.ValidThrough, Reason = reason, CreatedByUserId = actor, CreatedAtUtc = now,
    };

    private async Task<ProcedureDefinition> LoadAsync(Guid id, string? rowVersion, CancellationToken ct)
    {
        var version = ProcedureRules.ParseVersion(rowVersion);
        var procedure = await db.ProcedureDefinitions.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        db.Entry(procedure).Property(p => p.RowVersion).OriginalValue = version;
        return procedure;
    }

    private static ProcedureCatalogException NotFound() => new("procedure_not_found", "That procedure was not found.", 404);

    private static void Touch(ProcedureDefinition procedure, Guid actor, DateTimeOffset now)
    {
        procedure.UpdatedAtUtc = now;
        procedure.UpdatedByUserId = actor;
    }

    private async Task<int> NextEventAsync(Guid id, CancellationToken ct) =>
        (await db.ProcedureEvents.AsNoTracking().Where(e => e.ProcedureId == id).Select(e => (int?)e.EventNumber).MaxAsync(ct) ?? 0) + 1;

    private void StageEvent(Guid id, int number, string changeType, int? versionNumber, string? reason, Guid actor, DateTimeOffset now) =>
        db.ProcedureEvents.Add(new ProcedureEvent { Id = Guid.NewGuid(), ProcedureId = id, EventNumber = number, ChangeType = changeType, VersionNumber = versionNumber, Reason = reason, ActorUserId = actor, OccurredAtUtc = now });

    private async Task SaveAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(ProcedureDefinition), id, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException(nameof(ProcedureDefinition), id); // two writers claimed the same next event or version, or the same new code
        }
    }
}
