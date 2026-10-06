using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-013-C01: the structure and lifecycle of a diagnosis, on top of STORY-013's recording, correcting and withdrawing.
/// - <b>Amend</b> replaces the structure (location, coding, source) as a recorded act with a reason; the previous values stay in the history with who and when, and the label, notes and treatment-plan
///   reference (with its provenance) are carried through untouched. Nothing is silently replaced: a diagnosis in use is amended, never overwritten.
/// - <b>Resolve</b> and <b>reactivate</b> move a diagnosis between Active and Resolved (a reason is required, each is a history entry); a withdrawn diagnosis can do neither.
/// - <b>Link</b> ties a diagnosis to a finding or periodontal chart of the SAME patient (otherwise <c>link_target_not_found</c>, which does not reveal whether the record exists for someone else). Links are
///   append-only, idempotent, and carry who and when; they are logged in the audit trail.
/// - A treatment-plan reference can never be marked resolved or validated (<c>validation_failed</c> with <c>treatmentPlanReferenceState:not_supported</c>): there are no treatment plans to resolve it against.
/// </summary>
public partial class DiagnosisService
{
    /// <summary>Amends the structure of a diagnosis. Changing nothing is quiet.</summary>
    public async Task<DiagnosisView> AmendAsync(Guid diagnosisId, string? rowVersion, DiagnosisAmendment? amendment, string? reason, Guid actor, CancellationToken ct)
    {
        var problems = new List<DiagnosisProblem>();
        if (amendment is null) throw new DiagnosisException("validation_failed", "Enter the amended diagnosis. Nothing was saved.", 400, [new("diagnosis", "required", "Enter the amended diagnosis.")]);
        var why = Reason(reason, problems);
        var d = await LoadAsync(diagnosisId, rowVersion, ct);
        if (d.Status == DiagnosisStatuses.Withdrawn) throw Withdrawn("amended");

        // the amended whole entry as the rules judge it: the label, notes and reference are the current ones, so only the structure can be wrong
        var check = DiagnosisRules.Check(new DiagnosisInput(d.EncounterId, d.Label, amendment.ToothKey, d.Notes, d.TreatmentPlanReference, amendment.CodingSystem, amendment.Code, amendment.Source, amendment.SourceNote, amendment.RegionKey,
            amendment.TreatmentPlanReferenceState));
        problems.AddRange(check.Problems);
        if (problems.Count > 0) throw new DiagnosisException("validation_failed", "The amendment has entries that need correcting. Nothing was saved.", 400, problems);
        var v = check.Value!;

        if (v.ToothKey == d.ToothKey && v.RegionKey == d.RegionKey && v.CodingSystem == d.CodingSystem && v.Code == d.Code && v.Source == d.Source && v.SourceNote == d.SourceNote) return await ViewAsync(d.Id, ct);
        var now = clock.UtcNow;
        (d.ToothKey, d.RegionKey, d.CodingSystem, d.Code, d.Source, d.SourceNote) = (v.ToothKey, v.RegionKey, v.CodingSystem, v.Code, v.Source, v.SourceNote);
        Touch(d, actor, now);
        StageVersion(d, await NextVersionAsync(d.Id, ct), DiagnosisChangeTypes.Amended, why, actor, now);
        AuditService.Record(db, "DiagnosisAmended", nameof(Diagnosis), d.Id, actor, "Diagnosis amended.");
        await SaveAsync(d.Id, ct);
        return await ViewAsync(d.Id, ct);
    }

    /// <summary>Marks an active diagnosis as resolved (no longer present). Resolving a resolved diagnosis is quiet; a withdrawn one cannot be resolved.</summary>
    public Task<DiagnosisView> ResolveAsync(Guid diagnosisId, string? rowVersion, string? reason, Guid actor, CancellationToken ct) =>
        ChangeStatusAsync(diagnosisId, rowVersion, reason, actor, DiagnosisStatuses.Resolved, DiagnosisChangeTypes.Resolved, "DiagnosisResolved", "resolved", ct);

    /// <summary>Makes a resolved diagnosis active again. Reactivating an active diagnosis is quiet; a withdrawn one cannot be reactivated.</summary>
    public Task<DiagnosisView> ReactivateAsync(Guid diagnosisId, string? rowVersion, string? reason, Guid actor, CancellationToken ct) =>
        ChangeStatusAsync(diagnosisId, rowVersion, reason, actor, DiagnosisStatuses.Active, DiagnosisChangeTypes.Reactivated, "DiagnosisReactivated", "reactivated", ct);

    private async Task<DiagnosisView> ChangeStatusAsync(Guid diagnosisId, string? rowVersion, string? reason, Guid actor, string status, string changeType, string auditEvent, string word, CancellationToken ct)
    {
        var problems = new List<DiagnosisProblem>();
        var why = Reason(reason, problems);
        if (problems.Count > 0) throw new DiagnosisException("reason_required", $"Say why this diagnosis is being {word}. Nothing was changed.", 400, problems);
        var current = await db.Diagnoses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == diagnosisId, ct) ?? throw NotFound();
        if (current.Status == DiagnosisStatuses.Withdrawn) throw Withdrawn(word);
        if (current.Status == status) return await ViewAsync(diagnosisId, ct);

        var d = await LoadAsync(diagnosisId, rowVersion, ct);
        var now = clock.UtcNow;
        d.Status = status;
        Touch(d, actor, now);
        StageVersion(d, await NextVersionAsync(d.Id, ct), changeType, why, actor, now);
        AuditService.Record(db, auditEvent, nameof(Diagnosis), d.Id, actor, $"Diagnosis {word}.");
        await SaveAsync(d.Id, ct);
        return await ViewAsync(d.Id, ct);
    }

    /// <summary>Links a diagnosis to a finding or periodontal chart of the same patient. Linking the same target twice is quiet and returns the diagnosis as it is.</summary>
    public async Task<DiagnosisView> LinkAsync(Guid diagnosisId, string? linkType, Guid? targetId, Guid actor, CancellationToken ct)
    {
        var problems = new List<DiagnosisProblem>();
        if (linkType is null || !DiagnosisLinkTypes.All.Contains(linkType))
            problems.Add(new("linkType", "unsupported_link_type", $"Link a diagnosis to one of {string.Join(", ", DiagnosisLinkTypes.All)}."));
        if (targetId is null || targetId == Guid.Empty) problems.Add(new("targetId", "required", "Choose the record to link."));
        if (problems.Count > 0) throw new DiagnosisException("validation_failed", "The link has entries that need correcting. Nothing was saved.", 400, problems);

        var d = await db.Diagnoses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == diagnosisId, ct) ?? throw NotFound();
        if (d.Status == DiagnosisStatuses.Withdrawn) throw Withdrawn("linked");
        var found = linkType == DiagnosisLinkTypes.Finding
            ? await db.ToothFindings.AsNoTracking().AnyAsync(f => f.Id == targetId && f.PatientId == d.PatientId, ct)
            : await db.PerioExams.AsNoTracking().AnyAsync(x => x.Id == targetId && x.PatientId == d.PatientId, ct);
        if (!found) throw new DiagnosisException("link_target_not_found", "That record was not found for this patient. Choose one of this patient's records.", 404, [new("targetId", "not_found", "Choose one of this patient's records.")]);
        if (await db.DiagnosisLinks.AsNoTracking().AnyAsync(l => l.DiagnosisId == diagnosisId && l.LinkType == linkType && l.TargetId == targetId, ct)) return await ViewAsync(diagnosisId, ct);

        db.DiagnosisLinks.Add(new DiagnosisLink { Id = Guid.NewGuid(), DiagnosisId = diagnosisId, PatientId = d.PatientId, LinkType = linkType!, TargetId = targetId!.Value, CreatedAtUtc = clock.UtcNow, CreatedByUserId = actor });
        AuditService.Record(db, "DiagnosisLinked", nameof(Diagnosis), diagnosisId, actor, "Diagnosis linked.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                  // a simultaneous identical link won: the diagnosis already has it
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
        return await ViewAsync(diagnosisId, ct);
    }

    private async Task<Dictionary<Guid, IReadOnlyList<DiagnosisLinkView>>> LinkViewsAsync(IReadOnlyList<Guid> diagnosisIds, CancellationToken ct)
    {
        var links = await db.DiagnosisLinks.AsNoTracking().Where(l => diagnosisIds.Contains(l.DiagnosisId)).OrderBy(l => l.CreatedAtUtc).ThenBy(l => l.Id).ToListAsync(ct);
        if (links.Count == 0) return [];
        var findingIds = links.Where(l => l.LinkType == DiagnosisLinkTypes.Finding).Select(l => l.TargetId).ToList();
        var chartIds = links.Where(l => l.LinkType == DiagnosisLinkTypes.PerioExam).Select(l => l.TargetId).ToList();
        var findings = await db.ToothFindings.AsNoTracking().Where(f => findingIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        var charts = await db.PerioExams.AsNoTracking().Where(x => chartIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var names = await ClinicalNames.ResolveAsync(db, links.Select(l => l.CreatedByUserId), ct);
        string Summary(DiagnosisLink l) => l.LinkType == DiagnosisLinkTypes.Finding && findings.TryGetValue(l.TargetId, out var f)
            ? $"{f.Condition} on tooth {f.ToothKey}{(f.Surface is null ? "" : $", surface {f.Surface}")}"
            : l.LinkType == DiagnosisLinkTypes.PerioExam && charts.TryGetValue(l.TargetId, out var c) ? $"Periodontal chart of {c.RecordedAtUtc:yyyy-MM-dd}" : "Linked record";
        return links.GroupBy(l => l.DiagnosisId).ToDictionary(g => g.Key,
            g => (IReadOnlyList<DiagnosisLinkView>)g.Select(l => new DiagnosisLinkView(l.LinkType, l.TargetId, Summary(l), ClinicalNames.Name(names, l.CreatedByUserId), l.CreatedAtUtc)).ToList());
    }
}
