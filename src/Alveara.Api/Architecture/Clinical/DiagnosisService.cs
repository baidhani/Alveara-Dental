using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Clinical;

/// <summary>
/// STORY-013: structured diagnoses, linked to the patient and the encounter they were made in, with an optional treatment-plan FORWARD reference.
///
/// How the promises are kept:
/// - <b>Linked to the patient and the encounter, authoritatively.</b> A diagnosis needs both; the encounter must exist and belong to the same patient (otherwise <c>encounter_not_found</c>, 404, which
///   does not reveal whether it exists for someone else), and the database refuses anything else and any later change of either link.
/// - <b>Incorrect data is refused, with every problem named.</b> An entry is judged whole by <see cref="DiagnosisRules"/> before anything is written (<c>validation_failed</c>, 400); one problem means
///   nothing is saved.
/// - <b>Every entry is logged, in the same save.</b> Recording, correcting and withdrawing each write a version of the diagnosis (who, when, why) and a PHI-free audit entry (never the label, the tooth,
///   the notes or the reference) with the signed-in user; if the log cannot be written the change is not saved and the caller is told so (<c>save_failed</c>, 503).
/// - <b>The treatment-plan reference is opaque and kept.</b> It is validated for shape, never looked up, shown as Unresolved, carried in every version, and removed or replaced only by an explicit,
///   recorded act (see <see cref="DiagnosisCorrection"/>); withdrawing a diagnosis keeps it.
/// - <b>Repeats are quiet.</b> Recording under the same idempotency key and the same entry returns the diagnosis already saved; the same key with a different entry is refused
///   (<c>idempotency_key_reused</c>, 409); a correction that changes nothing and withdrawing a withdrawn diagnosis change nothing. Two simultaneous saves under one key end with one diagnosis.
/// - <b>A stale edit is refused</b> through the row version (the shared 409 conflict). A diagnosis is never deleted: a wrong one is corrected or withdrawn with a reason.
/// ALV-013-C01 adds (in DiagnosisService.Structure.cs): optional coding and source provenance, tooth or region, amendment of that structure, the Active/Resolved/Withdrawn lifecycle and links to findings and
/// periodontal charts. Not handled (by design): resolving or validating the treatment-plan reference (a later story: it stays Unresolved) and code sets (no terminology content is bundled).
/// </summary>
public partial class DiagnosisService(AlveraDbContext db, IPracticeClock clock, ILogger<DiagnosisService>? logger = null)
{
    public const int ReasonMax = 500;
    public const int KeyMax = 64;

    // ---------- writes ----------

    public async Task<DiagnosisView> RecordAsync(Guid patientId, string? idempotencyKey, DiagnosisInput? input, Guid actor, CancellationToken ct)
    {
        var key = idempotencyKey?.Trim();
        var check = DiagnosisRules.Check(input);
        var problems = new List<DiagnosisProblem>(check.Problems);
        if (string.IsNullOrEmpty(key) || key.Length > KeyMax) problems.Add(new("idempotencyKey", "invalid", $"Send a key of 1 to {KeyMax} characters with each save, so a retry cannot create a second diagnosis."));
        if (problems.Count > 0) throw new DiagnosisException("validation_failed", "The diagnosis has entries that need correcting. Nothing was saved.", 400, problems);
        var v = check.Value!;

        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new DiagnosisException("patient_not_found", "That patient was not found.", 404);
        if (!await db.Encounters.AsNoTracking().AnyAsync(e => e.Id == v.EncounterId && e.PatientId == patientId, ct))
            throw new DiagnosisException("encounter_not_found", "That encounter was not found for this patient. Choose one of this patient's encounters.", 404, [new("encounterId", "not_found", "Choose one of this patient's encounters.")]);

        var existing = await FindByKeyAsync(patientId, key!, ct);
        if (existing is not null) return await ReplayAsync(existing, v, ct);

        var now = clock.UtcNow;
        var d = new Diagnosis
        {
            Id = Guid.NewGuid(), PatientId = patientId, EncounterId = v.EncounterId, IdempotencyKey = key!, Label = v.Label, ToothKey = v.ToothKey, Notes = v.Notes, TreatmentPlanReference = v.TreatmentPlanReference,
            CodingSystem = v.CodingSystem, Code = v.Code, Source = v.Source, SourceNote = v.SourceNote, RegionKey = v.RegionKey,
            Status = DiagnosisStatuses.Active, CreatedAtUtc = now, CreatedByUserId = actor,
        };
        db.Diagnoses.Add(d);
        StageVersion(d, 1, DiagnosisChangeTypes.Recorded, null, actor, now);
        AuditService.Record(db, "DiagnosisRecorded", nameof(Diagnosis), d.Id, actor, "Diagnosis recorded.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                  // a simultaneous save with the same key won: answer with its diagnosis (or refuse if it was a different entry)
            var winner = await FindByKeyAsync(patientId, key!, ct) ?? throw SaveFailed(ex);
            return await ReplayAsync(winner, v, ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
        return await ViewAsync(d.Id, ct);
    }

    /// <summary>Corrects a diagnosis (a reason is required and goes in its history). Changing nothing is quiet. The treatment-plan reference changes only by an explicit act (see <see cref="DiagnosisCorrection"/>).</summary>
    public async Task<DiagnosisView> CorrectAsync(Guid diagnosisId, string? rowVersion, DiagnosisCorrection? correction, string? reason, Guid actor, CancellationToken ct)
    {
        var problems = new List<DiagnosisProblem>();
        if (correction is null) throw new DiagnosisException("validation_failed", "Enter the corrected diagnosis. Nothing was saved.", 400, [new("diagnosis", "required", "Enter the corrected diagnosis.")]);
        var why = Reason(reason, problems);
        if (correction.ClearTreatmentPlanReference && correction.TreatmentPlanReference is not null)
            problems.Add(new("treatmentPlanReference", "conflict", "Either replace the treatment-plan reference or clear it, not both."));
        var d = await LoadAsync(diagnosisId, rowVersion, ct);
        if (d.Status == DiagnosisStatuses.Withdrawn) throw Withdrawn();

        // the corrected whole entry as the rules judge it: the reference is the current one unless the caller explicitly replaces or clears it
        var proposedReference = correction.ClearTreatmentPlanReference ? null : correction.TreatmentPlanReference ?? d.TreatmentPlanReference;
        var check = DiagnosisRules.Check(new DiagnosisInput(d.EncounterId, correction.Label, correction.ToothKey, correction.Notes, proposedReference, d.CodingSystem, d.Code, d.Source, d.SourceNote, d.RegionKey, correction.TreatmentPlanReferenceState));
        problems.AddRange(check.Problems);
        if (problems.Count > 0) throw new DiagnosisException("validation_failed", "The correction has entries that need correcting. Nothing was saved.", 400, problems);
        var v = check.Value!;

        if (v.Label == d.Label && v.ToothKey == d.ToothKey && v.Notes == d.Notes && v.TreatmentPlanReference == d.TreatmentPlanReference) return await ViewAsync(d.Id, ct);
        var now = clock.UtcNow;
        (d.Label, d.ToothKey, d.Notes, d.TreatmentPlanReference) = (v.Label, v.ToothKey, v.Notes, v.TreatmentPlanReference);
        Touch(d, actor, now);
        StageVersion(d, await NextVersionAsync(d.Id, ct), DiagnosisChangeTypes.Corrected, why, actor, now);
        AuditService.Record(db, "DiagnosisCorrected", nameof(Diagnosis), d.Id, actor, "Diagnosis corrected.");
        await SaveAsync(d.Id, ct);
        return await ViewAsync(d.Id, ct);
    }

    /// <summary>Withdraws a wrong diagnosis with a reason; it stays in the record and its history, treatment-plan reference included. Withdrawing a withdrawn diagnosis is quiet.</summary>
    public async Task<DiagnosisView> WithdrawAsync(Guid diagnosisId, string? rowVersion, string? reason, Guid actor, CancellationToken ct)
    {
        var problems = new List<DiagnosisProblem>();
        var why = Reason(reason, problems);
        if (problems.Count > 0) throw new DiagnosisException("reason_required", "Say why this diagnosis is being withdrawn. Nothing was changed.", 400, problems);
        var d = await db.Diagnoses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == diagnosisId, ct) ?? throw NotFound();
        if (d.Status == DiagnosisStatuses.Withdrawn) return await ViewAsync(d.Id, ct);

        var tracked = await LoadAsync(diagnosisId, rowVersion, ct);
        var now = clock.UtcNow;
        tracked.Status = DiagnosisStatuses.Withdrawn;
        (tracked.WithdrawnAtUtc, tracked.WithdrawnByUserId, tracked.WithdrawnReason) = (now, actor, why);
        Touch(tracked, actor, now);
        StageVersion(tracked, await NextVersionAsync(tracked.Id, ct), DiagnosisChangeTypes.Withdrawn, why, actor, now);
        AuditService.Record(db, "DiagnosisWithdrawn", nameof(Diagnosis), tracked.Id, actor, "Diagnosis withdrawn.");
        await SaveAsync(tracked.Id, ct);
        return await ViewAsync(tracked.Id, ct);
    }

    // ---------- reads ----------

    /// <summary>A patient's diagnoses (active and resolved), newest first; withdrawn ones only when asked for. <paramref name="encounterId"/> narrows to one encounter.</summary>
    public async Task<IReadOnlyList<DiagnosisView>> ListAsync(Guid patientId, Guid? encounterId, bool includeWithdrawn, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new DiagnosisException("patient_not_found", "That patient was not found.", 404);
        var rows = await db.Diagnoses.AsNoTracking().Where(d => d.PatientId == patientId && (encounterId == null || d.EncounterId == encounterId) && (includeWithdrawn || d.Status != DiagnosisStatuses.Withdrawn))
            .OrderByDescending(d => d.CreatedAtUtc).ThenByDescending(d => d.Id).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    public async Task<DiagnosisView> GetAsync(Guid diagnosisId, CancellationToken ct) => await ViewAsync(diagnosisId, ct);

    public async Task<DiagnosisHistoryView> HistoryAsync(Guid diagnosisId, CancellationToken ct)
    {
        if (!await db.Diagnoses.AsNoTracking().AnyAsync(d => d.Id == diagnosisId, ct)) throw NotFound();
        var versions = await db.DiagnosisVersions.AsNoTracking().Where(v => v.DiagnosisId == diagnosisId).OrderBy(v => v.VersionNumber).ToListAsync(ct);
        var names = await ClinicalNames.ResolveAsync(db, versions.Select(v => v.ActorUserId), ct);
        return new(diagnosisId, versions.Select(v => new DiagnosisVersionView(v.VersionNumber, v.ChangeType, v.Label, v.ToothKey, v.Notes, v.TreatmentPlanReference, State(v.TreatmentPlanReference), v.Status, v.Reason,
            ClinicalNames.Name(names, v.ActorUserId), v.OccurredAtUtc, v.CodingSystem, v.Code, v.Source, v.SourceNote, v.RegionKey)).ToList());
    }

    // ---------- plumbing ----------

    private static string? State(string? reference) => reference is null ? null : TreatmentPlanReferenceStates.Unresolved;
    private static DiagnosisException NotFound() => new("diagnosis_not_found", "That diagnosis was not found.", 404);
    private static DiagnosisException Withdrawn(string what = "corrected") => new("diagnosis_withdrawn", $"This diagnosis was withdrawn, so it cannot be {what}. Record a new diagnosis if one is needed.", 409);

    private static string? Reason(string? reason, List<DiagnosisProblem> problems)
    {
        var r = reason is null ? null : DiagnosisRules.NormalizeNotes(reason);
        if (string.IsNullOrEmpty(r)) { problems.Add(new("reason", "required", "Say why.")); return null; }
        if (r.Length > ReasonMax) { problems.Add(new("reason", "too_long", $"The reason can be at most {ReasonMax} characters; this one has {r.Length}.")); return null; }
        return r;
    }

    private Task<Diagnosis?> FindByKeyAsync(Guid patientId, string key, CancellationToken ct) => db.Diagnoses.AsNoTracking().SingleOrDefaultAsync(d => d.PatientId == patientId && d.IdempotencyKey == key, ct);

    /// <summary>The same key again: the diagnosis already saved if it is the same entry, otherwise a refusal that says the key belongs to a different one.</summary>
    private async Task<DiagnosisView> ReplayAsync(Diagnosis existing, NormalizedDiagnosis sent, CancellationToken ct)
    {
        var first = await db.DiagnosisVersions.AsNoTracking().SingleAsync(v => v.DiagnosisId == existing.Id && v.VersionNumber == 1, ct);   // what was originally recorded, whatever has been corrected since
        if (existing.EncounterId != sent.EncounterId || first.Label != sent.Label || first.ToothKey != sent.ToothKey || first.Notes != sent.Notes || first.TreatmentPlanReference != sent.TreatmentPlanReference
            || first.CodingSystem != sent.CodingSystem || first.Code != sent.Code || first.Source != sent.Source || first.SourceNote != sent.SourceNote || first.RegionKey != sent.RegionKey)
            throw new DiagnosisException("idempotency_key_reused", "That save key was already used for a different diagnosis. Nothing was changed; save again to record this one as a new diagnosis.", 409);
        return await ViewAsync(existing.Id, ct);
    }

    private static byte[] ParseVersion(string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new DiagnosisException("row_version_required", "The version of the diagnosis you are changing is required, so a change made by someone else is never overwritten. Reload and try again.", 400);
        try { return Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new DiagnosisException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400); }
    }

    private async Task<Diagnosis> LoadAsync(Guid diagnosisId, string? rowVersion, CancellationToken ct)
    {
        var expected = ParseVersion(rowVersion);
        var d = await db.Diagnoses.SingleOrDefaultAsync(x => x.Id == diagnosisId, ct) ?? throw NotFound();
        db.Entry(d).Property(x => x.RowVersion).OriginalValue = expected;
        return d;
    }

    private static void Touch(Diagnosis d, Guid actor, DateTimeOffset now) => (d.UpdatedAtUtc, d.UpdatedByUserId) = (now, actor);

    private void StageVersion(Diagnosis d, int number, string changeType, string? reason, Guid actor, DateTimeOffset now) =>
        db.DiagnosisVersions.Add(new DiagnosisVersion
        {
            Id = Guid.NewGuid(), DiagnosisId = d.Id, PatientId = d.PatientId, VersionNumber = number, ChangeType = changeType, Label = d.Label, ToothKey = d.ToothKey, Notes = d.Notes,
            TreatmentPlanReference = d.TreatmentPlanReference, CodingSystem = d.CodingSystem, Code = d.Code, Source = d.Source, SourceNote = d.SourceNote, RegionKey = d.RegionKey,
            Status = d.Status, Reason = reason, ActorUserId = actor, OccurredAtUtc = now,
        });

    private async Task<int> NextVersionAsync(Guid diagnosisId, CancellationToken ct) => (await db.DiagnosisVersions.Where(v => v.DiagnosisId == diagnosisId).MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0) + 1;

    private async Task SaveAsync(Guid diagnosisId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, nameof(Diagnosis), diagnosisId, ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                                  // two writers claimed the same next version: the later one is stale and must look again
            throw new ConcurrencyConflictException(nameof(Diagnosis), diagnosisId);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex);
        }
    }

    private DiagnosisException SaveFailed(DbUpdateException ex)
    {
        // error class only: never the diagnosis, the patient or the SQL text
        logger?.LogError("diagnosis_save_failed error_class={ErrorClass} sql_error={SqlError}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, (ex.InnerException as SqlException)?.Number);
        return new DiagnosisException("save_failed", "The diagnosis could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503);
    }

    private async Task<DiagnosisView> ViewAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Diagnoses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        return (await ViewsAsync([d], ct)).Single();
    }

    private async Task<List<DiagnosisView>> ViewsAsync(IReadOnlyList<Diagnosis> rows, CancellationToken ct)
    {
        if (rows.Count == 0) return [];
        var encounterIds = rows.Select(r => r.EncounterId).Distinct().ToList();
        var at = await db.Encounters.AsNoTracking().Where(e => encounterIds.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.EncounterAtUtc, ct);
        var names = await ClinicalNames.ResolveAsync(db, rows.SelectMany(r => new[] { r.CreatedByUserId, r.UpdatedByUserId, r.WithdrawnByUserId }), ct);
        var ids = rows.Select(r => r.Id).ToList();
        var links = await LinkViewsAsync(ids, ct);
        return rows.Select(d => new DiagnosisView(
            d.Id, d.PatientId, d.EncounterId, at[d.EncounterId], d.Label, d.ToothKey, d.Notes, d.TreatmentPlanReference, State(d.TreatmentPlanReference), d.Status,
            ClinicalNames.Name(names, d.CreatedByUserId), d.CreatedAtUtc, ClinicalNames.Name(names, d.UpdatedByUserId), d.UpdatedAtUtc, ClinicalNames.Name(names, d.WithdrawnByUserId), d.WithdrawnAtUtc, d.WithdrawnReason,
            Convert.ToBase64String(d.RowVersion), d.CodingSystem, d.Code, d.Source, d.SourceNote, d.RegionKey, links.GetValueOrDefault(d.Id) ?? [])).ToList();
    }
}
