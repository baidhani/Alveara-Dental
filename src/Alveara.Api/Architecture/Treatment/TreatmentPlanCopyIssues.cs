using System.Text.Json;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Alveara.Api.Architecture.Treatment;

/// <summary>
/// ALV-N026 S7 (D3): one issue of a treatment plan's patient copy as a PDF. It is append-only (the database refuses any change or deletion) and is owned through its plan only: it carries no
/// <c>PatientId</c> (ownership decision), so patient scope, access and merge follow the plan. No PDF is stored. A reprint is rebuilt from the plan revision recorded here (immutable), each procedure's
/// state up to the recorded cut-off (append-only), the immutable item, procedure version and procedure code, the diagnosis label as of <see cref="IssuedAtUtc"/> (immutable versions), and the envelope.
/// </summary>
public class TreatmentPlanCopyIssue
{
    public Guid Id { get; set; }
    public Guid PlanId { get; set; }
    /// <summary>The plan revision the copy shows (the plan's current revision at issue).</summary>
    public int RevisionNumber { get; set; }
    /// <summary>Item id to the highest <see cref="TreatmentPlanItemState.Sequence"/> recorded at issue (0 when the item had no state yet), for every procedure of the revision.</summary>
    public required string ItemStateCutoffJson { get; set; }
    /// <summary>The plan's reference code at issue (null when it had none). The code is set once, so a later code never changes a copy; a different code is an integrity mismatch.</summary>
    public string? ReferenceCodeAtIssue { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
    public Guid IssuedByUserId { get; set; }
    /// <summary>"v2" for every issue made here (the browser copy before S7 was v1 and was never recorded).</summary>
    public required string TemplateVersion { get; set; }
    /// <summary>The printed practice and identity text (<see cref="PlanCopyEnvelope"/>). Displayed history, not ownership.</summary>
    public required string EnvelopeJson { get; set; }
    public string? RenderBundleId { get; set; }
    /// <summary>Optional diagnostic; never an acceptance gate and never compared on reprint.</summary>
    public string? ContentHash { get; set; }
    /// <summary>Unique per plan: a retried issue returns the first one.</summary>
    public required string IdempotencyKey { get; set; }
    public Guid CorrelationId { get; set; }
}

public sealed record PlanCopyPractice(string? Name, string? Phone, string? AddressLine);
public sealed record PlanCopyEnvelope(PlanCopyPractice Practice, string PatientName, long Mrn);

/// <summary>The answer to issuing: the issue, and whether it already existed for that key.</summary>
public sealed record IssuedPlanCopy(TreatmentPlanCopyIssue Issue, bool Replayed);

/// <summary>
/// ALV-N026 S7: issues a treatment plan's patient copy and rebuilds an issue for its PDF. A withdrawn plan has no patient copy (<c>plan_withdrawn</c>), as on screen; a reprint of an earlier issue of a
/// plan withdrawn later still works (it is history). Any source that is missing or no longer matches what was recorded refuses with <c>integrity_mismatch</c> (409) before anything is rendered.
/// </summary>
public class TreatmentPlanCopyIssueService(AlveraDbContext db, IPracticeClock clock, ILogger<TreatmentPlanCopyIssueService> logger)
{
    public const string TemplateVersion = "v2";

    public async Task<IssuedPlanCopy> IssueAsync(Guid planId, string? idempotencyKey, string? renderBundleId, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim();
        if (string.IsNullOrEmpty(key) || key.Length > 100)
            throw new TreatmentPlanException("validation_failed", "An idempotency key of 1 to 100 characters is required.", 400);

        var existing = await db.TreatmentPlanCopyIssues.AsNoTracking().SingleOrDefaultAsync(i => i.PlanId == planId && i.IdempotencyKey == key, ct);
        if (existing is not null) return new IssuedPlanCopy(existing, true);

        var plan = await db.TreatmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct)
            ?? throw new TreatmentPlanException("plan_not_found", "That treatment plan was not found.", 404);
        if (plan.Status == TreatmentPlanStatuses.Withdrawn)
            throw new TreatmentPlanException("plan_withdrawn", "This treatment plan was withdrawn, so it has no patient copy.", 409);

        var revision = await db.TreatmentPlanRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.PlanId == planId && r.RevisionNumber == plan.CurrentRevision, ct)
            ?? throw Mismatch(planId, "revision_missing");
        var itemIds = await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => i.RevisionId == revision.Id).Select(i => i.ItemId).ToListAsync(ct);
        var highest = await db.TreatmentPlanItemStates.AsNoTracking().Where(s => s.PlanId == planId).GroupBy(s => s.ItemId).Select(g => new { g.Key, Max = g.Max(s => s.Sequence) }).ToDictionaryAsync(x => x.Key, x => x.Max, ct);
        var cutoff = itemIds.ToDictionary(id => id, id => highest.GetValueOrDefault(id));

        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == plan.PatientId, ct);
        var practice = await db.PracticeSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var envelope = new PlanCopyEnvelope(new PlanCopyPractice(Blank(practice?.Name), Blank(practice?.Phone), Blank(practice?.AddressLine)),
            string.Join(' ', new[] { patient.FirstName, patient.MiddleName, patient.LastName }.Where(x => !string.IsNullOrWhiteSpace(x))), patient.Mrn);

        var issue = new TreatmentPlanCopyIssue
        {
            Id = Guid.NewGuid(), PlanId = planId, RevisionNumber = revision.RevisionNumber, ItemStateCutoffJson = JsonSerializer.Serialize(cutoff), ReferenceCodeAtIssue = plan.ReferenceCode,
            IssuedAtUtc = clock.UtcNow, IssuedByUserId = actor, TemplateVersion = TemplateVersion, EnvelopeJson = JsonSerializer.Serialize(envelope), RenderBundleId = renderBundleId,
            IdempotencyKey = key, CorrelationId = Guid.NewGuid(),
        };
        db.TreatmentPlanCopyIssues.Add(issue);
        AuditService.Record(db, "TreatmentPlanCopyIssued", nameof(TreatmentPlanCopyIssue), issue.Id, actor, $"Patient copy of plan revision {revision.RevisionNumber} issued (template {TemplateVersion}).", correlationId: issue.CorrelationId);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            // a concurrent retry with the same key won: return its issue; any other failure is not swallowed
            var winner = await db.TreatmentPlanCopyIssues.AsNoTracking().SingleOrDefaultAsync(i => i.PlanId == planId && i.IdempotencyKey == key, ct);
            if (winner is null)
            {
                logger.LogWarning("plan_copy_issue_not_saved error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, issue.CorrelationId);
                throw new TreatmentPlanException("print_failed", "The patient copy could not be issued. Nothing was printed. Try again.", 503);
            }
            return new IssuedPlanCopy(winner, true);
        }
        return new IssuedPlanCopy(issue, false);
    }

    /// <summary>The issue and the patient it belongs to, resolved through its plan (never from the caller).</summary>
    public async Task<(TreatmentPlanCopyIssue Issue, Guid PatientId)> GetAsync(Guid issueId, CancellationToken ct)
    {
        var found = await db.TreatmentPlanCopyIssues.AsNoTracking().Where(i => i.Id == issueId)
            .Join(db.TreatmentPlans.AsNoTracking(), i => i.PlanId, p => p.Id, (i, p) => new { Issue = i, p.PatientId }).SingleOrDefaultAsync(ct)
            ?? throw new TreatmentPlanException("plan_copy_issue_not_found", "That patient copy was not found.", 404);
        return (found.Issue, found.PatientId);
    }

    /// <summary>
    /// The content of an issue, rebuilt only from immutable or versioned sources (brief 3.3). Every check runs before rendering: the recorded revision exists, every procedure in it has its cut-off and the
    /// state row at that cut-off, each diagnosis has a version at or before the issue, and the plan's reference code still equals the recorded one (null recorded stays null).
    /// </summary>
    public async Task<PlanCopyContent> ContentAsync(TreatmentPlanCopyIssue issue, CancellationToken ct)
    {
        var plan = await db.TreatmentPlans.AsNoTracking().SingleAsync(p => p.Id == issue.PlanId, ct);
        if (issue.ReferenceCodeAtIssue is not null && !string.Equals(plan.ReferenceCode, issue.ReferenceCodeAtIssue, StringComparison.Ordinal))
            throw Mismatch(issue.Id, "reference_code_changed");
        var revision = await db.TreatmentPlanRevisions.AsNoTracking().SingleOrDefaultAsync(r => r.PlanId == issue.PlanId && r.RevisionNumber == issue.RevisionNumber, ct)
            ?? throw Mismatch(issue.Id, "revision_missing");
        var cutoff = Read<Dictionary<Guid, int>>(issue, issue.ItemStateCutoffJson, "cutoff_unreadable");
        var envelope = Read<PlanCopyEnvelope>(issue, issue.EnvelopeJson, "envelope_unreadable");

        var placed = await db.TreatmentPlanRevisionItems.AsNoTracking().Where(i => i.RevisionId == revision.Id).ToListAsync(ct);
        if (placed.Count != cutoff.Count || placed.Any(p => !cutoff.ContainsKey(p.ItemId))) throw Mismatch(issue.Id, "cutoff_items_differ");
        var phases = await db.TreatmentPlanRevisionPhases.AsNoTracking().Where(p => p.RevisionId == revision.Id).OrderBy(p => p.PhaseNumber).ToListAsync(ct);

        var live = placed.Where(p => !p.IsWithdrawn).ToList();
        var liveIds = live.Select(p => p.ItemId).ToList();
        var items = await db.TreatmentPlanItems.AsNoTracking().Where(i => liveIds.Contains(i.Id) && i.PlanId == issue.PlanId).ToDictionaryAsync(i => i.Id, ct);
        if (items.Count != liveIds.Count) throw Mismatch(issue.Id, "item_missing");
        var versionIds = items.Values.Select(i => i.ProcedureVersionId).Distinct().ToList();
        var versions = await db.ProcedureVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var procedureIds = items.Values.Select(i => i.ProcedureId).Distinct().ToList();
        var codes = await db.ProcedureDefinitions.AsNoTracking().Where(p => procedureIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Code, ct);
        var diagnosisIds = items.Values.Select(i => i.DiagnosisId).Distinct().ToList();
        var diagnosisVersions = (await db.DiagnosisVersions.AsNoTracking().Where(v => diagnosisIds.Contains(v.DiagnosisId)).ToListAsync(ct)).ToLookup(v => v.DiagnosisId);
        var states = (await db.TreatmentPlanItemStates.AsNoTracking().Where(s => liveIds.Contains(s.ItemId)).ToListAsync(ct)).ToDictionary(s => (s.ItemId, s.Sequence));

        var lines = new List<PlanCopyLine>();
        foreach (var p in live)
        {
            var item = items[p.ItemId];
            if (!versions.TryGetValue(item.ProcedureVersionId, out var version) || version.ProcedureId != item.ProcedureId || !codes.TryGetValue(item.ProcedureId, out var code))
                throw Mismatch(issue.Id, "procedure_version_missing");
            var label = DiagnosisLabelAsOf(diagnosisVersions[item.DiagnosisId], issue.IssuedAtUtc) ?? throw Mismatch(issue.Id, "diagnosis_version_missing");
            TreatmentPlanItemState? state = null;
            if (cutoff[p.ItemId] > 0 && !states.TryGetValue((p.ItemId, cutoff[p.ItemId]), out state)) throw Mismatch(issue.Id, "item_state_missing");
            lines.Add(new PlanCopyLine(item.ItemNumber, p.PhaseNumber, code, version.Description, item.ToothKey, item.Surface, item.Fee, label,
                state?.Acceptance ?? ItemAcceptance.Pending, state?.Scheduling ?? ItemScheduling.Unscheduled, state?.Completion ?? ItemCompletion.NotStarted, state?.PlannedDate));
        }
        return new PlanCopyContent(revision.Title, revision.PatientExplanation, revision.RevisionNumber, issue.ReferenceCodeAtIssue, phases, lines.OrderBy(l => l.ItemNumber).ToList(), envelope);
    }

    /// <summary>
    /// The diagnosis label as of an instant: the version with the greatest <c>OccurredAtUtc</c> at or before it; on a tie the higher <c>VersionNumber</c> (a disclosed default). Null when no version
    /// existed by then (the caller refuses rather than guessing).
    /// </summary>
    public static string? DiagnosisLabelAsOf(IEnumerable<Clinical.DiagnosisVersion> versions, DateTimeOffset at) =>
        versions.Where(v => v.OccurredAtUtc <= at).OrderByDescending(v => v.OccurredAtUtc).ThenByDescending(v => v.VersionNumber).FirstOrDefault()?.Label;

    private T Read<T>(TreatmentPlanCopyIssue issue, string json, string reason)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json) ?? throw Mismatch(issue.Id, reason);
        }
        catch (JsonException)
        {
            throw Mismatch(issue.Id, reason);
        }
    }

    private TreatmentPlanException Mismatch(Guid id, string reason)
    {
        logger.LogWarning("plan_copy_integrity_mismatch error_class=IntegrityMismatch reason={Reason} id={Id}", reason, id);
        return new TreatmentPlanException("integrity_mismatch", "This patient copy cannot be reproduced exactly from the recorded plan, so it was not produced.", 409);
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public sealed record PlanCopyLine(int ItemNumber, int? PhaseNumber, string Code, string Description, string? ToothKey, string? Surface, decimal Fee, string DiagnosisLabel,
    string Acceptance, string Scheduling, string Completion, DateOnly? PlannedDate);

public sealed record PlanCopyContent(string Title, string? PatientExplanation, int RevisionNumber, string? ReferenceCode, IReadOnlyList<TreatmentPlanRevisionPhase> Phases,
    IReadOnlyList<PlanCopyLine> Lines, PlanCopyEnvelope Envelope);
