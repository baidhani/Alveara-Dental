using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Safety;

/// <summary>
/// ALV-N011: patient-safety alerts - what a clinician states explicitly (a significant condition, pregnancy, anticoagulant status, an adverse reaction, a custom alert).
///
/// How the promises are kept:
/// - <b>Nothing is invented.</b> An alert exists only because a clinician created it and said where the information came from (a missing source is refused); allergies and
///   medications are read from the clinical record, never copied here, and nothing is inferred from unrelated data.
/// - <b>Acknowledged is not resolved.</b> Acknowledging records that one person saw one revision, in its own append-only table, and leaves the status untouched. Resolving needs a
///   reason and records who and when; reopening needs a reason too. An alert is never deleted, and every change appends a full version (who, when, why).
/// - <b>Stale data is refused, not merged.</b> A change echoes the alert's row version; acknowledging echoes the revision the person saw, so nobody acknowledges something that
///   changed under them.
/// - <b>One save per change.</b> The change, its version and its PHI-free audit entry (category and event only - never a title or a detail) are staged and saved once.
/// - <b>Repeats are quiet.</b> Creating the same active alert, resolving a resolved one, reopening an active one, saving unchanged values and acknowledging the same revision again
///   change nothing.
/// </summary>
public class SafetyAlertService(AlveraDbContext db, IPracticeClock clock)
{
    private SafetyContextService Context => new(db, clock);

    public async Task<SafetyContext> CreateAsync(Guid patientId, string? category, string? title, string? detail, string? severity, string? sourceNote, Guid? sourceItemId, Guid actor, CancellationToken ct)
    {
        var f = SafetyRules.ValidateAlert(category, title, detail, severity, sourceNote, categoryRequired: true);
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new SafetyException("patient_not_found", "That patient was not found.", 404);
        if (sourceItemId is { } sid)
        {
            var source = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.Id == sid).Select(i => new { i.PatientId, i.RemovedAtUtc }).SingleOrDefaultAsync(ct)
                ?? throw new SafetyException("source_not_found", "The clinical-record item this alert refers to was not found.", 404);
            if (source.PatientId != patientId) throw new SafetyException("source_patient_mismatch", "That clinical-record item belongs to a different patient.", 409);
            if (source.RemovedAtUtc is not null) throw new SafetyException("source_not_found", "The clinical-record item this alert refers to was removed.", 409);
        }

        var twin = await db.SafetyAlerts.AsNoTracking().Where(a => a.PatientId == patientId && a.Status == SafetyAlertStatuses.Active && a.Category == f.Category && a.Title == f.Title).SingleOrDefaultAsync(ct);
        if (twin is not null)
        {
            if (twin.Detail == f.Detail && twin.Severity == f.Severity && twin.SourceNote == f.Source) return await Context.GetAsync(patientId, actor, ct); // a retried create: already there
            throw new SafetyException("duplicate_alert", $"There is already an active {f.Category} alert named '{f.Title}'. Change that alert instead.", 409);
        }

        var now = clock.UtcNow;
        var alert = new SafetyAlert
        {
            Id = Guid.NewGuid(), PatientId = patientId, Category = f.Category, Title = f.Title, Detail = f.Detail, Severity = f.Severity, SourceNote = f.Source, SourceItemId = sourceItemId,
            Status = SafetyAlertStatuses.Active, Revision = 1, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.SafetyAlerts.Add(alert);
        StageVersion(alert, 1, SafetyChangeTypes.Created, null, actor, now);
        AuditService.Record(db, "SafetyAlertCreated", nameof(SafetyAlert), alert.Id, actor, "Safety alert created.");
        try
        {
            await SaveAsync(alert.Id, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // the same alert was created at the same moment (the unique index on active alerts): the first one stands, so this is a retry of it
            db.ChangeTracker.Clear();
            var winner = await db.SafetyAlerts.AsNoTracking().SingleOrDefaultAsync(a => a.PatientId == patientId && a.Status == SafetyAlertStatuses.Active && a.Category == f.Category && a.Title == f.Title, ct);
            if (winner is not null && winner.Detail == f.Detail && winner.Severity == f.Severity && winner.SourceNote == f.Source) return await Context.GetAsync(patientId, actor, ct);
            throw new SafetyException("duplicate_alert", $"There is already an active {f.Category} alert named '{f.Title}'. Change that alert instead.", 409);
        }
        return await Context.GetAsync(patientId, actor, ct);
    }

    public async Task<SafetyContext> UpdateAsync(Guid alertId, string? title, string? detail, string? severity, string? sourceNote, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var f = SafetyRules.ValidateAlert(null, title, detail, severity, sourceNote, categoryRequired: false);
        reason = SafetyRules.OptionalReason(reason);
        var alert = await LoadAsync(alertId, rowVersion, ct);
        if (alert.Status == SafetyAlertStatuses.Resolved) throw new SafetyException("alert_resolved", "This alert is resolved. Reopen it (with a reason) before changing it.", 409);
        if (alert.Title == f.Title && alert.Detail == f.Detail && alert.Severity == f.Severity && alert.SourceNote == f.Source) return await Context.GetAsync(alert.PatientId, actor, ct); // nothing to change

        var clash = await db.SafetyAlerts.AsNoTracking().AnyAsync(a => a.PatientId == alert.PatientId && a.Id != alertId && a.Status == SafetyAlertStatuses.Active && a.Category == alert.Category && a.Title == f.Title, ct);
        if (clash) throw new SafetyException("duplicate_alert", $"There is already an active {alert.Category} alert named '{f.Title}'.", 409);

        var now = clock.UtcNow;
        alert.Title = f.Title; alert.Detail = f.Detail; alert.Severity = f.Severity; alert.SourceNote = f.Source;
        Touch(alert, actor, now);
        StageVersion(alert, await NextVersionAsync(alertId, ct), SafetyChangeTypes.Changed, reason, actor, now);
        AuditService.Record(db, "SafetyAlertChanged", nameof(SafetyAlert), alert.Id, actor, "Safety alert changed.");
        await SaveAsync(alertId, ct);
        return await Context.GetAsync(alert.PatientId, actor, ct);
    }

    /// <summary>Resolves an alert. A reason is required; resolving a resolved alert changes nothing. Acknowledgement never gets here on its own: only this call resolves.</summary>
    public async Task<SafetyContext> ResolveAsync(Guid alertId, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = SafetyRules.RequireReason(reason, "Say why this alert is resolved.");
        var alert = await LoadAsync(alertId, rowVersion, ct);
        if (alert.Status == SafetyAlertStatuses.Resolved) return await Context.GetAsync(alert.PatientId, actor, ct);

        var now = clock.UtcNow;
        alert.Status = SafetyAlertStatuses.Resolved; alert.ResolvedAtUtc = now; alert.ResolvedByUserId = actor; alert.ResolutionReason = why;
        Touch(alert, actor, now);
        StageVersion(alert, await NextVersionAsync(alertId, ct), SafetyChangeTypes.Resolved, why, actor, now);
        AuditService.Record(db, "SafetyAlertResolved", nameof(SafetyAlert), alert.Id, actor, "Safety alert resolved.");
        await SaveAsync(alertId, ct);
        return await Context.GetAsync(alert.PatientId, actor, ct);
    }

    /// <summary>Reopens a resolved alert (a reason is required). What it said and who resolved it stay in the history; the current resolution fields are cleared.</summary>
    public async Task<SafetyContext> ReopenAsync(Guid alertId, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = SafetyRules.RequireReason(reason, "Say why this alert is being reopened.");
        var alert = await LoadAsync(alertId, rowVersion, ct);
        if (alert.Status == SafetyAlertStatuses.Active) return await Context.GetAsync(alert.PatientId, actor, ct);
        if (await db.SafetyAlerts.AsNoTracking().AnyAsync(a => a.PatientId == alert.PatientId && a.Id != alertId && a.Status == SafetyAlertStatuses.Active && a.Category == alert.Category && a.Title == alert.Title, ct))
            throw new SafetyException("duplicate_alert", $"There is already an active {alert.Category} alert named '{alert.Title}', so this one cannot be reopened.", 409);

        var now = clock.UtcNow;
        alert.Status = SafetyAlertStatuses.Active; alert.ResolvedAtUtc = null; alert.ResolvedByUserId = null; alert.ResolutionReason = null;
        Touch(alert, actor, now);
        StageVersion(alert, await NextVersionAsync(alertId, ct), SafetyChangeTypes.Reopened, why, actor, now);
        AuditService.Record(db, "SafetyAlertReopened", nameof(SafetyAlert), alert.Id, actor, "Safety alert reopened.");
        await SaveAsync(alertId, ct);
        return await Context.GetAsync(alert.PatientId, actor, ct);
    }

    /// <summary>
    /// Records that the caller has seen this revision of an active alert. It never changes the alert. The caller must say which revision they saw: if the alert changed since,
    /// the request is refused (<c>alert_changed</c>) so a clinician never acknowledges what they have not seen. Acknowledging again is quiet.
    /// </summary>
    public async Task<SafetyContext> AcknowledgeAsync(Guid alertId, int? revision, Guid actor, CancellationToken ct)
    {
        if (revision is null) throw new SafetyException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["revision"] = "Say which version of the alert was seen." });
        var alert = await db.SafetyAlerts.AsNoTracking().SingleOrDefaultAsync(a => a.Id == alertId, ct) ?? throw new SafetyException("alert_not_found", "That alert was not found.", 404);
        if (alert.Status == SafetyAlertStatuses.Resolved) throw new SafetyException("alert_resolved", "This alert is resolved, so there is nothing to acknowledge.", 409);
        if (alert.Revision != revision) throw new SafetyException("alert_changed", "This alert changed since you opened it. Review the new version, then acknowledge it.", 409);
        if (await db.SafetyAlertAcknowledgements.AsNoTracking().AnyAsync(k => k.AlertId == alertId && k.UserId == actor && k.Revision == alert.Revision, ct)) return await Context.GetAsync(alert.PatientId, actor, ct);

        var now = clock.UtcNow;
        db.SafetyAlertAcknowledgements.Add(new SafetyAlertAcknowledgement { Id = Guid.NewGuid(), AlertId = alertId, PatientId = alert.PatientId, UserId = actor, Revision = alert.Revision, AcknowledgedAtUtc = now });
        AuditService.Record(db, "SafetyAlertAcknowledged", nameof(SafetyAlert), alertId, actor, $"Safety alert acknowledged (still {alert.Status.ToLowerInvariant()}).");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // the same person acknowledged twice at once: the first stands
        }
        return await Context.GetAsync(alert.PatientId, actor, ct);
    }

    public async Task<AlertHistoryView> HistoryAsync(Guid alertId, CancellationToken ct)
    {
        if (!await db.SafetyAlerts.AsNoTracking().AnyAsync(a => a.Id == alertId, ct)) throw new SafetyException("alert_not_found", "That alert was not found.", 404);
        var versions = await db.SafetyAlertVersions.AsNoTracking().Where(v => v.AlertId == alertId).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => v.ActorUserId), ct);
        return new AlertHistoryView(alertId, versions.Select(v => new AlertVersionView(v.VersionNumber, v.ChangeType, v.Category, v.Title, v.Detail, v.Severity, v.SourceNote, v.Status, v.Reason, ClinicalNames.Name(names, v.ActorUserId), v.OccurredAtUtc)).ToList());
    }

    // ---------- shared ----------

    private async Task<SafetyAlert> LoadAsync(Guid alertId, string? rowVersion, CancellationToken ct)
    {
        var version = SafetyRules.ParseVersion(rowVersion);
        var alert = await db.SafetyAlerts.SingleOrDefaultAsync(a => a.Id == alertId, ct) ?? throw new SafetyException("alert_not_found", "That alert was not found.", 404);
        db.Entry(alert).Property(a => a.RowVersion).OriginalValue = version;
        return alert;
    }

    /// <summary>Every change a person who already saw the alert should see again moves the revision, so earlier acknowledgements stop counting.</summary>
    private static void Touch(SafetyAlert alert, Guid actor, DateTimeOffset now)
    {
        alert.Revision++;
        alert.UpdatedAtUtc = now;
        alert.UpdatedByUserId = actor;
    }

    private async Task<int> NextVersionAsync(Guid alertId, CancellationToken ct) =>
        (await db.SafetyAlertVersions.AsNoTracking().Where(v => v.AlertId == alertId).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0) + 1;

    private void StageVersion(SafetyAlert a, int number, string changeType, string? reason, Guid actor, DateTimeOffset now) =>
        db.SafetyAlertVersions.Add(new SafetyAlertVersion
        {
            Id = Guid.NewGuid(), AlertId = a.Id, PatientId = a.PatientId, VersionNumber = number, ChangeType = changeType, Category = a.Category, Title = a.Title, Detail = a.Detail, Severity = a.Severity,
            SourceNote = a.SourceNote, Status = a.Status, Reason = reason, ActorUserId = actor, OccurredAtUtc = now,
        });

    private async Task SaveAsync(Guid alertId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(SafetyAlert), alertId, ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // two writers claimed the same next version, or the same new alert: the stale one reloads and looks again
            throw new ConcurrencyConflictException(nameof(SafetyAlert), alertId);
        }
    }

    internal static bool IsUniqueViolation(DbUpdateException ex) => ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };
}
