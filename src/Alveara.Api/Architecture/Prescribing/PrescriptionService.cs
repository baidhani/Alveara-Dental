using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Safety;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// STORY-016: prescribing with an allergy check and a structural dose check.
///
/// How the promises are kept:
/// - <b>Dosage accuracy means structure, and the screen says so.</b> There is no drug-knowledge source, so the dose is judged by <see cref="PrescriptionRules"/> (a positive number, a known unit and frequency, not above a
///   typing-error ceiling) and nothing here claims a dose is clinically right.
/// - <b>The allergy check reads the shared patient-safety context</b> (<see cref="ISafetyContextProvider"/>, ALV-N011): the patient's active allergies and whether the allergy list is established. A name match, or a list
///   nobody has reviewed, needs the prescriber's own review note before anything is saved (<c>allergy_review_required</c>, 409); nothing is overridden silently. If the safety context cannot be read the save is refused
///   (<c>allergy_check_unavailable</c>, 503): a prescription is never saved on an allergy check that did not happen.
/// - <b>An incorrect entry prompts for correction.</b> The whole entry is judged first; every problem is named (<c>validation_failed</c>, 400) and nothing is saved.
/// - <b>Every entry is logged, in the same save.</b> The prescription and its PHI-free audit entry (never the medication, the dose or an allergy) carry the signed-in user and the time; if the log cannot be written nothing is
///   saved and the caller is told so (<c>save_failed</c>, 503).
/// - <b>Repeats are quiet.</b> The same key and the same entry return the prescription already saved; the same key with a different entry is refused (<c>idempotency_key_reused</c>, 409); two simultaneous saves under one key
///   end with one prescription. A prescription is never changed or deleted (the database refuses both).
/// </summary>
public class PrescriptionService(AlveraDbContext db, IPracticeClock clock, ISafetyContextProvider safety, ILogger<PrescriptionService>? logger = null)
{
    // ---------- reads ----------

    public static PrescriptionOptions Options() =>
        new(PrescriptionRules.DoseUnits, PrescriptionRules.TypingCeilings, PrescriptionRules.Frequencies, PrescriptionRules.DoseDecimals, PrescriptionRules.MedicationMax, PrescriptionRules.ReviewNoteMax,
            PrescriptionRules.DoseCheckLimitation, AllergyCheck.Limitation);

    public async Task<IReadOnlyList<PrescriptionView>> ListAsync(Guid patientId, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var rows = await db.Prescriptions.AsNoTracking().Where(p => p.PatientId == patientId).OrderByDescending(p => p.CreatedAtUtc).ThenByDescending(p => p.Id).ToListAsync(ct);
        return await ViewsAsync(rows, ct);
    }

    /// <summary>Judges an entry without saving it: the problems, the allergy assessment, and whether the prescriber's review is required. Writes nothing.</summary>
    public async Task<PrescriptionPreview> PreviewAsync(Guid patientId, PrescriptionInput? input, Guid actor, CancellationToken ct)
    {
        await RequirePatientAsync(patientId, ct);
        var check = PrescriptionRules.Check(input is null ? null : input with { IdempotencyKey = input.IdempotencyKey ?? "preview" });       // the key is not a field of the entry being previewed
        var medication = check.Value?.Medication ?? MedicationIfValid(input);
        AllergyAssessment? assessment = medication is null ? null : await AssessAsync(patientId, medication, actor, ct);
        var review = assessment is null ? null : PrescriptionRules.ReviewProblem(assessment, check.Value?.ReviewNote ?? NormalizedNote(input));
        return new PrescriptionPreview(check.Problems, assessment, assessment is not null && assessment.Outcome != AllergyOutcomes.NoRecordedMatch, review, PrescriptionRules.DoseCheckLimitation, AllergyCheck.Limitation);
    }

    // ---------- the write ----------

    public async Task<SavedPrescription> RecordAsync(Guid patientId, PrescriptionInput? input, Guid actor, CancellationToken ct)
    {
        var check = PrescriptionRules.Check(input);
        if (check.Problems.Count > 0) throw new PrescriptionException("validation_failed", "The prescription has entries that need correcting. Nothing was saved.", 400, check.Problems);
        var v = check.Value!;
        await RequirePatientAsync(patientId, ct);

        var hash = PrescriptionRules.RequestHash(v);
        var existing = await FindByKeyAsync(patientId, v.IdempotencyKey, ct);
        if (existing is not null) return await ReplayAsync(existing, hash, ct);

        var assessment = await AssessAsync(patientId, v.Medication, actor, ct);
        if (PrescriptionRules.ReviewProblem(assessment, v.ReviewNote) is { } needsReview)
            throw new PrescriptionException("allergy_review_required",
                assessment.Outcome == AllergyOutcomes.RecordedAllergyMatch
                    ? "This medication matches an allergy recorded for this patient. Nothing was saved. Say why you are prescribing it, or change the medication."
                    : "This patient's allergies are not established, so the allergy check cannot say anything. Nothing was saved. Review the allergies, or confirm in your own words that you have asked the patient.",
                409, [needsReview], assessment);

        var now = clock.UtcNow;
        var correlation = Guid.NewGuid();
        var row = new Prescription
        {
            Id = Guid.NewGuid(), PatientId = patientId, Medication = v.Medication, DoseValue = v.DoseValue, DoseUnit = v.DoseUnit, Frequency = v.Frequency, AllergyOutcome = assessment.Outcome,
            AllergyListState = assessment.ListState, AllergyMatches = assessment.Matches.Count == 0 ? null : string.Join("; ", assessment.Matches.Select(m => m.Name)), ReviewNote = v.ReviewNote,
            IdempotencyKey = v.IdempotencyKey, RequestHash = hash, CorrelationId = correlation, PrescriberUserId = actor, CreatedAtUtc = now,
        };
        db.Prescriptions.Add(row);
        AuditService.Record(db, "PrescriptionRecorded", nameof(Prescription), row.Id, actor,
            assessment.Outcome == AllergyOutcomes.NoRecordedMatch ? "Prescription recorded." : "Prescription recorded after the prescriber's allergy review.", correlationId: correlation);
        var started = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();                                         // a simultaneous save with the same key won: answer with its prescription (or refuse if it was a different entry)
            var winner = await FindByKeyAsync(patientId, v.IdempotencyKey, ct) ?? throw SaveFailed(ex, correlation, started);
            return await ReplayAsync(winner, hash, ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw SaveFailed(ex, correlation, started);
        }
        logger?.LogInformation("prescription_recorded correlation_id={CorrelationId} allergy_outcome={AllergyOutcome} duration_ms={DurationMs}", correlation, assessment.Outcome, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        return new SavedPrescription((await ViewsAsync([row], ct))[0], false);
    }

    // ---------- the allergy check ----------

    /// <summary>
    /// Reads the shared safety context and compares the medication's name with the active allergies recorded in the clinical record. The allergy list's state comes from the context's gaps: a section that is not
    /// reviewed, unknown or changed since it was confirmed is a gap; one that is reviewed (or says none known) is not. Any failure to read it refuses the save: an allergy check that did not happen is never "clear".
    /// </summary>
    private async Task<AllergyAssessment> AssessAsync(Guid patientId, string medication, Guid actor, CancellationToken ct)
    {
        try
        {
            var context = await safety.GetAsync(patientId, actor, ct);
            var allergies = context.Entries
                .Where(e => e.Origin == SafetyOrigins.ClinicalRecord && e.Category == SafetyCategories.Allergy && e.Status == SafetyAlertStatuses.Active)
                .Select(e => new RecordedAllergy(e.Title, e.Severity, e.Detail)).ToList();
            var gap = context.Gaps.FirstOrDefault(g => g.Section == EncounterEntryKinds.Allergy);
            return AllergyCheck.Assess(medication, allergies, AllergyListStates.From(gap?.Status ?? ClinicalSectionStatuses.Reviewed));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning("prescription_allergy_check_unavailable error_class={ErrorClass}", ex.GetType().Name);
            throw new PrescriptionException("allergy_check_unavailable",
                "The allergy check could not be completed, so nothing was saved. Try again; if it keeps failing, contact support.", 503);
        }
    }

    // ---------- helpers ----------

    private static string? MedicationIfValid(PrescriptionInput? input)
    {
        if (input?.Medication is not { } m || string.IsNullOrWhiteSpace(m) || m.Any(char.IsControl)) return null;
        var normalized = PrescriptionRules.NormalizeLine(m);
        return normalized.Length is >= PrescriptionRules.MedicationMin and <= PrescriptionRules.MedicationMax ? normalized : null;
    }

    private static string? NormalizedNote(PrescriptionInput? input) =>
        string.IsNullOrWhiteSpace(input?.ReviewNote) ? null : input!.ReviewNote.Replace("\r\n", "\n").Replace('\r', '\n').Trim();

    private async Task RequirePatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new PrescriptionException("patient_not_found", "That patient was not found.", 404);
    }

    private Task<Prescription?> FindByKeyAsync(Guid patientId, string key, CancellationToken ct) =>
        db.Prescriptions.AsNoTracking().SingleOrDefaultAsync(p => p.PatientId == patientId && p.IdempotencyKey == key, ct);

    private async Task<SavedPrescription> ReplayAsync(Prescription existing, string hash, CancellationToken ct)
    {
        if (existing.RequestHash != hash)
            throw new PrescriptionException("idempotency_key_reused", "That save key was already used for a different prescription. Nothing was saved. Use a new key to prescribe something else.", 409);
        return new SavedPrescription((await ViewsAsync([existing], ct))[0], true);
    }

    private async Task<IReadOnlyList<PrescriptionView>> ViewsAsync(IReadOnlyList<Prescription> rows, CancellationToken ct)
    {
        var names = await ClinicalNames.ResolveAsync(db, rows.Select(r => (Guid?)r.PrescriberUserId), ct);
        return rows.Select(r => new PrescriptionView(r.Id, r.PatientId, r.Medication, r.DoseValue, r.DoseUnit, r.Frequency, r.AllergyOutcome, r.AllergyListState, r.AllergyMatches, r.ReviewNote,
            ClinicalNames.Name(names, r.PrescriberUserId), r.CreatedAtUtc)).ToList();
    }

    private PrescriptionException SaveFailed(Exception ex, Guid correlation, DateTimeOffset started)
    {
        logger?.LogWarning("prescription_not_recorded error_class={ErrorClass} correlation_id={CorrelationId} duration_ms={DurationMs}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, correlation, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        return new PrescriptionException("save_failed", "The prescription could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503);
    }
}
