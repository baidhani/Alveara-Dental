using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;

namespace Alveara.Api.Architecture.Odontogram;

/// <summary>
/// ALV-006-C01: the longitudinal view of one tooth, and the links from a finding to the diagnosis, treatment plan or procedure it relates to.
///
/// - <b>The timeline is read, never written.</b> It is every version of every finding ever recorded on the tooth (withdrawn ones included), in the order they happened. Findings are never
///   deleted, versions are append-only (database triggers refuse an edit or a delete), and a retired condition keeps its label, so nothing a later chart edit does removes or re-words an event.
/// - <b>A link is a recorded event.</b> It is stored by reference (the diagnosis, plan and procedure stories come later), is append-only and unique per finding, type and reference, appends a
///   history version in the same save as its audit entry, and does not change the finding, so linking never invalidates someone else's edit. Linking the same thing again is quiet.
/// </summary>
public partial class OdontogramService
{
    public const int ReferenceMax = 100;

    public async Task<ToothHistoryView> ToothHistoryAsync(Guid patientId, string? toothKey, CancellationToken ct)
    {
        var tooth = toothKey?.Trim();
        if (!ToothKeys.IsValid(tooth)) throw new OdontogramException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["toothKey"] = "Choose one of the teeth on the chart." });
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new OdontogramException("patient_not_found", "That patient was not found.", 404);
        var versions = await db.ToothFindingVersions.AsNoTracking().Where(v => v.PatientId == patientId && v.ToothKey == tooth).OrderBy(v => v.OccurredAtUtc).ThenBy(v => v.FindingId).ThenBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => v.ActorUserId), ct);
        var labels = await LabelsAsync(ct);
        return new ToothHistoryView(patientId, tooth!, versions.Select(v => new ToothEventView(v.FindingId, v.Condition, labels.GetValueOrDefault(v.Condition, v.Condition), v.Surface, v.VersionNumber, v.ChangeType,
            v.State, v.Status, v.Reason, ClinicalNames.Name(names, v.ActorUserId), v.OccurredAtUtc)).ToList());
    }

    /// <summary>Links a finding to a diagnosis, treatment plan or procedure by reference. Linking a withdrawn finding is refused; linking the same thing again changes nothing.</summary>
    public async Task<ChartView> LinkAsync(Guid findingId, string? linkType, string? reference, Guid actor, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();
        var type = OdontogramRules.Clean(linkType);
        var reference_ = OdontogramRules.Clean(reference);
        if (type is null || !LinkTypes.All.Contains(type)) errors["linkType"] = "Choose " + string.Join(", ", LinkTypes.All) + ".";
        if (reference_ is null) errors["reference"] = "Say which record this links to.";
        else if (reference_.Length > ReferenceMax) errors["reference"] = $"Keep the reference to {ReferenceMax} characters or fewer.";
        if (errors.Count > 0) throw new OdontogramException("validation_failed", "Some fields need attention.", 400, errors);

        var finding = await db.ToothFindings.AsNoTracking().SingleOrDefaultAsync(f => f.Id == findingId, ct) ?? throw new OdontogramException("finding_not_found", "That finding was not found.", 404);
        if (finding.Status == FindingStatuses.Withdrawn) throw new OdontogramException("finding_withdrawn", "This finding was withdrawn, so it cannot be linked.", 409);
        if (await db.ToothFindingLinks.AsNoTracking().AnyAsync(l => l.FindingId == findingId && l.LinkType == type && l.Reference == reference_, ct)) return await ChartAsync(finding.PatientId, ct);

        var now = clock.UtcNow;
        db.ToothFindingLinks.Add(new ToothFindingLink { Id = Guid.NewGuid(), FindingId = findingId, PatientId = finding.PatientId, LinkType = type!, Reference = reference_!, CreatedAtUtc = now, CreatedByUserId = actor });
        db.ToothFindingVersions.Add(new ToothFindingVersion
        {
            Id = Guid.NewGuid(), FindingId = findingId, PatientId = finding.PatientId, VersionNumber = await NextVersionAsync(findingId, ct), ChangeType = FindingChangeTypes.Linked, ToothKey = finding.ToothKey,
            Surface = finding.Surface, Condition = finding.Condition, State = finding.State, Status = finding.Status, Reason = $"{type}: {reference_}", ActorUserId = actor, OccurredAtUtc = now,
        });
        AuditService.Record(db, "ToothFindingLinked", nameof(ToothFinding), findingId, actor, "Tooth finding linked.");
        try
        {
            await SaveAsync(findingId, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // the same link was made at the same moment (the unique index), or another change took the next history number: look again - an identical link is a retry of ours
            db.ChangeTracker.Clear();
            if (await db.ToothFindingLinks.AsNoTracking().AnyAsync(l => l.FindingId == findingId && l.LinkType == type && l.Reference == reference_, ct)) return await ChartAsync(finding.PatientId, ct);
            throw;
        }
        return await ChartAsync(finding.PatientId, ct);
    }
}
