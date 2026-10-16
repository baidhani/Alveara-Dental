using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// ALV-016-C01: the prescription draft. A draft is a prescription being written: it can be saved incomplete (instructions, quantity and refills may wait), edited while it is a draft, and discarded, and it is the
/// only mutable record of the workflow. Finalizing it is <see cref="PrescriptionFinalizeService"/>.
///
/// How the promises are kept:
/// - <b>Nothing is lost or overwritten.</b> A draft is never deleted (the database refuses it); a change echoes the row version and a stale one is refused (409 <c>concurrency_conflict</c>); a finalized or discarded
///   draft never changes (<c>draft_closed</c>, 409).
/// - <b>Incorrect data is named, not saved.</b> The whole entry is judged by <see cref="PrescriptionWorkflowRules"/>; every problem is listed (<c>validation_failed</c>, 400).
/// - <b>The lists the prescriber saw are remembered, and a change is never silent.</b> A draft keeps a snapshot of the allergy and medication lists as last acknowledged. Edits do not refresh it; only an explicit
///   acknowledgement does, and only for the lists as they are now (see <see cref="AcknowledgeSafetyAsync"/>).
/// - <b>Every action is logged in the same save</b> with the signed-in user and the time (PHI-free: never a medication, dose or allergy); if the log cannot be written nothing is saved (<c>save_failed</c>, 503).
/// - <b>Repeats are quiet.</b> Creating twice under one key returns the draft; the same key for a different entry is refused (<c>idempotency_key_reused</c>, 409); discarding a discarded draft changes nothing; amending a
///   prescription that already has an open amendment returns that draft.
/// </summary>
public class PrescriptionDraftService(AlveraDbContext db, IPracticeClock clock, PrescribingSafetyReader reader, ILogger<PrescriptionDraftService>? logger = null)
{
    public static WorkflowOptions Options() =>
        new(PrescriptionRules.DoseUnits, PrescriptionRules.TypingCeilings, PrescriptionRules.Frequencies, PrescriptionWorkflowRules.QuantityUnits, PrescriptionWorkflowRules.QuantityTypingCeiling,
            PrescriptionWorkflowRules.RefillsMax, PrescriptionWorkflowRules.InstructionsMax, PrescriptionRules.MedicationMax, PrescriptionRules.ReviewNoteMax, false, DecisionSupport.Message,
            PrescriptionRules.DoseCheckLimitation, PrescriptionWorkflowRules.QuantityLimitation, AllergyCheck.Limitation);

    // ---------- reads ----------

    public async Task<IReadOnlyList<DraftView>> ListAsync(Guid patientId, bool includeClosed, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var rows = await db.PrescriptionDrafts.AsNoTracking().Where(d => d.PatientId == patientId && (includeClosed || d.Status == DraftStatuses.Draft))
            .OrderByDescending(d => d.UpdatedAtUtc).ThenByDescending(d => d.Id).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    public async Task<DraftDetailView> GetAsync(Guid draftId, Guid actor, CancellationToken ct)
    {
        var d = await FindAsync(draftId, ct);
        var read = await reader.ReadAsync(d.PatientId, actor, ct);
        return new DraftDetailView((await ViewsAsync([d], ct))[0], SafetyOf(d, read));
    }

    /// <summary>What finalizing the draft would meet right now: the problems, the safety check and the limitation texts. Writes nothing.</summary>
    public async Task<DraftCheck> CheckAsync(Guid draftId, Guid actor, CancellationToken ct)
    {
        var d = await FindAsync(draftId, ct);
        var problems = PrescriptionWorkflowRules.CheckFinal(InputOf(d)).Problems;
        var safety = SafetyOf(d, await reader.ReadAsync(d.PatientId, actor, ct));
        var open = d.Status == DraftStatuses.Draft;
        return new DraftCheck(problems, safety, open && problems.Count == 0 && !safety.ChangedSinceAcknowledged && safety.ReviewProblem is null,
            PrescriptionRules.DoseCheckLimitation, PrescriptionWorkflowRules.QuantityLimitation, AllergyCheck.Limitation, DecisionSupport.Message);
    }

    // ---------- writes ----------

    public async Task<SavedDraft> CreateAsync(Guid patientId, WorkflowInput? input, Guid actor, CancellationToken ct)
    {
        var check = PrescriptionWorkflowRules.CheckDraft(input);
        if (check.Problems.Count > 0) throw Invalid(check.Problems);
        var v = check.Value!;
        await RequirePatientAsync(patientId, ct);
        var hash = PrescriptionWorkflowRules.RequestHash(v);
        if (await FindByKeyAsync(patientId, v.IdempotencyKey, ct) is { } existing) return await ReplayAsync(existing, hash, ct);

        var read = await reader.ReadAsync(patientId, actor, ct);
        var now = clock.UtcNow;
        var draft = NewDraft(patientId, v, hash, read, null, actor, now);
        db.PrescriptionDrafts.Add(draft);
        var correlation = Guid.NewGuid();
        AuditService.Record(db, "PrescriptionDraftCreated", nameof(PrescriptionDraft), draft.Id, actor, "Prescription draft created.", correlationId: correlation);
        try
        {
            await SaveAsync(draft.Id, correlation, ct);
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();                                         // a simultaneous create under the same key won: answer with its draft (or refuse if it was a different entry)
            return await ReplayAsync(await FindByKeyAsync(patientId, v.IdempotencyKey, ct) ?? throw SaveFailed(ex, correlation), hash, ct);
        }
        return new SavedDraft((await ViewsAsync([draft], ct))[0], false);
    }

    public async Task<DraftView> UpdateAsync(Guid draftId, string? rowVersion, WorkflowInput? input, Guid actor, CancellationToken ct)
    {
        var check = PrescriptionWorkflowRules.CheckDraft(input is null ? null : input with { IdempotencyKey = input.IdempotencyKey ?? "update" });      // the key is not a field of the draft being edited
        if (check.Problems.Count > 0) throw Invalid(check.Problems);
        var v = check.Value!;
        var d = await LoadForChangeAsync(draftId, rowVersion, ct);
        if (d.Status != DraftStatuses.Draft) throw Closed(d);
        if (SameAs(d, v)) return (await ViewsAsync([d], ct))[0];             // nothing changed: nothing is saved and nothing is logged

        (d.Medication, d.DoseValue, d.DoseUnit, d.Frequency, d.Instructions, d.QuantityValue, d.QuantityUnit, d.Refills, d.ReviewNote) =
            (v.Medication, v.DoseValue, v.DoseUnit, v.Frequency, v.Instructions, v.QuantityValue, v.QuantityUnit, v.Refills, v.ReviewNote);
        Touch(d, actor);
        var correlation = Guid.NewGuid();
        AuditService.Record(db, "PrescriptionDraftChanged", nameof(PrescriptionDraft), d.Id, actor, "Prescription draft changed.", correlationId: correlation);
        await SaveAsync(d.Id, correlation, ct);
        return (await ViewsAsync([d], ct))[0];
    }

    /// <summary>
    /// The prescriber confirms they have seen the allergy and medication lists as they are now. <paramref name="seenFingerprint"/> is the fingerprint of the lists the screen showed; if the lists have changed
    /// again since, nothing is acknowledged and the change is reported (<c>safety_context_changed</c>, 409), so an acknowledgement is only ever of what was actually on screen.
    /// </summary>
    public async Task<DraftView> AcknowledgeSafetyAsync(Guid draftId, string? rowVersion, string? seenFingerprint, Guid actor, CancellationToken ct)
    {
        var d = await LoadForChangeAsync(draftId, rowVersion, ct);
        if (d.Status != DraftStatuses.Draft) throw Closed(d);
        var read = await reader.ReadAsync(d.PatientId, actor, ct);
        if (!string.Equals(seenFingerprint, read.Fingerprint, StringComparison.Ordinal)) throw SafetyChanged(d, read);
        if (d.SafetyFingerprint == read.Fingerprint) return (await ViewsAsync([d], ct))[0];

        (d.SafetyFingerprint, d.SafetySnapshot) = (read.Fingerprint, SafetySnapshot.Join(read.Lines));
        Touch(d, actor);
        var correlation = Guid.NewGuid();
        AuditService.Record(db, "PrescriptionDraftSafetyAcknowledged", nameof(PrescriptionDraft), d.Id, actor, "The prescriber acknowledged the allergy and medication lists.", correlationId: correlation);
        await SaveAsync(d.Id, correlation, ct);
        return (await ViewsAsync([d], ct))[0];
    }

    public async Task<DraftView> DiscardAsync(Guid draftId, string? rowVersion, string? reason, Guid actor, CancellationToken ct)
    {
        var d = await LoadForChangeAsync(draftId, rowVersion, ct);
        if (d.Status == DraftStatuses.Discarded) return (await ViewsAsync([d], ct))[0];
        if (d.Status != DraftStatuses.Draft) throw Closed(d);
        string? why = null;
        if (!string.IsNullOrWhiteSpace(reason))
        {
            var (text, problem) = PrescriptionWorkflowRules.CheckReason(reason);
            if (problem is not null) throw Invalid([problem]);
            why = text;
        }
        d.Status = DraftStatuses.Discarded;
        d.DiscardedReason = why;
        Touch(d, actor);
        var correlation = Guid.NewGuid();
        AuditService.Record(db, "PrescriptionDraftDiscarded", nameof(PrescriptionDraft), d.Id, actor, "Prescription draft discarded.", correlationId: correlation);
        await SaveAsync(d.Id, correlation, ct);
        return (await ViewsAsync([d], ct))[0];
    }

    /// <summary>
    /// Starts an amendment of a finalized prescription: a new draft with the same content, which replaces the original only when it is finalized. The original is never edited. An Active prescription can have one
    /// open amendment; asking again returns it. The prescriber's review note is not carried over: the new prescription needs its own.
    /// </summary>
    public async Task<SavedDraft> AmendAsync(Guid prescriptionId, string? key, Guid actor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) throw Invalid([new("idempotencyKey", "required", "A save key is required so that a retry cannot start the amendment twice.")]);
        if (key.Trim().Length > PrescriptionRules.KeyMax || key.Any(char.IsControl)) throw Invalid([new("idempotencyKey", "invalid", $"The save key must be 1 to {PrescriptionRules.KeyMax} characters without control characters.")]);
        key = key.Trim();
        var p = await db.Prescriptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == prescriptionId, ct) ?? throw new PrescriptionException("prescription_not_found", "That prescription was not found.", 404);
        var hash = PrescriptionWorkflowRules.AmendHash(p.Id);
        if (await FindByKeyAsync(p.PatientId, key, ct) is { } byKey) return await ReplayAsync(byKey, hash, ct);
        if (await db.PrescriptionDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SupersedesPrescriptionId == p.Id && d.Status == DraftStatuses.Draft, ct) is { } open)
            return new SavedDraft((await ViewsAsync([open], ct))[0], true);
        var status = PrescriptionStatuses.From(await db.PrescriptionEvents.AsNoTracking().Where(e => e.PrescriptionId == p.Id).Select(e => e.EventType).ToListAsync(ct));
        if (!PrescriptionStatuses.CanEnd(status)) throw new PrescriptionException("prescription_not_active", $"This prescription is {status.ToLowerInvariant()}, so it cannot be amended.", 409);

        var detail = await db.PrescriptionDetails.AsNoTracking().SingleOrDefaultAsync(x => x.PrescriptionId == p.Id, ct);
        var read = await reader.ReadAsync(p.PatientId, actor, ct);
        var v = new NormalizedWorkflow(p.Medication, p.DoseValue, p.DoseUnit, p.Frequency, detail?.Instructions, detail?.QuantityValue, detail?.QuantityUnit, detail?.Refills, null, key);
        var draft = NewDraft(p.PatientId, v, hash, read, p.Id, actor, clock.UtcNow);
        db.PrescriptionDrafts.Add(draft);
        var correlation = Guid.NewGuid();
        AuditService.Record(db, "PrescriptionAmendmentStarted", nameof(PrescriptionDraft), draft.Id, actor, "An amendment of a prescription was started as a draft.", correlationId: correlation);
        try
        {
            await SaveAsync(draft.Id, correlation, ct);
        }
        catch (DbUpdateException ex) when (IsUnique(ex))
        {
            db.ChangeTracker.Clear();                                         // a simultaneous amendment won: answer with it
            var winner = await FindByKeyAsync(p.PatientId, key, ct)
                ?? await db.PrescriptionDrafts.AsNoTracking().FirstOrDefaultAsync(d => d.SupersedesPrescriptionId == p.Id && d.Status == DraftStatuses.Draft, ct) ?? throw SaveFailed(ex, correlation);
            return new SavedDraft((await ViewsAsync([winner], ct))[0], true);
        }
        return new SavedDraft((await ViewsAsync([draft], ct))[0], false);
    }

    // ---------- shared with the finalize and lifecycle services ----------

    /// <summary>The draft's entry in the form the rules judge.</summary>
    internal static WorkflowInput InputOf(PrescriptionDraft d) =>
        new(d.Medication, d.DoseValue, d.DoseUnit, d.Frequency, d.Instructions, d.QuantityValue, d.QuantityUnit, d.Refills, d.ReviewNote, $"draft-{d.Id:N}");

    /// <summary>How the live allergy and medication lists stand against what the prescriber last acknowledged, and the allergy check for the draft's medication.</summary>
    internal static SafetyCheck SafetyOf(PrescriptionDraft d, SafetyRead read)
    {
        var assessment = read.Assess(d.Medication);
        var (added, removed) = SafetySnapshot.Diff(SafetySnapshot.Split(d.SafetySnapshot), read.Lines);
        return new SafetyCheck(read.Fingerprint, d.SafetyFingerprint != read.Fingerprint, added, removed, assessment, assessment.Outcome != AllergyOutcomes.NoRecordedMatch,
            PrescriptionRules.ReviewProblem(assessment, d.ReviewNote));
    }

    internal static PrescriptionException SafetyChanged(PrescriptionDraft d, SafetyRead read)
    {
        var (added, removed) = SafetySnapshot.Diff(SafetySnapshot.Split(d.SafetySnapshot), read.Lines);
        return new PrescriptionException("safety_context_changed",
            "The patient's allergy or medication list changed since you last looked. Nothing was saved. Read the changes, then acknowledge them to continue.", 409, detail: new SafetyChange(read.Fingerprint, added, removed));
    }

    internal static PrescriptionException Invalid(IReadOnlyList<PrescriptionProblem> problems) =>
        new("validation_failed", "The prescription has entries that need correcting. Nothing was saved.", 400, problems);

    internal static PrescriptionException Closed(PrescriptionDraft d) =>
        new("draft_closed", $"This draft was {d.Status.ToLowerInvariant()} and can no longer be changed.", 409);

    internal static bool IsUnique(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2601 or 2627 };

    internal PrescriptionException SaveFailed(Exception ex, Guid correlation)
    {
        logger?.LogWarning("prescription_workflow_not_saved error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, correlation);
        return new PrescriptionException("save_failed", "The prescription could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503);
    }

    // ---------- helpers ----------

    private async Task SaveAsync(Guid entityId, Guid correlation, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            throw new ConcurrencyConflictException(nameof(PrescriptionDraft), entityId);
        }
        catch (DbUpdateException ex) when (!IsUnique(ex))
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex, correlation);
        }
    }

    private PrescriptionDraft NewDraft(Guid patientId, NormalizedWorkflow v, string hash, SafetyRead read, Guid? supersedes, Guid actor, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(), PatientId = patientId, Medication = v.Medication, DoseValue = v.DoseValue, DoseUnit = v.DoseUnit, Frequency = v.Frequency, Instructions = v.Instructions, QuantityValue = v.QuantityValue,
        QuantityUnit = v.QuantityUnit, Refills = v.Refills, ReviewNote = v.ReviewNote, SupersedesPrescriptionId = supersedes, SafetyFingerprint = read.Fingerprint, SafetySnapshot = SafetySnapshot.Join(read.Lines),
        Status = DraftStatuses.Draft, CreateKey = v.IdempotencyKey, CreateHash = hash, CreatedByUserId = actor, CreatedAtUtc = now, UpdatedByUserId = actor, UpdatedAtUtc = now,
    };

    private static bool SameAs(PrescriptionDraft d, NormalizedWorkflow v) =>
        (d.Medication, d.DoseValue, d.DoseUnit, d.Frequency, d.Instructions, d.QuantityValue, d.QuantityUnit, d.Refills, d.ReviewNote) ==
        (v.Medication, v.DoseValue, v.DoseUnit, v.Frequency, v.Instructions, v.QuantityValue, v.QuantityUnit, v.Refills, v.ReviewNote);

    private void Touch(PrescriptionDraft d, Guid actor) => (d.UpdatedAtUtc, d.UpdatedByUserId) = (clock.UtcNow, actor);

    private async Task RequirePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PrescriptionException("patient_not_found", "That patient was not found.", 404);
    }

    private async Task<PrescriptionDraft> FindAsync(Guid draftId, CancellationToken ct) =>
        await db.PrescriptionDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == draftId, ct) ?? throw new PrescriptionException("draft_not_found", "That draft was not found.", 404);

    private Task<PrescriptionDraft?> FindByKeyAsync(Guid patientId, string key, CancellationToken ct) =>
        db.PrescriptionDrafts.AsNoTracking().SingleOrDefaultAsync(d => d.PatientId == patientId && d.CreateKey == key, ct);

    /// <summary>Loads a draft for a change, with the version the caller read: a change made by someone else since is refused when saving.</summary>
    internal async Task<PrescriptionDraft> LoadForChangeAsync(Guid draftId, string? rowVersion, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new PrescriptionException("row_version_required", "The version of the draft you are changing is required, so a change made by someone else is never overwritten. Reload and try again.", 400);
        byte[] expected;
        try { expected = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new PrescriptionException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400); }
        var d = await db.PrescriptionDrafts.SingleOrDefaultAsync(x => x.Id == draftId, ct) ?? throw new PrescriptionException("draft_not_found", "That draft was not found.", 404);
        db.Entry(d).Property(x => x.RowVersion).OriginalValue = expected;
        return d;
    }

    private async Task<SavedDraft> ReplayAsync(PrescriptionDraft existing, string hash, CancellationToken ct)
    {
        if (existing.CreateHash != hash)
            throw new PrescriptionException("idempotency_key_reused", "That save key was already used for a different draft. Nothing was saved. Use a new key to start another.", 409);
        return new SavedDraft((await ViewsAsync([existing], ct))[0], true);
    }

    internal async Task<IReadOnlyList<DraftView>> ViewsAsync(IReadOnlyList<PrescriptionDraft> rows, CancellationToken ct)
    {
        var names = await ClinicalNames.ResolveAsync(db, rows.SelectMany(r => new Guid?[] { r.CreatedByUserId, r.UpdatedByUserId }), ct);
        return rows.Select(r => new DraftView(r.Id, r.PatientId, r.Medication, r.DoseValue, r.DoseUnit, r.Frequency, r.Instructions, r.QuantityValue, r.QuantityUnit, r.Refills, r.ReviewNote, r.Status,
            r.SupersedesPrescriptionId, r.FinalizedPrescriptionId, r.DiscardedReason, r.SafetyFingerprint, ClinicalNames.Name(names, r.CreatedByUserId), r.CreatedAtUtc,
            ClinicalNames.Name(names, r.UpdatedByUserId), r.UpdatedAtUtc, Convert.ToBase64String(r.RowVersion))).ToList();
    }
}
