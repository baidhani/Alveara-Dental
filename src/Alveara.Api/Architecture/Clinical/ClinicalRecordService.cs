using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// ALV-005-C01: a patient's longitudinal clinical record - medical and dental history, allergies and medications as they stand across encounters.
///
/// How the promises are kept:
/// - <b>History is never overwritten.</b> Every add, change, status change and removal appends a full <see cref="ClinicalRecordItemVersion"/> (who, when, why, and which
///   encounter); an item entered in error is marked removed and kept. A database trigger refuses any edit of the history.
/// - <b>One save per change</b>: the change, its version row, its PHI-free record event and its audit entry are staged on one context and saved ONCE.
/// - <b>Stale edits never overwrite</b>: a change to an item echoes the item's row version (the shared 409 concurrency conflict otherwise).
/// - <b>No invented facts.</b> A section is "none known", "unknown" or "reviewed" only because a clinician said so, with who and when; "not reviewed" is the absence of
///   any statement. A section that says none known/unknown cannot hold items, and adding one clears that statement.
/// - <b>Repeats are quiet.</b> Adding an identical live item, setting the status an item already has, removing a removed item and re-stating a review change nothing.
/// - <b>Audit and history text is PHI-free</b>: section names only; names, reactions, doses and details live in the clinical tables behind the clinical permissions.
/// </summary>
public class ClinicalRecordService(AlveraDbContext db, IPracticeClock clock)
{
    private ClinicalRecordReader Reader => new(db);

    // ---------- items ----------

    public Task<ClinicalRecordView> AddItemAsync(Guid patientId, string? kind, EntryFields fields, string? status, Guid? encounterId, Guid actor, CancellationToken ct) =>
        AddItemAsync(patientId, kind, fields, status, encounterId, actor, ct, retried: false);

    /// <summary>
    /// Adding an item to a section that says "none known" or "unknown" also deletes that statement. When two clinicians add at the same moment, the second finds the statement already
    /// gone and its delete matches no row: that is a benign race (the statement IS withdrawn), so the add is re-read and tried once more rather than failed.
    /// </summary>
    private async Task<ClinicalRecordView> AddItemAsync(Guid patientId, string? kind, EntryFields fields, string? status, Guid? encounterId, Guid actor, CancellationToken ct, bool retried)
    {
        kind = EncounterRules.RequireKind(kind);
        var f = EncounterRules.Validate(kind, fields);
        status = ClinicalRecordRules.RequireStatus(kind, status ?? ClinicalItemStatuses.Active);
        await RequireActivePatientAsync(patientId, ct);
        await RequireOpenEncounterAsync(patientId, encounterId, ct);

        var existing = await FindLiveByNameAsync(patientId, kind, f.Name!, ct);
        if (existing is not null) return await ReplayOrRefuseDuplicateAsync(existing, f, status, ct);

        var now = clock.UtcNow;
        var item = new ClinicalRecordItem
        {
            Id = Guid.NewGuid(), PatientId = patientId, Kind = kind, Name = f.Name!, Detail = f.Detail, Reaction = f.Reaction, Severity = f.Severity,
            Dose = f.Dose, Frequency = f.Frequency, Status = status, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.ClinicalRecordItems.Add(item);
        StageVersion(item, 1, ClinicalChangeTypes.Added, null, encounterId, actor, now);
        await ClearStatementIfAnyAsync(patientId, kind, "added an item", encounterId, actor, now, ct);
        StageEvent(patientId, kind, "ItemAdded", $"{EncounterRules.Label(kind)}: item added.", encounterId, actor, now);
        AuditService.Record(db, "ClinicalRecordItemAdded", nameof(ClinicalRecordItem), item.Id, actor, $"{EncounterRules.Label(kind)}: item added.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) when (!retried)
        {
            db.ChangeTracker.Clear();
            return await AddItemAsync(patientId, kind, fields, status, encounterId, actor, ct, retried: true);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // the same item arrived twice at once: the first one won
            var winner = await FindLiveByNameAsync(patientId, kind, f.Name!, ct) ?? throw new ClinicalException("duplicate_item", "That item was just added by someone else. Reload the record.", 409);
            return await ReplayOrRefuseDuplicateAsync(winner, f, status, ct);
        }
        return await Reader.GetAsync(patientId, ct);
    }

    public async Task<ClinicalRecordView> UpdateItemAsync(Guid itemId, EntryFields fields, string? rowVersion, string? reason, Guid? encounterId, Guid actor, CancellationToken ct)
    {
        reason = ClinicalRecordRules.CleanReason(reason);
        var item = await LoadItemAsync(itemId, rowVersion, ct);
        var f = EncounterRules.Validate(item.Kind, fields);
        await RequireOpenEncounterAsync(item.PatientId, encounterId, ct);
        if (SameFields(item, f)) return await Reader.GetAsync(item.PatientId, ct);

        var clash = await db.ClinicalRecordItems.AsNoTracking().AnyAsync(x => x.PatientId == item.PatientId && x.Kind == item.Kind && x.RemovedAtUtc == null && x.Id != itemId && x.Name == f.Name, ct);
        if (clash) throw new ClinicalException("duplicate_item", $"'{f.Name}' is already listed under {EncounterRules.Label(item.Kind)}.", 409);

        var now = clock.UtcNow;
        item.Name = f.Name!; item.Detail = f.Detail; item.Reaction = f.Reaction; item.Severity = f.Severity; item.Dose = f.Dose; item.Frequency = f.Frequency;
        Stamp(item, actor, now);
        StageVersion(item, await NextVersionAsync(itemId, ct), ClinicalChangeTypes.Changed, reason, encounterId, actor, now);
        StageEvent(item.PatientId, item.Kind, "ItemChanged", $"{EncounterRules.Label(item.Kind)}: item changed.", encounterId, actor, now);
        AuditService.Record(db, "ClinicalRecordItemChanged", nameof(ClinicalRecordItem), item.Id, actor, $"{EncounterRules.Label(item.Kind)}: item changed.");
        return await SaveAsync(item, ct);
    }

    public async Task<ClinicalRecordView> SetStatusAsync(Guid itemId, string? status, string? rowVersion, string? reason, Guid? encounterId, Guid actor, CancellationToken ct)
    {
        reason = ClinicalRecordRules.CleanReason(reason);
        var item = await LoadItemAsync(itemId, rowVersion, ct);
        status = ClinicalRecordRules.RequireStatus(item.Kind, status);
        await RequireOpenEncounterAsync(item.PatientId, encounterId, ct);
        if (item.Status == status) return await Reader.GetAsync(item.PatientId, ct);

        var now = clock.UtcNow;
        item.Status = status;
        Stamp(item, actor, now);
        StageVersion(item, await NextVersionAsync(itemId, ct), ClinicalChangeTypes.StatusChanged, reason, encounterId, actor, now);
        StageEvent(item.PatientId, item.Kind, "ItemStatusChanged", $"{EncounterRules.Label(item.Kind)}: status changed to {status}.", encounterId, actor, now);
        AuditService.Record(db, "ClinicalRecordItemStatusChanged", nameof(ClinicalRecordItem), item.Id, actor, $"{EncounterRules.Label(item.Kind)}: status changed to {status}.");
        return await SaveAsync(item, ct);
    }

    /// <summary>Marks an item as entered in error. It is kept with its history and a required reason; removing a removed item changes nothing.</summary>
    public async Task<ClinicalRecordView> RemoveInErrorAsync(Guid itemId, string? rowVersion, string? reason, Guid? encounterId, Guid actor, CancellationToken ct)
    {
        reason = ClinicalRecordRules.CleanReason(reason);
        if (reason is null) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, new Dictionary<string, string> { ["reason"] = "Say why it is being removed." });
        var item = await LoadItemAsync(itemId, rowVersion, ct, allowRemoved: true);
        if (item.RemovedAtUtc is not null) return await Reader.GetAsync(item.PatientId, ct);
        await RequireOpenEncounterAsync(item.PatientId, encounterId, ct);

        var now = clock.UtcNow;
        item.RemovedAtUtc = now; item.RemovedByUserId = actor;
        Stamp(item, actor, now);
        StageVersion(item, await NextVersionAsync(itemId, ct), ClinicalChangeTypes.RemovedInError, reason, encounterId, actor, now);
        StageEvent(item.PatientId, item.Kind, "ItemRemoved", $"{EncounterRules.Label(item.Kind)}: item removed as entered in error.", encounterId, actor, now);
        AuditService.Record(db, "ClinicalRecordItemRemoved", nameof(ClinicalRecordItem), item.Id, actor, $"{EncounterRules.Label(item.Kind)}: item removed as entered in error.");
        return await SaveAsync(item, ct);
    }

    // ---------- section statements ----------

    /// <summary>
    /// Records what a clinician says about a section: the items listed are current (Reviewed), nothing is known (NoneKnown), it could not be established (Unknown), or the
    /// statement is withdrawn (NotReviewed). None known and unknown need an empty section; Reviewed needs at least one item. Saying the same thing again changes nothing.
    /// </summary>
    public async Task<ClinicalRecordView> SetReviewAsync(Guid patientId, string? section, string? state, Guid? encounterId, Guid actor, CancellationToken ct)
    {
        section = EncounterRules.RequireKind(section);
        state = ClinicalRecordRules.RequireReviewState(state);
        await RequireActivePatientAsync(patientId, ct);
        await RequireOpenEncounterAsync(patientId, encounterId, ct);

        var live = await db.ClinicalRecordItems.AsNoTracking().Where(i => i.PatientId == patientId && i.Kind == section && i.RemovedAtUtc == null).ToListAsync(ct);
        var review = await db.ClinicalSectionReviews.SingleOrDefaultAsync(r => r.PatientId == patientId && r.Section == section, ct);
        var now = clock.UtcNow;
        var label = EncounterRules.Label(section);

        if (state == ClinicalReviewStates.NotReviewed)
        {
            if (review is null) return await Reader.GetAsync(patientId, ct);
            db.ClinicalSectionReviews.Remove(review);
            StageEvent(patientId, section, "ReviewCleared", $"{label}: review withdrawn.", encounterId, actor, now);
            AuditService.Record(db, "ClinicalSectionReviewCleared", nameof(ClinicalSectionReview), review.Id, actor, $"{label}: review withdrawn.");
        }
        else
        {
            if (state == ClinicalReviewStates.Reviewed && live.Count == 0)
                throw new ClinicalException("section_empty", $"{label} has nothing listed to confirm. Mark it none known or unknown instead.", 409);
            if (state != ClinicalReviewStates.Reviewed && live.Count > 0)
                throw new ClinicalException("section_has_items", $"{label} already has items, so it cannot be marked {(state == ClinicalReviewStates.NoneKnown ? "none known" : "unknown")}.", 409);
            if (review is not null && review.State == state && (state != ClinicalReviewStates.Reviewed || review.ReviewedAtUtc >= live.Max(i => i.UpdatedAtUtc ?? i.CreatedAtUtc)))
                return await Reader.GetAsync(patientId, ct); // already said, and (for a confirmation) nothing changed since: nothing to record

            if (review is null)
            {
                review = new ClinicalSectionReview { Id = Guid.NewGuid(), PatientId = patientId, Section = section, State = state, ReviewedAtUtc = now, ReviewedByUserId = actor };
                db.ClinicalSectionReviews.Add(review);
            }
            else
            {
                review.State = state; review.ReviewedAtUtc = now; review.ReviewedByUserId = actor;
            }
            var said = state switch { ClinicalReviewStates.Reviewed => "reviewed and confirmed current", ClinicalReviewStates.NoneKnown => "reviewed, none known", _ => "marked unknown" };
            StageEvent(patientId, section, "SectionReviewed", $"{label}: {said}.", encounterId, actor, now);
            AuditService.Record(db, "ClinicalSectionReviewed", nameof(ClinicalSectionReview), review.Id, actor, $"{label}: {said}.");
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear(); // the statement was changed or withdrawn by someone else at the same moment: theirs stands, and the record below shows it
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear(); // two clinicians stated the same section at once: the first statement stands, this one was already made
        }
        return await Reader.GetAsync(patientId, ct);
    }

    // ---------- shared ----------

    private async Task RequireActivePatientAsync(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => new { p.IsActive }).SingleOrDefaultAsync(ct)
            ?? throw new ClinicalException("patient_not_found", "That patient was not found.", 404);
        if (!patient.IsActive) throw new ClinicalException("patient_inactive", "This patient is inactive. Reactivate them before changing the clinical record.", 409);
    }

    /// <summary>A change made during an encounter must name an open (unsigned draft) encounter of the same patient.</summary>
    private async Task RequireOpenEncounterAsync(Guid patientId, Guid? encounterId, CancellationToken ct)
    {
        if (encounterId is null) return;
        var e = await db.Encounters.AsNoTracking().Where(x => x.Id == encounterId).Select(x => new { x.PatientId, x.Status, x.SignedAtUtc }).SingleOrDefaultAsync(ct)
            ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        if (e.PatientId != patientId) throw new ClinicalException("encounter_patient_mismatch", "That encounter belongs to a different patient.", 409);
        if (e.Status != EncounterStatuses.Draft || e.SignedAtUtc is not null)
            throw new ClinicalException("encounter_not_open", "That encounter is no longer open for changes. Make the change without linking it to the encounter.", 409);
    }

    private async Task<ClinicalRecordItem> LoadItemAsync(Guid itemId, string? rowVersion, CancellationToken ct, bool allowRemoved = false)
    {
        var item = await db.ClinicalRecordItems.SingleOrDefaultAsync(i => i.Id == itemId, ct) ?? throw new ClinicalException("item_not_found", "That item was not found.", 404);
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ClinicalException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            db.Entry(item).Property(i => i.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ClinicalException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
        if (item.RemovedAtUtc is not null && !allowRemoved) throw new ClinicalException("item_removed", "That item was removed as entered in error.", 409);
        return item;
    }

    private Task<ClinicalRecordItem?> FindLiveByNameAsync(Guid patientId, string kind, string name, CancellationToken ct) =>
        db.ClinicalRecordItems.AsNoTracking().Where(i => i.PatientId == patientId && i.Kind == kind && i.RemovedAtUtc == null && i.Name == name).SingleOrDefaultAsync(ct);

    private async Task<ClinicalRecordView> ReplayOrRefuseDuplicateAsync(ClinicalRecordItem existing, EntryFields f, string status, CancellationToken ct)
    {
        if (SameFields(existing, f, ignoreNameCase: true) && existing.Status == status) return await Reader.GetAsync(existing.PatientId, ct); // a retried add: already there
        throw new ClinicalException("duplicate_item", $"'{f.Name}' is already listed under {EncounterRules.Label(existing.Kind)} (status {existing.Status}). Change that item instead.", 409);
    }

    /// <summary>Adding an item contradicts "none known" / "unknown" for the section, so that statement is withdrawn in the same save (and the withdrawal is recorded).</summary>
    private async Task ClearStatementIfAnyAsync(Guid patientId, string kind, string because, Guid? encounterId, Guid actor, DateTimeOffset now, CancellationToken ct)
    {
        var review = await db.ClinicalSectionReviews.SingleOrDefaultAsync(r => r.PatientId == patientId && r.Section == kind && r.State != ClinicalReviewStates.Reviewed, ct);
        if (review is null) return;
        db.ClinicalSectionReviews.Remove(review);
        StageEvent(patientId, kind, "ReviewCleared", $"{EncounterRules.Label(kind)}: review withdrawn because someone {because}.", encounterId, actor, now);
    }

    private async Task<int> NextVersionAsync(Guid itemId, CancellationToken ct) => (await db.ClinicalRecordItemVersions.AsNoTracking().Where(v => v.ItemId == itemId).Select(v => (int?)v.VersionNumber).MaxAsync(ct) ?? 0) + 1;

    private static void Stamp(ClinicalRecordItem item, Guid actor, DateTimeOffset now)
    {
        item.UpdatedAtUtc = now;
        item.UpdatedByUserId = actor;
    }

    private void StageVersion(ClinicalRecordItem item, int number, string changeType, string? reason, Guid? encounterId, Guid actor, DateTimeOffset now) =>
        db.ClinicalRecordItemVersions.Add(new ClinicalRecordItemVersion
        {
            Id = Guid.NewGuid(), ItemId = item.Id, PatientId = item.PatientId, VersionNumber = number, ChangeType = changeType, Kind = item.Kind, Name = item.Name, Detail = item.Detail,
            Reaction = item.Reaction, Severity = item.Severity, Dose = item.Dose, Frequency = item.Frequency, Status = item.Status, Reason = reason, EncounterId = encounterId,
            ActorUserId = actor, OccurredAtUtc = now,
        });

    private void StageEvent(Guid patientId, string section, string eventType, string detail, Guid? encounterId, Guid actor, DateTimeOffset now) =>
        db.ClinicalRecordEvents.Add(new ClinicalRecordEvent { Id = Guid.NewGuid(), PatientId = patientId, Section = section, EventType = eventType, ActorUserId = actor, OccurredAtUtc = now, EncounterId = encounterId, Detail = detail });

    private async Task<ClinicalRecordView> SaveAsync(ClinicalRecordItem item, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(ClinicalRecordItem), item.Id, ct);
        }
        catch (DbUpdateException ex) when (ClinicalWrite.IsUniqueViolation(ex))
        {
            // two writers took the same next version number, or the same new name: the stale one must reload and look again
            throw new ConcurrencyConflictException(nameof(ClinicalRecordItem), item.Id);
        }
        return await Reader.GetAsync(item.PatientId, ct);
    }

    private static bool SameFields(ClinicalRecordItem e, EntryFields f, bool ignoreNameCase = false) =>
        string.Equals(e.Name, f.Name, ignoreNameCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && e.Detail == f.Detail && e.Reaction == f.Reaction && e.Severity == f.Severity && e.Dose == f.Dose && e.Frequency == f.Frequency;
}
