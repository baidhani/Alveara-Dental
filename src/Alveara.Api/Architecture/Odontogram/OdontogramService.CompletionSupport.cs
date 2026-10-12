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
}
