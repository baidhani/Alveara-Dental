using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// ALV-016-C01: finalizing a draft into a prescription. The STORY-016 checks run unchanged on the draft's entry (<see cref="PrescriptionRules"/> through <see cref="PrescriptionWorkflowRules.CheckFinal"/>, and the same
/// allergy check and review rule), and the finalization adds its own: the instructions, quantity and refills must be there, and the allergy and medication lists must be the ones the prescriber acknowledged.
///
/// Refusals, each leaving nothing saved: <c>validation_failed</c> (400, every problem named), <c>safety_context_changed</c> (409, what changed in the lists), <c>allergy_review_required</c> (409, the prescriber's
/// own words are needed), <c>allergy_check_unavailable</c> (503: the allergy check is never skipped), <c>prescription_not_active</c> (409: an amendment's original was cancelled, discontinued or already replaced),
/// <c>draft_closed</c> (409: discarded), <c>concurrency_conflict</c> (409: someone changed the draft first) and <c>save_failed</c> (503: the log or the save failed).
///
/// <b>One save.</b> The prescription, its details, its Finalized event, the draft's change of state, for an amendment the original's Superseded event, and the PHI-free audit entries are written together, so a failure
/// anywhere writes nothing. <b>Repeats are quiet:</b> finalizing a draft that was finalized returns the prescription it became (<c>replayed</c>); two simultaneous finalizations make one prescription (the draft's
/// version and the prescription's key, which is derived from the draft, both refuse the second).
/// </summary>
public class PrescriptionFinalizeService(AlveraDbContext db, IPracticeClock clock, PrescribingSafetyReader reader, PrescriptionDraftService drafts, PrescriptionRecordReader records, ILogger<PrescriptionFinalizeService>? logger = null)
{
    public async Task<SavedRecord> FinalizeAsync(Guid draftId, string? rowVersion, Guid actor, CancellationToken ct)
    {
        var d = await drafts.LoadForChangeAsync(draftId, rowVersion, ct);
        if (d.Status == DraftStatuses.Finalized) return new SavedRecord(await records.GetAsync(d.FinalizedPrescriptionId!.Value, ct), true);
        if (d.Status != DraftStatuses.Draft) throw PrescriptionDraftService.Closed(d);

        var check = PrescriptionWorkflowRules.CheckFinal(PrescriptionDraftService.InputOf(d));
        if (check.Problems.Count > 0) throw PrescriptionDraftService.Invalid(check.Problems);
        var v = check.Value!;

        var read = await reader.ReadAsync(d.PatientId, actor, ct);                       // fails closed: no allergy check, no prescription
        if (read.Fingerprint != d.SafetyFingerprint) throw PrescriptionDraftService.SafetyChanged(d, read);
        var assessment = read.Assess(v.Medication);
        if (PrescriptionRules.ReviewProblem(assessment, v.ReviewNote) is { } needsReview)
            throw new PrescriptionException("allergy_review_required",
                assessment.Outcome == AllergyOutcomes.RecordedAllergyMatch
                    ? "This medication matches an allergy recorded for this patient. Nothing was saved. Say why you are prescribing it, or change the medication."
                    : "This patient's allergies are not established, so the allergy check cannot say anything. Nothing was saved. Review the allergies, or confirm in your own words that you have asked the patient.",
                409, [needsReview], assessment);

        if (d.SupersedesPrescriptionId is Guid original)
        {
            var status = PrescriptionStatuses.From(await db.PrescriptionEvents.AsNoTracking().Where(e => e.PrescriptionId == original).Select(e => e.EventType).ToListAsync(ct));
            if (!PrescriptionStatuses.CanEnd(status)) throw new PrescriptionException("prescription_not_active", $"The prescription this amends is {status.ToLowerInvariant()}, so it cannot be replaced.", 409);
        }

        var now = clock.UtcNow;
        var correlation = Guid.NewGuid();
        var core = PrescriptionWorkflowRules.CoreOf(v);
        var row = new Prescription
        {
            Id = Guid.NewGuid(), PatientId = d.PatientId, Medication = v.Medication, DoseValue = v.DoseValue, DoseUnit = v.DoseUnit, Frequency = v.Frequency, AllergyOutcome = assessment.Outcome,
            AllergyListState = assessment.ListState, AllergyMatches = assessment.Matches.Count == 0 ? null : string.Join("; ", assessment.Matches.Select(m => m.Name)), ReviewNote = v.ReviewNote,
            IdempotencyKey = core.IdempotencyKey, RequestHash = PrescriptionRules.RequestHash(core), CorrelationId = correlation, PrescriberUserId = actor, CreatedAtUtc = now,
        };
        db.Prescriptions.Add(row);
        db.PrescriptionDetails.Add(new PrescriptionDetail
        {
            PrescriptionId = row.Id, DraftId = d.Id, SupersedesPrescriptionId = d.SupersedesPrescriptionId, Instructions = v.Instructions!, QuantityValue = v.QuantityValue!.Value, QuantityUnit = v.QuantityUnit!,
            Refills = v.Refills!.Value, SafetyFingerprint = read.Fingerprint, SafetySnapshot = SafetySnapshot.Join(read.Lines), FinalizedByUserId = actor, FinalizedAtUtc = now,
        });
        Stage(row.Id, PrescriptionEventTypes.Finalized, null, null, actor, now, correlation);
        if (d.SupersedesPrescriptionId is Guid replaced)
        {
            Stage(replaced, PrescriptionEventTypes.Superseded, null, row.Id, actor, now, correlation);
            AuditService.Record(db, "PrescriptionSuperseded", nameof(Prescription), replaced, actor, "A prescription was replaced by an amendment.", correlationId: correlation);
        }
        (d.Status, d.FinalizedPrescriptionId, d.UpdatedAtUtc, d.UpdatedByUserId) = (DraftStatuses.Finalized, row.Id, now, actor);
        AuditService.Record(db, "PrescriptionRecorded", nameof(Prescription), row.Id, actor,
            d.SupersedesPrescriptionId is not null ? "Prescription finalized as an amendment of an earlier prescription."
            : assessment.Outcome == AllergyOutcomes.NoRecordedMatch ? "Prescription recorded." : "Prescription recorded after the prescriber's allergy review.", correlationId: correlation);

        var started = DateTimeOffset.UtcNow;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || (ex is DbUpdateException u && PrescriptionDraftService.IsUnique(u)))
        {
            db.ChangeTracker.Clear();                                         // a simultaneous finalization (or another change) won: answer with what it made, or report the conflict
            var now2 = await db.PrescriptionDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId, ct);
            if (now2.Status == DraftStatuses.Finalized) return new SavedRecord(await records.GetAsync(now2.FinalizedPrescriptionId!.Value, ct), true);
            throw new ConcurrencyConflictException(nameof(PrescriptionDraft), draftId);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            throw drafts.SaveFailed(ex, correlation);
        }
        logger?.LogInformation("prescription_finalized correlation_id={CorrelationId} allergy_outcome={AllergyOutcome} amendment={Amendment} duration_ms={DurationMs}",
            correlation, assessment.Outcome, d.SupersedesPrescriptionId is not null, (DateTimeOffset.UtcNow - started).TotalMilliseconds);
        return new SavedRecord(await records.GetAsync(row.Id, ct), false);
    }

    private void Stage(Guid prescriptionId, string type, string? reason, Guid? supersededBy, Guid actor, DateTimeOffset now, Guid correlation) =>
        db.PrescriptionEvents.Add(new PrescriptionEvent { Id = Guid.NewGuid(), PrescriptionId = prescriptionId, EventType = type, Reason = reason, SupersededByPrescriptionId = supersededBy, ActorUserId = actor, OccurredAtUtc = now, CorrelationId = correlation });
}
