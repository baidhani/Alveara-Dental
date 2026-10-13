using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;

namespace Alveara.Api.Architecture.Odontogram;

/// <summary>
/// ALV-008-C01: the chart half of completing a procedure, sharing the rules of a person changing a finding's state by hand (the same allowed moves, the missing-tooth rule, the same history version and audit
/// entry) but staging only, so completion can save it inside its own transaction together with everything else.
/// </summary>
public partial class OdontogramService
{
    /// <summary>
    /// Applies a state change to a loaded finding and stages its history version and audit entry. The change is not saved here. The missing-tooth rule applies exactly as it does to a manual change
    /// (completing an extraction is refused while other findings stand on the tooth).
    /// </summary>
    private async Task StageMoveAsync(ToothFinding finding, string to, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        if (MakesToothAbsent(to) && await db.ConditionTypes.AsNoTracking().AnyAsync(c => c.Code == finding.Condition && c.ToothEffect == ToothEffects.Absent, ct))
            await EnsureToothHasNoOtherFindingsAsync(finding.PatientId, finding.ToothKey, finding.Id, ct);

        finding.State = to;
        Touch(finding, actor, now);
        StageVersion(finding, await NextVersionAsync(finding.Id, ct), FindingChangeTypes.StateChanged, null, actor, now);
        AuditService.Record(db, "ToothFindingStateChanged", nameof(ToothFinding), finding.Id, actor, "Tooth finding state changed.");
    }

    /// <summary>
    /// Stages the move of one finding to Completed for a completed procedure. The finding is read with its current row version, so a change made by someone else before the transaction saves is a conflict
    /// rather than an overwrite. A finding that is already Completed is left alone. Nothing is saved here.
    /// </summary>
    internal async Task StageCompletedAsync(Guid findingId, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var finding = await db.ToothFindings.SingleOrDefaultAsync(f => f.Id == findingId, ct) ?? throw new OdontogramException("finding_not_found", "That finding was not found.", 404);
        if (finding.Status == FindingStatuses.Withdrawn) throw new OdontogramException("finding_withdrawn", "This finding was withdrawn, so its state cannot change.", 409);
        if (finding.State == FindingStates.Completed) return;
        if (!OdontogramRules.CanMove(finding.State, FindingStates.Completed))
            throw new OdontogramException("invalid_transition", $"A finding that is {finding.State} cannot become {FindingStates.Completed}.", 409);
        await StageMoveAsync(finding, FindingStates.Completed, actor, now, ct);
    }

    /// <summary>
    /// ALV-008-C02 R02: for each given finding, the number of its latest history version, which must show it as an active Completed state (the version this operation just wrote, or the one it observed). Read
    /// inside the completion's transaction after the chart's moves are saved, so a finding this operation moved is held by its own row lock and cannot be changed under it; a finding it only observed that was
    /// changed (for example withdrawn) between resolution and now is refused rather than recorded with wrong evidence.
    /// </summary>
    internal async Task<IReadOnlyDictionary<Guid, int>> CompletedVersionsAsync(IReadOnlyCollection<Guid> findingIds, CancellationToken ct)
    {
        if (findingIds.Count == 0) return new Dictionary<Guid, int>();
        var latest = await db.ToothFindingVersions.AsNoTracking()
            .Where(v => findingIds.Contains(v.FindingId) && v.VersionNumber == db.ToothFindingVersions.Where(w => w.FindingId == v.FindingId).Max(w => w.VersionNumber))
            .ToListAsync(ct);
        var result = new Dictionary<Guid, int>();
        foreach (var id in findingIds)
        {
            var v = latest.SingleOrDefault(x => x.FindingId == id);
            if (v is null) throw new OdontogramException("finding_not_found", "That finding was not found.", 404);
            if (v.State != FindingStates.Completed || v.Status != FindingStatuses.Active)
                throw new OdontogramException("finding_changed", "The chart finding changed while the completion was being recorded, so nothing was recorded. Review the chart and try again.", 409);
            result[id] = v.VersionNumber;
        }
        return result;
    }
}
