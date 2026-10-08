using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;

namespace Alveara.Api.Architecture.Procedures;

/// <summary>
/// The read side of the catalog: the screen's list and detail, the usage warning, the history, and the calls other modules make - the active procedures a treatment plan may offer for a tooth and
/// surface, and the exact snapshot (a version, or the fee in effect on a date) a plan, a completed procedure or a charge copies. The catalog is small (hundreds of rows), so the
/// "version in effect on a date" is worked out in memory over the versions of the rows asked about.
/// </summary>
public partial class ProcedureCatalogService
{
    /// <summary>The catalog as of a date (today when none is given). <paramref name="status"/> is <c>active</c> (usable that day), <c>inactive</c> or <c>all</c> (the default).</summary>
    public async Task<IReadOnlyList<ProcedureSummaryView>> ListAsync(string? search, string? category, string? codeSystem, string? status, DateOnly? asOf, CancellationToken ct)
    {
        var on = asOf ?? Today();
        var wantStatus = ProcedureRules.Clean(status)?.ToLowerInvariant() ?? "all";
        if (wantStatus is not ("all" or "active" or "inactive")) throw ProcedureRules.Invalid(new Dictionary<string, string> { ["status"] = "Choose active, inactive or all." });
        var wantCategory = ProcedureRules.Clean(category);
        var wantSystem = ProcedureRules.Clean(codeSystem);
        if (wantCategory is not null && !ProcedureCategories.All.Contains(wantCategory)) throw ProcedureRules.Invalid(new Dictionary<string, string> { ["category"] = "That is not a category." });
        if (wantSystem is not null && !ProcedureCodeSystems.All.Contains(wantSystem)) throw ProcedureRules.Invalid(new Dictionary<string, string> { ["codeSystem"] = "That is not a code system." });

        var procedures = await db.ProcedureDefinitions.AsNoTracking().Where(p => wantSystem == null || p.CodeSystem == wantSystem).ToListAsync(ct);
        var ids = procedures.Select(p => p.Id).ToList();
        var versions = (await db.ProcedureVersions.AsNoTracking().Where(v => ids.Contains(v.ProcedureId)).ToListAsync(ct)).ToLookup(v => v.ProcedureId);
        var names = await ClinicalNames.ResolveAsync(db, versions.SelectMany(g => g).Select(v => (Guid?)v.CreatedByUserId), ct);
        var needle = ProcedureRules.Clean(search)?.ToLowerInvariant();

        var rows = new List<ProcedureSummaryView>();
        foreach (var p in procedures)
        {
            var summary = Summarize(p, versions[p.Id].ToList(), on, names);
            if (wantCategory is not null && summary.Version.Category != wantCategory) continue;
            if (wantStatus == "active" && summary.Status != "Active") continue;
            if (wantStatus == "inactive" && summary.IsActive) continue;
            if (needle is not null && !summary.Code.ToLowerInvariant().Contains(needle) && !summary.Version.Description.ToLowerInvariant().Contains(needle)) continue;
            rows.Add(summary);
        }
        return rows.OrderBy(r => r.CodeSystem, StringComparer.Ordinal).ThenBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    public async Task<ProcedureDetailView> GetAsync(Guid id, DateOnly? asOf, CancellationToken ct)
    {
        var procedure = await db.ProcedureDefinitions.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw NotFound();
        var versions = await db.ProcedureVersions.AsNoTracking().Where(v => v.ProcedureId == id).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => (Guid?)v.CreatedByUserId), ct);
        return new ProcedureDetailView(Summarize(procedure, versions, asOf ?? Today(), names), versions.Select(v => ViewOf(v, names)).ToList());
    }

    public async Task<IReadOnlyList<ProcedureEventView>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ProcedureDefinitions.AsNoTracking().AnyAsync(p => p.Id == id, ct)) throw NotFound();
        var events = await db.ProcedureEvents.AsNoTracking().Where(e => e.ProcedureId == id).OrderBy(e => e.EventNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, events.Select(e => (Guid?)e.ActorUserId), ct);
        return events.Select(e => new ProcedureEventView(e.EventNumber, e.ChangeType, e.VersionNumber, e.Reason, ClinicalNames.Name(names, e.ActorUserId), e.OccurredAtUtc)).ToList();
    }

    /// <summary>How many records elsewhere refer to the procedure, by source. Only numbers leave this method.</summary>
    public async Task<ProcedureUsageView> UsageAsync(Guid id, CancellationToken ct)
    {
        if (!await db.ProcedureDefinitions.AsNoTracking().AnyAsync(p => p.Id == id, ct)) throw NotFound();
        var by = new List<ProcedureUsageBySource>();
        foreach (var source in usageSources) by.Add(new ProcedureUsageBySource(source.Name, await source.CountAsync(id, ct)));
        return new ProcedureUsageView(id, by.Sum(b => b.Count), by);
    }

    /// <summary>
    /// What treatment planning may offer: the procedures that are usable on a date (active, started, not past their last valid date), each as the snapshot in effect that day. When a tooth is
    /// given only tooth-level procedures that fit that tooth's dentition are returned, and when a surface is given only the surface-level ones; <paramref name="scope"/> narrows it further.
    /// </summary>
    public async Task<IReadOnlyList<ProcedureSnapshot>> ActiveForPlanningAsync(string? toothKey, string? surface, string? scope, string? category, string? search, DateOnly? asOf, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var tooth = ProcedureRules.Clean(toothKey);
        var surf = ProcedureRules.Clean(surface);
        var wantScope = ProcedureRules.Clean(scope);
        if (tooth is not null && !ToothKeys.IsValid(tooth)) errors["toothKey"] = "That is not a tooth.";
        if (surf is not null && tooth is null) errors["surface"] = "A surface needs the tooth it is on.";
        else if (surf is not null && tooth is not null && ToothKeys.IsValid(tooth) && !ToothSurfaces.For(tooth).Contains(surf)) errors["surface"] = "That surface does not exist on that tooth.";
        if (wantScope is not null && !ProcedureScopes.All.Contains(wantScope)) errors["scope"] = "That is not a scope.";
        if (errors.Count > 0) throw ProcedureRules.Invalid(errors);

        var on = asOf ?? Today();
        var summaries = await ListAsync(search, category, null, "active", on, ct);
        var rows = summaries.Where(s => FitsTooth(s.Version, tooth, surf, wantScope));
        return rows.Select(s => SnapshotOf(s.Id, s.CodeSystem, s.Code, s.Version)).ToList();
    }

    /// <summary>The snapshot of a procedure on a date (today when none is given), whether or not it is active: what a consumer copies to remember the fee it was made with.</summary>
    public async Task<ProcedureSnapshot> SnapshotAsync(Guid id, DateOnly? asOf, CancellationToken ct)
    {
        var detail = await GetAsync(id, asOf, ct);
        return SnapshotOf(detail.Summary.Id, detail.Summary.CodeSystem, detail.Summary.Code, detail.Summary.Version);
    }

    /// <summary>The exact snapshot of one version, however old: what a plan or charge that remembered a version id reads back.</summary>
    public async Task<ProcedureSnapshot> VersionSnapshotAsync(Guid versionId, CancellationToken ct)
    {
        var version = await db.ProcedureVersions.AsNoTracking().SingleOrDefaultAsync(v => v.Id == versionId, ct) ?? throw new ProcedureCatalogException("procedure_version_not_found", "That version of the procedure was not found.", 404);
        var procedure = await db.ProcedureDefinitions.AsNoTracking().SingleAsync(p => p.Id == version.ProcedureId, ct);
        var names = await ClinicalNames.ResolveAsync(db, new[] { (Guid?)version.CreatedByUserId }, ct);
        return SnapshotOf(procedure.Id, procedure.CodeSystem, procedure.Code, ViewOf(version, names));
    }

    // ---------- shared ----------

    private static bool FitsTooth(ProcedureVersionView v, string? tooth, string? surface, string? scope)
    {
        if (scope is not null && v.Scope != scope) return false;
        if (tooth is null) return true;
        if (!ProcedureScopes.IsToothLevel(v.Scope)) return false;
        if (surface is not null && v.Scope != ProcedureScopes.ToothSurface) return false;
        return v.Dentition == ProcedureDentitions.Both || (v.Dentition == ProcedureDentitions.Primary) == ToothKeys.IsPrimary(tooth);
    }

    private static ProcedureSnapshot SnapshotOf(Guid procedureId, string system, string code, ProcedureVersionView v) =>
        new(procedureId, v.VersionId, v.VersionNumber, system, code, v.Description, v.Category, v.Scope, v.Dentition, v.Fee, v.Currency, v.SourceName, v.SourceVersion, v.EffectiveFrom, v.ValidThrough);

    private static ProcedureVersionView ViewOf(ProcedureVersion v, IReadOnlyDictionary<Guid, string> names) =>
        new(v.Id, v.ProcedureId, v.VersionNumber, v.Description, v.Category, v.Scope, v.Dentition, v.Fee, Alveara.Api.Architecture.Money.Money.Currency, v.SourceName, v.SourceVersion, v.EffectiveFrom, v.ValidThrough, v.Reason,
            ClinicalNames.Name(names, v.CreatedByUserId), v.CreatedAtUtc);

    private static ProcedureSummaryView Summarize(ProcedureDefinition p, IReadOnlyList<ProcedureVersion> versions, DateOnly on, IReadOnlyDictionary<Guid, string> names)
    {
        var inEffect = ProcedureRules.InEffect(versions, on);
        // before any version has started the procedure is "Scheduled" and shows its first version, so a row is never blank
        var shown = inEffect ?? versions.OrderBy(v => v.EffectiveFrom).ThenBy(v => v.VersionNumber).First();
        return new ProcedureSummaryView(p.Id, p.CodeSystem, p.Code, p.IsActive, ProcedureRules.StatusOn(p.IsActive, inEffect, on), on, p.CurrentVersionNumber, ViewOf(shown, names), Convert.ToBase64String(p.RowVersion), p.CreatedAtUtc);
    }
}
