using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Odontogram;

/// <summary>
/// ALV-006-C01: the practice's catalogue of conditions a finding can be recorded as. Starts with the six the course odontogram shipped with; a dentist can add another (for example a
/// fracture or a sealant) without a release, and retire one that is no longer wanted.
///
/// How the promises are kept:
/// - <b>History is never re-described.</b> A condition's code, label, scope, dentition and tooth effect are fixed when it is created (a database trigger refuses a change), and a retired
///   condition stays in the catalogue, so every finding that used it still reads the same. Retiring only stops NEW findings of that type.
/// - <b>Every change is logged in the same save</b> - the row, its event (who, when, why) and a PHI-free audit entry - so a change cannot exist without its log entry.
/// - <b>Stale changes are refused</b> through the row version; two people creating the same code at the same moment end with one condition.
/// - <b>Repeats are quiet.</b> Creating the identical condition, retiring a retired one and reactivating an active one change nothing.
/// </summary>
public class ConditionTypeService(AlveraDbContext db, IPracticeClock clock)
{
    private static readonly Regex CodeShape = new("^[A-Z][A-Za-z0-9]{1,31}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public const int LabelMax = 80;

    public async Task<IReadOnlyList<ConditionTypeView>> ListAsync(CancellationToken ct)
    {
        var rows = await db.ConditionTypes.AsNoTracking().OrderByDescending(c => c.IsActive).ThenBy(c => c.Label).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(r => r.CreatedByUserId), ct);
        return rows.Select(r => new ConditionTypeView(r.Id, r.Code, r.Label, r.Scope, r.AppliesTo, r.ToothEffect, r.IsActive, r.CreatedByUserId is null ? "System" : ClinicalNames.Name(names, r.CreatedByUserId),
            r.CreatedAtUtc, Convert.ToBase64String(r.RowVersion))).ToList();
    }

    public async Task<IReadOnlyList<ConditionTypeView>> CreateAsync(string? code, string? label, string? scope, string? appliesTo, string? toothEffect, Guid actor, CancellationToken ct)
    {
        var f = Validate(code, label, scope, appliesTo, toothEffect);
        // codes are compared ignoring case here, so "fracture" cannot be added beside "Fracture" (the database compares them exactly)
        var twin = await db.ConditionTypes.AsNoTracking().SingleOrDefaultAsync(c => EF.Functions.Collate(c.Code, "Latin1_General_CI_AS") == f.Code, ct);
        if (twin is not null) return twin.Code == f.Code && Same(twin, f) ? await ListAsync(ct) : throw Exists(f.Code);

        var now = clock.UtcNow;
        var type = new ConditionType { Id = Guid.NewGuid(), Code = f.Code, Label = f.Label, Scope = f.Scope, AppliesTo = f.AppliesTo, ToothEffect = f.Effect, IsActive = true, CreatedAtUtc = now, CreatedByUserId = actor };
        db.ConditionTypes.Add(type);
        StageEvent(type, 1, ConditionTypeChanges.Created, null, actor, now);
        AuditService.Record(db, "ConditionTypeCreated", nameof(ConditionType), type.Id, actor, "Condition type created.");
        try
        {
            await SaveAsync(type.Id, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // the same code was created at the same moment (the unique key): the first one stands, so this is a retry of it when it is the same definition
            db.ChangeTracker.Clear();
            var winner = await db.ConditionTypes.AsNoTracking().SingleOrDefaultAsync(c => EF.Functions.Collate(c.Code, "Latin1_General_CI_AS") == f.Code, ct);
            if (winner is not null && winner.Code == f.Code && Same(winner, f)) return await ListAsync(ct);
            throw Exists(f.Code);
        }
        return await ListAsync(ct);
    }

    /// <summary>Retires a condition: no new finding can use it; every existing finding keeps it. A reason is required; retiring a retired one changes nothing.</summary>
    public async Task<IReadOnlyList<ConditionTypeView>> RetireAsync(Guid id, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = OdontogramRules.RequireReason(reason, "Say why this condition is being retired.");
        var type = await LoadAsync(id, rowVersion, ct);
        if (!type.IsActive) return await ListAsync(ct);
        var now = clock.UtcNow;
        type.IsActive = false;
        Touch(type, actor, now);
        StageEvent(type, await NextEventAsync(id, ct), ConditionTypeChanges.Retired, why, actor, now);
        AuditService.Record(db, "ConditionTypeRetired", nameof(ConditionType), type.Id, actor, "Condition type retired.");
        await SaveAsync(id, ct);
        return await ListAsync(ct);
    }

    /// <summary>Makes a retired condition available again. A reason is optional; reactivating an active one changes nothing.</summary>
    public async Task<IReadOnlyList<ConditionTypeView>> ReactivateAsync(Guid id, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = OdontogramRules.Clean(reason);
        if (why is { Length: > OdontogramRules.ReasonMax }) throw new OdontogramException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["reason"] = $"Keep the reason to {OdontogramRules.ReasonMax} characters or fewer." });
        var type = await LoadAsync(id, rowVersion, ct);
        if (type.IsActive) return await ListAsync(ct);
        var now = clock.UtcNow;
        type.IsActive = true;
        Touch(type, actor, now);
        StageEvent(type, await NextEventAsync(id, ct), ConditionTypeChanges.Reactivated, why, actor, now);
        AuditService.Record(db, "ConditionTypeReactivated", nameof(ConditionType), type.Id, actor, "Condition type reactivated.");
        await SaveAsync(id, ct);
        return await ListAsync(ct);
    }

    public async Task<IReadOnlyList<ConditionTypeEventView>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ConditionTypes.AsNoTracking().AnyAsync(c => c.Id == id, ct)) throw new OdontogramException("condition_not_found", "That condition type was not found.", 404);
        var events = await db.ConditionTypeEvents.AsNoTracking().Where(e => e.ConditionTypeId == id).OrderBy(e => e.EventNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, events.Select(e => e.ActorUserId), ct);
        return events.Select(e => new ConditionTypeEventView(e.EventNumber, e.ChangeType, e.Reason, e.ActorUserId is null ? "System" : ClinicalNames.Name(names, e.ActorUserId), e.OccurredAtUtc)).ToList();
    }

    // ---------- shared ----------

    private sealed record Fields(string Code, string Label, string Scope, string AppliesTo, string Effect);

    private static Fields Validate(string? code, string? label, string? scope, string? appliesTo, string? toothEffect)
    {
        var errors = new Dictionary<string, string>();
        var c = OdontogramRules.Clean(code);
        var l = OdontogramRules.Clean(label);
        var sc = OdontogramRules.Clean(scope);
        var ap = OdontogramRules.Clean(appliesTo);
        var ef = OdontogramRules.Clean(toothEffect) ?? ToothEffects.None;
        if (c is null || !CodeShape.IsMatch(c)) errors["code"] = "Use 2 to 32 letters and digits, starting with a capital letter (for example Fracture).";
        if (l is null) errors["label"] = "A label is required.";
        else if (l.Length > LabelMax) errors["label"] = $"Keep the label to {LabelMax} characters or fewer.";
        if (sc is null || !ConditionScopes.All.Contains(sc)) errors["scope"] = "Choose Surface or WholeTooth.";
        if (ap is null || !ConditionDentitions.All.Contains(ap)) errors["appliesTo"] = "Choose Permanent, Primary or Both.";
        if (!ToothEffects.All.Contains(ef)) errors["toothEffect"] = "Choose None, Absent or Replacement.";
        if (errors.Count > 0) throw new OdontogramException("validation_failed", "Some fields need attention.", 400, errors);
        return new Fields(c!, l!, sc!, ap!, ef);
    }

    private static bool Same(ConditionType t, Fields f) => t.Label == f.Label && t.Scope == f.Scope && t.AppliesTo == f.AppliesTo && t.ToothEffect == f.Effect;

    private static OdontogramException Exists(string code) => new("condition_exists", $"There is already a condition with the code '{code}'. Choose another code.", 409, new Dictionary<string, string> { ["code"] = "That code is already used." });

    private async Task<ConditionType> LoadAsync(Guid id, string? rowVersion, CancellationToken ct)
    {
        var version = OdontogramRules.ParseVersion(rowVersion);
        var type = await db.ConditionTypes.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new OdontogramException("condition_not_found", "That condition type was not found.", 404);
        db.Entry(type).Property(c => c.RowVersion).OriginalValue = version;
        return type;
    }

    private static void Touch(ConditionType type, Guid actor, DateTimeOffset now)
    {
        type.UpdatedAtUtc = now;
        type.UpdatedByUserId = actor;
    }

    private async Task<int> NextEventAsync(Guid id, CancellationToken ct) =>
        (await db.ConditionTypeEvents.AsNoTracking().Where(e => e.ConditionTypeId == id).Select(e => (int?)e.EventNumber).MaxAsync(ct) ?? 0) + 1;

    private void StageEvent(ConditionType type, int number, string changeType, string? reason, Guid actor, DateTimeOffset now) =>
        db.ConditionTypeEvents.Add(new ConditionTypeEvent { Id = Guid.NewGuid(), ConditionTypeId = type.Id, Code = type.Code, EventNumber = number, ChangeType = changeType, Reason = reason, ActorUserId = actor, OccurredAtUtc = now });

    private async Task SaveAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(ConditionType), id, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException(nameof(ConditionType), id); // two writers claimed the same next event, or the same new code
        }
    }
}
