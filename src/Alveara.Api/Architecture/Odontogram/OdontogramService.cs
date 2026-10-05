using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Odontogram;

/// <summary>
/// STORY-006: the interactive odontogram's findings - what is on each tooth and where it is in its lifecycle (Existing, Diagnosed, Planned, Completed).
///
/// How the promises are kept:
/// - <b>Correct tooth, correct state.</b> A tooth must be one of the 52 FDI keys and a surface must exist on that kind of tooth; a wrong selection is refused with the field named
///   (<c>validation_failed</c>), never saved and guessed at. A finding is saved with exactly the state the clinician chose. A state only moves forward (Diagnosed, Planned,
///   Completed); a refused move is <c>invalid_transition</c>.
/// - <b>Completed work updates the chart.</b> Completing a finding, or recording work as Completed directly, changes the finding the chart reads; the chart is always read from the
///   findings, so there is no second copy to fall out of step.
/// - <b>Every update is logged, in the same save.</b> The change, its history version (who, when, why) and its PHI-free audit entry (never the tooth or the condition) are staged and
///   saved once, so a finding can never exist without its log entry: if the audit write fails, the whole change fails and nothing is saved.
/// - <b>Wrong entries are withdrawn, not deleted.</b> A reason is required and the finding stays in the history; the database refuses deleting it.
/// - <b>Concurrency:</b> a change echoes the finding's row version; a stale one is refused (409 <c>concurrency_conflict</c>), never merged. Two people recording the same finding at the
///   same moment end with one finding (a unique index).
/// - <b>Repeats are quiet.</b> Recording the same active finding again, moving to the state it already has, and withdrawing a withdrawn finding change nothing.
/// </summary>
public partial class OdontogramService(AlveraDbContext db, IPracticeClock clock)
{
    public async Task<ChartView> ChartAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new OdontogramException("patient_not_found", "That patient was not found.", 404);
        var rows = await db.ToothFindings.AsNoTracking().Where(f => f.PatientId == patientId && f.Status == FindingStatuses.Active).OrderBy(f => f.ToothKey).ThenBy(f => f.Surface).ThenBy(f => f.Condition).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.SelectMany(f => new[] { f.CreatedByUserId, f.UpdatedByUserId }), ct);
        var labels = await LabelsAsync(ct);
        var ids = rows.Select(f => f.Id).ToList();
        var links = await db.ToothFindingLinks.AsNoTracking().Where(l => ids.Contains(l.FindingId)).OrderBy(l => l.CreatedAtUtc).ToListAsync(ct);
        var linkNames = await ClinicalNames.ResolveAsync(db, links.Select(l => l.CreatedByUserId), ct);
        var byFinding = links.ToLookup(l => l.FindingId);
        return new ChartView(patientId, rows.Select(f => ToView(f, names, labels, byFinding[f.Id].Select(l => new LinkView(l.LinkType, l.Reference, ClinicalNames.Name(linkNames, l.CreatedByUserId), l.CreatedAtUtc)).ToList())).ToList());
    }

    /// <summary>Records a finding in the state the clinician chose (Completed is allowed directly, for work done at the visit). The same active finding again is a quiet repeat.</summary>
    public async Task<ChartView> RecordAsync(Guid patientId, string? toothKey, string? surface, string? condition, string? state, Guid actor, CancellationToken ct)
    {
        var code = OdontogramRules.Clean(condition);
        var type = code is null ? null : await db.ConditionTypes.AsNoTracking().SingleOrDefaultAsync(c => c.Code == code, ct);
        if (type is not null && !string.Equals(type.Code, code, StringComparison.Ordinal)) type = null;                 // the database compares case-insensitively; the catalogue's spelling is the only one accepted
        var f = OdontogramRules.ValidateFinding(toothKey, surface, code, state, type);
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new OdontogramException("patient_not_found", "That patient was not found.", 404);
        if (!f.Type.IsActive) throw new OdontogramException("condition_inactive", $"{f.Type.Label} is no longer in use, so it cannot be recorded on a new finding. Choose another condition.", 409, new Dictionary<string, string> { ["condition"] = "This condition has been retired." });

        var twin = await FindActiveAsync(patientId, f, ct);
        if (twin is not null) return await RepeatOrRefuseAsync(patientId, twin, f.State, ct);
        await EnsureToothPresentAsync(patientId, f, ct);
        if (f.Type.ToothEffect == ToothEffects.Absent && MakesToothAbsent(f.State)) await EnsureToothHasNoOtherFindingsAsync(patientId, f.ToothKey, Guid.Empty, ct);

        var now = clock.UtcNow;
        var finding = new ToothFinding
        {
            Id = Guid.NewGuid(), PatientId = patientId, ToothKey = f.ToothKey, Surface = f.Surface, Condition = f.Condition, ConditionScope = f.Type.Scope, State = f.State, Status = FindingStatuses.Active,
            CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.ToothFindings.Add(finding);
        StageVersion(finding, 1, FindingChangeTypes.Recorded, null, actor, now);
        AuditService.Record(db, "ToothFindingRecorded", nameof(ToothFinding), finding.Id, actor, "Tooth finding recorded.");
        try
        {
            await SaveAsync(finding.Id, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // the same finding was recorded at the same moment (the unique index on active findings): the first one stands, so this is a retry of it
            db.ChangeTracker.Clear();
            var winner = await FindActiveAsync(patientId, f, ct);
            if (winner is null) throw;
            return await RepeatOrRefuseAsync(patientId, winner, f.State, ct);
        }
        return await ChartAsync(patientId, ct);
    }

    /// <summary>Moves a finding forward (Diagnosed to Planned to Completed, or Diagnosed straight to Completed). Moving to the state it already has is quiet.</summary>
    public async Task<ChartView> ChangeStateAsync(Guid findingId, string? newState, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var to = OdontogramRules.ValidateTargetState(newState);
        var finding = await LoadAsync(findingId, rowVersion, ct);
        if (finding.Status == FindingStatuses.Withdrawn) throw new OdontogramException("finding_withdrawn", "This finding was withdrawn, so its state cannot change.", 409);
        if (finding.State == to) return await ChartAsync(finding.PatientId, ct);
        if (!OdontogramRules.CanMove(finding.State, to))
            throw new OdontogramException("invalid_transition", $"A finding that is {finding.State} cannot become {to}. If it was entered wrongly, withdraw it and record it again.", 409);

        if (MakesToothAbsent(to) && await db.ConditionTypes.AsNoTracking().AnyAsync(c => c.Code == finding.Condition && c.ToothEffect == ToothEffects.Absent, ct))
            await EnsureToothHasNoOtherFindingsAsync(finding.PatientId, finding.ToothKey, finding.Id, ct);

        var now = clock.UtcNow;
        finding.State = to;
        Touch(finding, actor, now);
        StageVersion(finding, await NextVersionAsync(findingId, ct), FindingChangeTypes.StateChanged, null, actor, now);
        AuditService.Record(db, "ToothFindingStateChanged", nameof(ToothFinding), finding.Id, actor, "Tooth finding state changed.");
        await SaveAsync(findingId, ct);
        return await ChartAsync(finding.PatientId, ct);
    }

    /// <summary>Withdraws a wrong entry. A reason is required; the finding and its history are kept. Withdrawing a withdrawn finding changes nothing.</summary>
    public async Task<ChartView> WithdrawAsync(Guid findingId, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = OdontogramRules.RequireReason(reason, "Say why this finding is being withdrawn.");
        var finding = await LoadAsync(findingId, rowVersion, ct);
        if (finding.Status == FindingStatuses.Withdrawn) return await ChartAsync(finding.PatientId, ct);

        var now = clock.UtcNow;
        finding.Status = FindingStatuses.Withdrawn; finding.WithdrawnAtUtc = now; finding.WithdrawnByUserId = actor; finding.WithdrawnReason = why;
        Touch(finding, actor, now);
        StageVersion(finding, await NextVersionAsync(findingId, ct), FindingChangeTypes.Withdrawn, why, actor, now);
        AuditService.Record(db, "ToothFindingWithdrawn", nameof(ToothFinding), finding.Id, actor, "Tooth finding withdrawn.");
        await SaveAsync(findingId, ct);
        return await ChartAsync(finding.PatientId, ct);
    }

    public async Task<FindingHistoryView> HistoryAsync(Guid findingId, CancellationToken ct)
    {
        if (!await db.ToothFindings.AsNoTracking().AnyAsync(f => f.Id == findingId, ct)) throw new OdontogramException("finding_not_found", "That finding was not found.", 404);
        var versions = await db.ToothFindingVersions.AsNoTracking().Where(v => v.FindingId == findingId).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => v.ActorUserId), ct);
        return new FindingHistoryView(findingId, versions.Select(v => new FindingVersionView(v.VersionNumber, v.ChangeType, v.ToothKey, v.Surface, v.Condition, v.State, v.Status, v.Reason, ClinicalNames.Name(names, v.ActorUserId), v.OccurredAtUtc)).ToList());
    }

    // ---------- shared ----------

    private Task<ToothFinding?> FindActiveAsync(Guid patientId, OdontogramRules.FindingFields f, CancellationToken ct) =>
        db.ToothFindings.AsNoTracking().Where(x => x.PatientId == patientId && x.Status == FindingStatuses.Active && x.ToothKey == f.ToothKey && x.Surface == f.Surface && x.Condition == f.Condition)
            .SingleOrDefaultAsync(ct);

    private async Task<ChartView> RepeatOrRefuseAsync(Guid patientId, ToothFinding twin, string state, CancellationToken ct)
    {
        if (twin.State == state) return await ChartAsync(patientId, ct); // a retried record: already there
        throw new OdontogramException("finding_exists", $"This is already recorded as {twin.State}. Change its state instead of recording it again.", 409);
    }

    private async Task<ToothFinding> LoadAsync(Guid findingId, string? rowVersion, CancellationToken ct)
    {
        var version = OdontogramRules.ParseVersion(rowVersion);
        var finding = await db.ToothFindings.SingleOrDefaultAsync(f => f.Id == findingId, ct) ?? throw new OdontogramException("finding_not_found", "That finding was not found.", 404);
        db.Entry(finding).Property(f => f.RowVersion).OriginalValue = version;
        return finding;
    }

    private static void Touch(ToothFinding finding, Guid actor, DateTimeOffset now)
    {
        finding.UpdatedAtUtc = now;
        finding.UpdatedByUserId = actor;
    }

    private async Task<int> NextVersionAsync(Guid findingId, CancellationToken ct) =>
        (await db.ToothFindingVersions.AsNoTracking().Where(v => v.FindingId == findingId).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0) + 1;

    private void StageVersion(ToothFinding f, int number, string changeType, string? reason, Guid actor, DateTimeOffset now) =>
        db.ToothFindingVersions.Add(new ToothFindingVersion
        {
            Id = Guid.NewGuid(), FindingId = f.Id, PatientId = f.PatientId, VersionNumber = number, ChangeType = changeType, ToothKey = f.ToothKey, Surface = f.Surface,
            Condition = f.Condition, State = f.State, Status = f.Status, Reason = reason, ActorUserId = actor, OccurredAtUtc = now,
        });

    private async Task SaveAsync(Guid findingId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(ToothFinding), findingId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // two writers claimed the same next version, or the same new finding: the stale one reloads and looks again
            throw new ConcurrencyConflictException(nameof(ToothFinding), findingId);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: ToothPresenceErrors.Absent or ToothPresenceErrors.HasFindings or ToothPresenceErrors.Busy } sql)
        {
            // the database refused a change that would leave the tooth inconsistent (a concurrent writer got there first); say it as the service says it
            throw sql.Number switch
            {
                ToothPresenceErrors.Absent => AbsentRefusal(),
                ToothPresenceErrors.HasFindings => HasFindingsRefusal(),
                _ => new OdontogramException("tooth_busy", "Another change to this tooth was in progress at the same moment. Reload and try again.", 409),
            };
        }
    }

    /// <summary>
    /// A tooth recorded as absent (an Existing or Completed finding whose condition has the Absent effect, such as Missing tooth) takes no further finding except one that is itself about its
    /// presence: another Absent condition, or a Replacement such as an implant. A Planned or Diagnosed extraction does not make the tooth absent yet.
    /// </summary>
    private async Task EnsureToothPresentAsync(Guid patientId, OdontogramRules.FindingFields f, CancellationToken ct)
    {
        if (f.Type.ToothEffect != ToothEffects.None) return;
        var absent = await (from x in db.ToothFindings.AsNoTracking()
                            join t in db.ConditionTypes.AsNoTracking() on x.Condition equals t.Code
                            where x.PatientId == patientId && x.ToothKey == f.ToothKey && x.Status == FindingStatuses.Active && t.ToothEffect == ToothEffects.Absent
                                  && (x.State == FindingStates.Existing || x.State == FindingStates.Completed)
                            select x.Id).AnyAsync(ct);
        if (absent)
            throw AbsentRefusal();
    }

    /// <summary>A finding in these states puts the tooth out of the mouth (an extraction planned or merely diagnosed does not).</summary>
    private static bool MakesToothAbsent(string state) => state is FindingStates.Existing or FindingStates.Completed;

    private static OdontogramException AbsentRefusal() =>
        new("tooth_absent", "This tooth is recorded as missing, so no other finding can be recorded on it. Record an implant, or withdraw the missing-tooth finding if it was a mistake.", 409);

    private static OdontogramException HasFindingsRefusal() =>
        new("tooth_has_findings", "This tooth has other active findings, so it cannot be recorded as missing yet. Withdraw them (with a reason such as the extraction) if they no longer apply, or keep the missing tooth as Planned until they are dealt with. Nothing was changed.", 409);

    /// <summary>
    /// The other half of the missing-tooth rule: a tooth cannot become absent (a missing-tooth finding recorded as Existing or Completed, or moved there) while other active findings that say
    /// nothing about its presence stand on it. The refusal is non-destructive - nothing is withdrawn or rewritten for the person - and names the way out. This read gives the clear message
    /// for the ordinary case; the database enforces the same rule atomically for concurrent writers (see <see cref="ToothPresenceErrors"/>).
    /// </summary>
    private async Task EnsureToothHasNoOtherFindingsAsync(Guid patientId, string toothKey, Guid exceptFindingId, CancellationToken ct)
    {
        var others = await (from x in db.ToothFindings.AsNoTracking()
                            join t in db.ConditionTypes.AsNoTracking() on x.Condition equals t.Code
                            where x.PatientId == patientId && x.ToothKey == toothKey && x.Status == FindingStatuses.Active && x.Id != exceptFindingId && t.ToothEffect == ToothEffects.None
                            select x.Id).AnyAsync(ct);
        if (others) throw HasFindingsRefusal();
    }

    /// <summary>Every condition's label by code, including retired ones, so a finding recorded under a condition that was later retired still reads as it did.</summary>
    private async Task<Dictionary<string, string>> LabelsAsync(CancellationToken ct) =>
        await db.ConditionTypes.AsNoTracking().ToDictionaryAsync(c => c.Code, c => c.Label, ct);

    private static FindingView ToView(ToothFinding f, IReadOnlyDictionary<Guid, string> names, IReadOnlyDictionary<string, string> labels, IReadOnlyList<LinkView> links) =>
        new(f.Id, f.ToothKey, f.Surface, f.Condition, labels.GetValueOrDefault(f.Condition, f.Condition), f.ConditionScope, f.State, f.Status, ClinicalNames.Name(names, f.CreatedByUserId), f.CreatedAtUtc,
            ClinicalNames.Name(names, f.UpdatedByUserId), f.UpdatedAtUtc, Convert.ToBase64String(f.RowVersion), links);
}
