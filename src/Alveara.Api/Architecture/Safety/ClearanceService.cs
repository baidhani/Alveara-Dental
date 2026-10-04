using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Safety;

/// <summary>
/// ALV-N011: clearance tracking - a medical or dental clearance the practice needs before treatment: Requested -> Received -> Resolved (or Cancelled), each step with who, when and why.
///
/// - <b>Received is not resolved.</b> Receiving records that the answer arrived (with an optional note); only a separate resolve - which needs a reason - closes it, and only from
///   Received (a clearance still waiting is refused <c>clearance_not_received</c>, never resolved by assumption). Cancelling also needs a reason.
/// - <b>The document may not be available yet.</b> A clearance can be received (and even resolved) without a supporting document; it then says "document not yet attached" and the
///   reference can be attached later. The reference is free text until a documents story exists - it never pretends to be a stored file.
/// - Never deleted; every change appends a version (who, when, why) in the same save as the change and its PHI-free audit entry. A stale edit is refused through the row version;
///   repeats (requesting the same open clearance, receiving a received one, resolving a resolved one) are quiet.
/// </summary>
public class ClearanceService(AlveraDbContext db, IPracticeClock clock)
{
    private SafetyContextService Context => new(db, clock);

    public async Task<SafetyContext> RequestAsync(Guid patientId, string? kind, string? reason, string? requestedFrom, Guid actor, CancellationToken ct)
    {
        var f = SafetyRules.ValidateClearance(kind, reason, requestedFrom);
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new SafetyException("patient_not_found", "That patient was not found.", 404);

        var open = await db.Clearances.AsNoTracking().Where(c => c.PatientId == patientId && c.Kind == f.Kind && (c.Status == ClearanceStatuses.Requested || c.Status == ClearanceStatuses.Received) && c.Reason == f.Reason).FirstOrDefaultAsync(ct);
        if (open is not null) return await Context.GetAsync(patientId, actor, ct); // a retried request: already open

        var now = clock.UtcNow;
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(f.Reason.ToLowerInvariant())));
        var c2 = new Clearance { Id = Guid.NewGuid(), PatientId = patientId, Kind = f.Kind, Reason = f.Reason, ReasonKey = key, RequestedFrom = f.RequestedFrom, Status = ClearanceStatuses.Requested, RequestedAtUtc = now, RequestedByUserId = actor };
        db.Clearances.Add(c2);
        StageVersion(c2, 1, ClearanceChangeTypes.Requested, null, actor, now);
        AuditService.Record(db, "ClearanceRequested", nameof(Clearance), c2.Id, actor, $"{c2.Kind} clearance requested.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (SafetyAlertService.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // the same request arrived twice at once: the first one stands
        }
        return await Context.GetAsync(patientId, actor, ct);
    }

    /// <summary>Records that the clearance arrived. The document reference is optional ("not yet available"); receiving a received clearance changes nothing.</summary>
    public async Task<SafetyContext> ReceiveAsync(Guid id, string? note, string? documentReference, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var n = SafetyRules.OptionalNote(note);
        var doc = SafetyRules.OptionalDocument(documentReference);
        var c = await LoadAsync(id, rowVersion, ct);
        if (c.Status == ClearanceStatuses.Received) return await Context.GetAsync(c.PatientId, actor, ct);
        if (c.Status != ClearanceStatuses.Requested) throw Closed(c);

        var now = clock.UtcNow;
        c.Status = ClearanceStatuses.Received; c.ReceivedAtUtc = now; c.ReceivedByUserId = actor; c.ReceivedNote = n; c.DocumentReference = doc;
        Touch(c, actor, now);
        StageVersion(c, await NextVersionAsync(id, ct), ClearanceChangeTypes.Received, n, actor, now);
        AuditService.Record(db, "ClearanceReceived", nameof(Clearance), id, actor, $"{c.Kind} clearance received{(doc is null ? " (document not yet attached)" : "")}.");
        await SaveAsync(id, ct);
        return await Context.GetAsync(c.PatientId, actor, ct);
    }

    /// <summary>Attaches (or replaces) the supporting-document reference once it is available. Needs a received clearance; the earlier reference stays in the history.</summary>
    public async Task<SafetyContext> AttachDocumentAsync(Guid id, string? documentReference, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var doc = SafetyRules.RequireDocument(documentReference);
        var c = await LoadAsync(id, rowVersion, ct);
        if (c.Status == ClearanceStatuses.Cancelled) throw Closed(c);
        if (c.Status == ClearanceStatuses.Requested) throw new SafetyException("clearance_not_received", "The clearance has not been received yet, so there is no document to attach.", 409);
        if (c.DocumentReference == doc) return await Context.GetAsync(c.PatientId, actor, ct);

        var now = clock.UtcNow;
        c.DocumentReference = doc;
        Touch(c, actor, now);
        StageVersion(c, await NextVersionAsync(id, ct), ClearanceChangeTypes.DocumentAttached, null, actor, now);
        AuditService.Record(db, "ClearanceDocumentAttached", nameof(Clearance), id, actor, $"{c.Kind} clearance document attached.");
        await SaveAsync(id, ct);
        return await Context.GetAsync(c.PatientId, actor, ct);
    }

    /// <summary>Resolves a RECEIVED clearance. A reason is required; a clearance still waiting is refused; resolving a resolved one changes nothing.</summary>
    public async Task<SafetyContext> ResolveAsync(Guid id, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = SafetyRules.RequireReason(reason, "Say why this clearance is resolved.");
        var c = await LoadAsync(id, rowVersion, ct);
        if (c.Status == ClearanceStatuses.Resolved) return await Context.GetAsync(c.PatientId, actor, ct);
        if (c.Status == ClearanceStatuses.Cancelled) throw Closed(c);
        if (c.Status == ClearanceStatuses.Requested) throw new SafetyException("clearance_not_received", "A clearance must be received before it is resolved. Record that it arrived first.", 409);

        var now = clock.UtcNow;
        c.Status = ClearanceStatuses.Resolved; c.ClosedAtUtc = now; c.ClosedByUserId = actor; c.ClosingReason = why;
        Touch(c, actor, now);
        StageVersion(c, await NextVersionAsync(id, ct), ClearanceChangeTypes.Resolved, why, actor, now);
        AuditService.Record(db, "ClearanceResolved", nameof(Clearance), id, actor, $"{c.Kind} clearance resolved{(c.DocumentReference is null ? " without a supporting document" : "")}.");
        await SaveAsync(id, ct);
        return await Context.GetAsync(c.PatientId, actor, ct);
    }

    /// <summary>Withdraws a clearance that is no longer needed (a reason is required). It stays in the list, closed, with who and why.</summary>
    public async Task<SafetyContext> CancelAsync(Guid id, string? reason, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var why = SafetyRules.RequireReason(reason, "Say why this clearance is no longer needed.");
        var c = await LoadAsync(id, rowVersion, ct);
        if (c.Status == ClearanceStatuses.Cancelled) return await Context.GetAsync(c.PatientId, actor, ct);
        if (c.Status == ClearanceStatuses.Resolved) throw Closed(c);

        var now = clock.UtcNow;
        c.Status = ClearanceStatuses.Cancelled; c.ClosedAtUtc = now; c.ClosedByUserId = actor; c.ClosingReason = why;
        Touch(c, actor, now);
        StageVersion(c, await NextVersionAsync(id, ct), ClearanceChangeTypes.Cancelled, why, actor, now);
        AuditService.Record(db, "ClearanceCancelled", nameof(Clearance), id, actor, $"{c.Kind} clearance cancelled.");
        await SaveAsync(id, ct);
        return await Context.GetAsync(c.PatientId, actor, ct);
    }

    public async Task<ClearanceHistoryView> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Clearances.AsNoTracking().AnyAsync(c => c.Id == id, ct)) throw new SafetyException("clearance_not_found", "That clearance was not found.", 404);
        var versions = await db.ClearanceVersions.AsNoTracking().Where(v => v.ClearanceId == id).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => v.ActorUserId), ct);
        return new ClearanceHistoryView(id, versions.Select(v => new ClearanceVersionView(v.VersionNumber, v.ChangeType, v.Kind, v.Reason, v.RequestedFrom, v.Status, v.DocumentReference, v.Note, ClinicalNames.Name(names, v.ActorUserId), v.OccurredAtUtc)).ToList());
    }

    // ---------- shared ----------

    private static SafetyException Closed(Clearance c) => new("clearance_closed", $"This clearance is already {c.Status.ToLowerInvariant()} and cannot be changed.", 409);

    private async Task<Clearance> LoadAsync(Guid id, string? rowVersion, CancellationToken ct)
    {
        var version = SafetyRules.ParseVersion(rowVersion);
        var c = await db.Clearances.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new SafetyException("clearance_not_found", "That clearance was not found.", 404);
        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;
        return c;
    }

    private static void Touch(Clearance c, Guid actor, DateTimeOffset now)
    {
        c.UpdatedAtUtc = now;
        c.UpdatedByUserId = actor;
    }

    private async Task<int> NextVersionAsync(Guid id, CancellationToken ct) =>
        (await db.ClearanceVersions.AsNoTracking().Where(v => v.ClearanceId == id).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0) + 1;

    private void StageVersion(Clearance c, int number, string changeType, string? note, Guid actor, DateTimeOffset now) =>
        db.ClearanceVersions.Add(new ClearanceVersion
        {
            Id = Guid.NewGuid(), ClearanceId = c.Id, PatientId = c.PatientId, VersionNumber = number, ChangeType = changeType, Kind = c.Kind, Reason = c.Reason, RequestedFrom = c.RequestedFrom,
            Status = c.Status, DocumentReference = c.DocumentReference, Note = note, ActorUserId = actor, OccurredAtUtc = now,
        });

    private async Task SaveAsync(Guid id, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Clearance), id, ct);
        }
        catch (DbUpdateException ex) when (SafetyAlertService.IsUniqueViolation(ex))
        {
            throw new ConcurrencyConflictException(nameof(Clearance), id); // two writers claimed the same next version: the stale one reloads
        }
    }
}
