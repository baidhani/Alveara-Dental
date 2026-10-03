using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Lifecycle;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// STORY-005: documenting an encounter - structured medical and dental history, allergies and medications - finalizing it, and amending it afterwards.
///
/// How the promises are kept:
/// - <b>One save per change.</b> Every write stages the change, its history event and its audit entry on the same context and saves ONCE, so the change and its
///   record stand or fall together: if the audit entry cannot be written, the change is not made (and the same request can simply be retried).
/// - <b>Stale edits never overwrite.</b> Every change to a draft echoes the encounter's row version; every accepted change moves it, so two clinicians editing the
///   same note cannot silently overwrite each other (the shared 409 concurrency conflict).
/// - <b>Finalized means finalized.</b> Once finalized nothing in the encounter changes (the service refuses it, and database triggers refuse it independently);
///   the only addition is an <see cref="EncounterAddendum"/>, appended beside the original.
/// - <b>No invented facts.</b> A section is complete when it has an entry or the clinician marked it "reviewed, none reported"; finalizing refuses while any
///   section is neither, and says which.
/// - <b>Repeats are quiet.</b> Starting with the same key, adding an identical entry, removing a removed entry, marking a marked section, finalizing a finalized
///   encounter and re-sending an addendum with the same key all return the current state and change nothing.
/// - <b>Audit text is PHI-free:</b> section names and ids only; names, reactions, doses and notes live in the clinical tables, behind the clinical permissions.
/// </summary>
public class EncounterService(AlveraDbContext db, IPracticeClock clock, ILogger<EncounterService>? logger = null)
{
    private static readonly IReadOnlySet<RecordLifecycleAction> AllowedFromDraft = new HashSet<RecordLifecycleAction> { RecordLifecycleAction.Finalize };
    private static readonly IReadOnlySet<RecordLifecycleAction> AllowedFromFinalized = new HashSet<RecordLifecycleAction> { RecordLifecycleAction.Addendum };

    private EncounterReader Reader => new(db);

    // ---------- start ----------

    /// <summary>
    /// Starts a draft encounter for a patient. With an appointment it is the appointment's one encounter (asking again returns it); with a start key a retried
    /// request returns the first result. <paramref name="encounterAtUtc"/> defaults to the appointment's start, or now.
    /// </summary>
    public async Task<(EncounterDetail Detail, bool Created)> StartAsync(Guid patientId, Guid? appointmentId, DateTimeOffset? encounterAtUtc, string? startKey, Guid actor, CancellationToken ct)
    {
        startKey = EncounterRules.Clean(startKey);
        if (startKey is { Length: > EncounterRules.KeyMax }) throw new ClinicalException("validation_failed", "The request key is too long.", 400);
        var now = clock.UtcNow;
        if (encounterAtUtc is { } given && given > now.AddDays(1))
            throw new ClinicalException("validation_failed", "An encounter cannot be dated more than a day ahead.", 400, new Dictionary<string, string> { ["encounterAtUtc"] = "Choose the time the encounter took place." });

        var patient = await db.Patients.AsNoTracking().Where(p => p.Id == patientId).Select(p => new { p.Id, p.IsActive }).SingleOrDefaultAsync(ct)
            ?? throw new ClinicalException("patient_not_found", "That patient was not found.", 404);
        if (!patient.IsActive) throw new ClinicalException("patient_inactive", "This patient is inactive. Reactivate them before documenting a new encounter.", 409);

        if (startKey is not null)
        {
            var replay = await db.Encounters.AsNoTracking().Where(e => e.StartKey == startKey).Select(e => new { e.Id, e.PatientId }).SingleOrDefaultAsync(ct);
            if (replay is not null)
            {
                if (replay.PatientId != patientId) throw new ClinicalException("idempotency_key_reused", "That request key was already used for a different patient.", 409);
                return (await Reader.GetAsync(replay.Id, ct), false);
            }
        }

        var at = encounterAtUtc ?? now;
        if (appointmentId is { } apptId)
        {
            var appointment = await db.Appointments.AsNoTracking().Where(a => a.Id == apptId).Select(a => new { a.PatientId, a.StartUtc }).SingleOrDefaultAsync(ct)
                ?? throw new ClinicalException("appointment_not_found", "That appointment was not found.", 404);
            if (appointment.PatientId != patientId) throw new ClinicalException("appointment_patient_mismatch", "That appointment belongs to a different patient.", 409);
            var existing = await db.Encounters.AsNoTracking().Where(e => e.AppointmentId == apptId).Select(e => e.Id).SingleOrDefaultAsync(ct);
            if (existing != Guid.Empty) return (await Reader.GetAsync(existing, ct), false);
            at = encounterAtUtc ?? appointment.StartUtc;
        }

        var encounter = new Encounter
        {
            Id = Guid.NewGuid(), PatientId = patientId, AppointmentId = appointmentId, EncounterAtUtc = at, StartKey = startKey,
            Status = EncounterStatuses.Draft, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.Encounters.Add(encounter);
        Stage(encounter, EncounterEventTypes.Created, "EncounterStarted", actor, now, "Encounter started.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear(); // a simultaneous start for the same appointment or with the same key: use the winner
            var winner = await db.Encounters.AsNoTracking()
                .Where(e => (appointmentId != null && e.AppointmentId == appointmentId) || (startKey != null && e.StartKey == startKey)).Select(e => e.Id).SingleAsync(ct);
            return (await Reader.GetAsync(winner, ct), false);
        }
        return (await Reader.GetAsync(encounter.Id, ct), true);
    }

    // ---------- entries ----------

    public async Task<EncounterDetail> AddEntryAsync(Guid encounterId, string? kind, EntryFields fields, string? rowVersion, Guid actor, CancellationToken ct)
    {
        kind = EncounterRules.RequireKind(kind);
        var f = EncounterRules.Validate(kind, fields);
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);

        if (await db.EncounterSectionMarks.AnyAsync(m => m.EncounterId == encounterId && m.Section == kind, ct))
            throw new ClinicalException("section_marked_none_reported", $"{EncounterRules.Label(kind)} is marked reviewed, none reported. Clear that review before adding an entry.", 409);

        var duplicate = await db.EncounterEntries.AsNoTracking().Where(x => x.EncounterId == encounterId && x.Kind == kind && x.RemovedAtUtc == null && x.Name == f.Name).SingleOrDefaultAsync(ct);
        if (duplicate is not null)
        {
            if (Same(duplicate, f, ignoreNameCase: true)) return await Reader.GetAsync(encounterId, ct); // a retried add (even retyped in another case): already there
            throw new ClinicalException("duplicate_entry", $"'{f.Name}' is already listed under {EncounterRules.Label(kind)}. Change that entry instead.", 409);
        }

        var now = clock.UtcNow;
        db.EncounterEntries.Add(new EncounterEntry
        {
            Id = Guid.NewGuid(), EncounterId = encounterId, Kind = kind, Name = f.Name!, Detail = f.Detail, Reaction = f.Reaction, Severity = f.Severity,
            Dose = f.Dose, Frequency = f.Frequency, CreatedAtUtc = now, CreatedByUserId = actor,
        });
        Touch(encounter, actor, now);
        Stage(encounter, EncounterEventTypes.EntryAdded, "EncounterEntryAdded", actor, now, $"{EncounterRules.Label(kind)} entry added.");
        await SaveAsync(encounterId, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    public async Task<EncounterDetail> UpdateEntryAsync(Guid encounterId, Guid entryId, EntryFields fields, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        var entry = await db.EncounterEntries.SingleOrDefaultAsync(x => x.Id == entryId && x.EncounterId == encounterId, ct)
            ?? throw new ClinicalException("entry_not_found", "That entry was not found on this encounter.", 404);
        if (entry.RemovedAtUtc is not null) throw new ClinicalException("entry_removed", "That entry was removed. Add it again if it is still needed.", 409);

        var f = EncounterRules.Validate(entry.Kind, fields);
        if (Same(entry, f)) return await Reader.GetAsync(encounterId, ct); // nothing to change: no history, no new version

        var clash = await db.EncounterEntries.AsNoTracking().AnyAsync(x => x.EncounterId == encounterId && x.Kind == entry.Kind && x.RemovedAtUtc == null && x.Id != entryId && x.Name == f.Name, ct);
        if (clash) throw new ClinicalException("duplicate_entry", $"'{f.Name}' is already listed under {EncounterRules.Label(entry.Kind)}.", 409);

        var now = clock.UtcNow;
        entry.Name = f.Name!; entry.Detail = f.Detail; entry.Reaction = f.Reaction; entry.Severity = f.Severity; entry.Dose = f.Dose; entry.Frequency = f.Frequency;
        entry.UpdatedAtUtc = now; entry.UpdatedByUserId = actor;
        Touch(encounter, actor, now);
        Stage(encounter, EncounterEventTypes.EntryChanged, "EncounterEntryChanged", actor, now, $"{EncounterRules.Label(entry.Kind)} entry changed.");
        await SaveAsync(encounterId, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    /// <summary>Removes an entry from the active documentation. It is kept (who and when) and never deleted; removing a removed entry changes nothing.</summary>
    public async Task<EncounterDetail> RemoveEntryAsync(Guid encounterId, Guid entryId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        var entry = await db.EncounterEntries.SingleOrDefaultAsync(x => x.Id == entryId && x.EncounterId == encounterId, ct)
            ?? throw new ClinicalException("entry_not_found", "That entry was not found on this encounter.", 404);
        if (entry.RemovedAtUtc is not null) return await Reader.GetAsync(encounterId, ct);

        var now = clock.UtcNow;
        entry.RemovedAtUtc = now; entry.RemovedByUserId = actor;
        Touch(encounter, actor, now);
        Stage(encounter, EncounterEventTypes.EntryRemoved, "EncounterEntryRemoved", actor, now, $"{EncounterRules.Label(entry.Kind)} entry removed.");
        await SaveAsync(encounterId, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    // ---------- "reviewed, none reported" ----------

    /// <summary>Records that the clinician reviewed a section and there is nothing to report, so completing the documentation never needs an invented entry.</summary>
    public async Task<EncounterDetail> MarkSectionNoneReportedAsync(Guid encounterId, string? kind, string? rowVersion, Guid actor, CancellationToken ct)
    {
        kind = EncounterRules.RequireKind(kind);
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        if (await db.EncounterSectionMarks.AnyAsync(m => m.EncounterId == encounterId && m.Section == kind, ct)) return await Reader.GetAsync(encounterId, ct);
        if (await db.EncounterEntries.AnyAsync(x => x.EncounterId == encounterId && x.Kind == kind && x.RemovedAtUtc == null, ct))
            throw new ClinicalException("section_has_entries", $"{EncounterRules.Label(kind)} already has entries, so it cannot be marked none reported. Remove them first.", 409);

        var now = clock.UtcNow;
        db.EncounterSectionMarks.Add(new EncounterSectionMark { Id = Guid.NewGuid(), EncounterId = encounterId, Section = kind, State = EncounterSectionStates.NoneReported, MarkedAtUtc = now, MarkedByUserId = actor });
        Touch(encounter, actor, now);
        Stage(encounter, EncounterEventTypes.SectionMarked, "EncounterSectionReviewed", actor, now, $"{EncounterRules.Label(kind)} reviewed, none reported.");
        await SaveAsync(encounterId, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    public async Task<EncounterDetail> ClearSectionReviewAsync(Guid encounterId, string? kind, string? rowVersion, Guid actor, CancellationToken ct)
    {
        kind = EncounterRules.RequireKind(kind);
        var encounter = await LoadDraftAsync(encounterId, rowVersion, ct);
        var mark = await db.EncounterSectionMarks.SingleOrDefaultAsync(m => m.EncounterId == encounterId && m.Section == kind, ct);
        if (mark is null) return await Reader.GetAsync(encounterId, ct);

        var now = clock.UtcNow;
        db.EncounterSectionMarks.Remove(mark);
        Touch(encounter, actor, now);
        Stage(encounter, EncounterEventTypes.SectionUnmarked, "EncounterSectionReviewCleared", actor, now, $"{EncounterRules.Label(kind)} review cleared.");
        await SaveAsync(encounterId, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    // ---------- finalize ----------

    /// <summary>
    /// Finalizes a draft: every section must have an entry or be marked none reported (otherwise a 409 naming the sections that still need attention), and the
    /// caller must echo the version it reviewed. Finalizing a finalized encounter returns it unchanged.
    /// </summary>
    public async Task<EncounterDetail> FinalizeAsync(Guid encounterId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var encounter = await db.Encounters.SingleOrDefaultAsync(x => x.Id == encounterId, ct) ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        if (encounter.Status == EncounterStatuses.Finalized) return await Reader.GetAsync(encounterId, ct); // a repeat: already done
        ClinicalWrite.ApplyExpectedVersion(db, encounter, rowVersion);

        var detail = await Reader.GetAsync(encounterId, ct);
        if (!detail.IsComplete)
        {
            var errors = detail.MissingSections.ToDictionary(k => k, k => $"Record at least one entry or mark {EncounterRules.Label(k).ToLowerInvariant()} reviewed, none reported.");
            throw new ClinicalException("documentation_incomplete", "The documentation is not complete: " + string.Join(", ", detail.MissingSections.Select(EncounterRules.Label)) + " still need attention.", 409, errors);
        }
        RecordLifecycleGuard.EnsureAllowed(RecordLifecycleAction.Finalize, AllowedFromDraft);

        var now = clock.UtcNow;
        encounter.Status = EncounterStatuses.Finalized;
        encounter.FinalizedAtUtc = now; encounter.FinalizedByUserId = actor;
        Touch(encounter, actor, now);
        Stage(encounter, EncounterEventTypes.Finalized, "EncounterFinalized", actor, now, "Encounter finalized.");
        await SaveAsync(encounterId, ct);
        return await Reader.GetAsync(encounterId, ct);
    }

    // ---------- addendum ----------

    /// <summary>
    /// Adds an addendum to a FINALIZED encounter. The original is untouched; the addendum is appended with who and when. The client key makes a retry return the first
    /// result (the same key with different text is refused, never silently merged).
    /// </summary>
    public async Task<(EncounterDetail Detail, bool Created)> AddAddendumAsync(Guid encounterId, string? text, string? clientKey, Guid actor, CancellationToken ct)
    {
        text = EncounterRules.Clean(text);
        clientKey = EncounterRules.Clean(clientKey);
        var errors = new Dictionary<string, string>();
        if (text is null) errors["text"] = "Write the addendum.";
        else if (text.Length > EncounterRules.AddendumMax) errors["text"] = $"Keep the addendum to {EncounterRules.AddendumMax} characters or fewer.";
        if (clientKey is null) errors["clientKey"] = "A request key is required so a retry never adds the addendum twice.";
        else if (clientKey.Length > EncounterRules.KeyMax) errors["clientKey"] = "The request key is too long.";
        if (errors.Count > 0) throw new ClinicalException("validation_failed", "Some fields need attention.", 400, errors);

        var encounter = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == encounterId, ct) ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        if (encounter.Status != EncounterStatuses.Finalized)
            throw new ClinicalException("encounter_not_finalized", "Only a finalized encounter takes an addendum. Change the draft instead.", 409);
        RecordLifecycleGuard.EnsureAllowed(RecordLifecycleAction.Addendum, AllowedFromFinalized);

        var replay = await db.EncounterAddenda.AsNoTracking().SingleOrDefaultAsync(a => a.EncounterId == encounterId && a.ClientKey == clientKey, ct);
        if (replay is not null) return (await ReplayAsync(replay, text!, encounterId, ct), false);

        var now = clock.UtcNow;
        var addendum = new EncounterAddendum { Id = Guid.NewGuid(), EncounterId = encounterId, Text = text!, ClientKey = clientKey!, CreatedAtUtc = now, CreatedByUserId = actor };
        db.EncounterAddenda.Add(addendum);
        db.EncounterEvents.Add(new EncounterEvent { Id = Guid.NewGuid(), EncounterId = encounterId, PatientId = encounter.PatientId, EventType = EncounterEventTypes.AddendumAdded, ActorUserId = actor, OccurredAtUtc = now, Detail = "Addendum added." });
        AuditService.Record(db, "EncounterAddendumAdded", nameof(Encounter), encounterId, actor, $"Addendum {addendum.Id} added to a finalized encounter.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear(); // the same key arrived twice at once: the first one won
            var winner = await db.EncounterAddenda.AsNoTracking().SingleAsync(a => a.EncounterId == encounterId && a.ClientKey == clientKey, ct);
            return (await ReplayAsync(winner, text!, encounterId, ct), false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: >= 51030 and <= 51037 })
        {
            throw new ClinicalException("encounter_not_finalized", "Only a finalized encounter takes an addendum.", 409);
        }
        return (await Reader.GetAsync(encounterId, ct), true);
    }

    private async Task<EncounterDetail> ReplayAsync(EncounterAddendum existing, string text, Guid encounterId, CancellationToken ct)
    {
        if (existing.Text != text) throw new ClinicalException("idempotency_key_reused", "That request key was already used for a different addendum.", 409);
        return await Reader.GetAsync(encounterId, ct);
    }

    // ---------- shared ----------

    /// <summary>Loads the encounter for a change: the caller's version is applied (a stale one is a 409 at save), and only a draft can change.</summary>
    private async Task<Encounter> LoadDraftAsync(Guid encounterId, string? rowVersion, CancellationToken ct)
    {
        var encounter = await db.Encounters.SingleOrDefaultAsync(x => x.Id == encounterId, ct) ?? throw new ClinicalException("encounter_not_found", "That encounter was not found.", 404);
        ClinicalWrite.ApplyExpectedVersion(db, encounter, rowVersion);
        if (encounter.Status != EncounterStatuses.Draft)
            throw new ClinicalException("encounter_finalized", "This encounter is finalized and cannot be changed. Add an addendum to correct or extend it.", 409);
        return encounter;
    }

    private static void Touch(Encounter encounter, Guid actor, DateTimeOffset now)
    {
        encounter.UpdatedAtUtc = now;
        encounter.UpdatedByUserId = actor;
    }

    /// <summary>Stages the history row and the audit entry beside the change; the caller saves once.</summary>
    private void Stage(Encounter encounter, string eventType, string auditType, Guid actor, DateTimeOffset now, string detail)
    {
        db.EncounterEvents.Add(new EncounterEvent { Id = Guid.NewGuid(), EncounterId = encounter.Id, PatientId = encounter.PatientId, EventType = eventType, ActorUserId = actor, OccurredAtUtc = now, Detail = detail });
        AuditService.Record(db, auditType, nameof(Encounter), encounter.Id, actor, detail);
    }

    private async Task SaveAsync(Guid encounterId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Encounter), encounterId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: >= 51030 and <= 51037 })
        {
            // the database's own guard caught a write the service's check raced past (for example a finalize that committed in between)
            logger?.LogWarning("The database refused a clinical write the service had allowed (error {Number}) for encounter {EncounterId}.", ((SqlException)ex.InnerException!).Number, encounterId);
            throw new ClinicalException("encounter_finalized", "This encounter was finalized while you were editing it. Reload it; add an addendum to extend it.", 409);
        }
    }

    /// <summary>Whether an entry already holds exactly these values. A retried add ignores the case of the name (the database matches names that way); a change does not, so fixing a capital letter is a real change.</summary>
    private static bool Same(EncounterEntry e, EntryFields f, bool ignoreNameCase = false) =>
        string.Equals(e.Name, f.Name, ignoreNameCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && e.Detail == f.Detail && e.Reaction == f.Reaction && e.Severity == f.Severity && e.Dose == f.Dose && e.Frequency == f.Frequency;
}

/// <summary>The row-version rule shared by the clinical writes (the forms module has the same rule with its own exception type).</summary>
internal static class ClinicalWrite
{
    public static void ApplyExpectedVersion(AlveraDbContext db, Encounter encounter, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new ClinicalException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            db.Entry(encounter).Property(e => e.RowVersion).OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new ClinicalException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }
}
