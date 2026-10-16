using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Billing;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>
/// ALV-016-C01: what happens to a finalized prescription: it is cancelled (entered in error, or never taken), discontinued (stopped), or replaced by an amendment (see <see cref="PrescriptionFinalizeService"/>), and it
/// can be printed. A prescription is never edited or deleted: each of these is an appended event with who, when and (for the first two) the prescriber's reason, and the status is worked out from the events.
///
/// Refusals, each leaving nothing recorded: <c>validation_failed</c> (400, a reason is required), <c>prescription_not_found</c> (404), <c>prescription_not_active</c> (409, it has already ended in another way),
/// <c>print_incomplete</c> (409, it was saved before instructions, quantity and refills were recorded, so there is nothing complete to print: amend it) and <c>save_failed</c> / <c>print_failed</c> (503, the log could
/// not be written). Repeats are quiet: cancelling a cancelled prescription, or discontinuing a discontinued one, changes nothing. Two simultaneous endings make one (the database allows one ending event).
/// </summary>
public class PrescriptionLifecycleService(AlveraDbContext db, IPracticeClock clock, PrescriptionRecordReader records, ILogger<PrescriptionLifecycleService>? logger = null)
{
    public Task<SavedRecord> CancelAsync(Guid prescriptionId, string? reason, Guid actor, CancellationToken ct) =>
        EndAsync(prescriptionId, PrescriptionEventTypes.Cancelled, "PrescriptionCancelled", "Prescription cancelled; the reason is kept with the prescription.", reason, actor, ct);

    public Task<SavedRecord> DiscontinueAsync(Guid prescriptionId, string? reason, Guid actor, CancellationToken ct) =>
        EndAsync(prescriptionId, PrescriptionEventTypes.Discontinued, "PrescriptionDiscontinued", "Prescription discontinued; the reason is kept with the prescription.", reason, actor, ct);

    private async Task<SavedRecord> EndAsync(Guid prescriptionId, string eventType, string auditType, string auditText, string? reason, Guid actor, CancellationToken ct)
    {
        var current = await records.GetAsync(prescriptionId, ct);
        var target = eventType;                                              // the status names are the event names
        if (current.Status == target) return new SavedRecord(current, true);
        if (!PrescriptionStatuses.CanEnd(current.Status)) throw new PrescriptionException("prescription_not_active", $"This prescription is {current.Status.ToLowerInvariant()}, so it cannot be {eventType.ToLowerInvariant()}.", 409);
        var (text, problem) = PrescriptionWorkflowRules.CheckReason(reason);
        if (problem is not null) throw new PrescriptionException("validation_failed", "A reason is needed. Nothing was recorded.", 400, [problem]);

        var correlation = Guid.NewGuid();
        db.PrescriptionEvents.Add(new PrescriptionEvent { Id = Guid.NewGuid(), PrescriptionId = prescriptionId, EventType = eventType, Reason = text, ActorUserId = actor, OccurredAtUtc = clock.UtcNow, CorrelationId = correlation });
        AuditService.Record(db, auditType, nameof(Prescription), prescriptionId, actor, auditText, correlationId: correlation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            if (PrescriptionDraftService.IsUnique(ex))                       // a simultaneous ending won: it is the same ending, or the prescription has ended otherwise
            {
                var now = await records.GetAsync(prescriptionId, ct);
                if (now.Status == target) return new SavedRecord(now, true);
                throw new PrescriptionException("prescription_not_active", $"This prescription is {now.Status.ToLowerInvariant()}, so it cannot be {eventType.ToLowerInvariant()}.", 409);
            }
            logger?.LogWarning("prescription_event_not_recorded error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, correlation);
            throw new PrescriptionException("save_failed", "The change could not be saved, so nothing was recorded. Try again; if it keeps failing, contact support.", 503);
        }
        return new SavedRecord(await records.GetAsync(prescriptionId, ct), false);
    }

    /// <summary>
    /// The prescription laid out for printing, built from the immutable record, and the fact that it was printed (PHI-free, with the user and the time) saved with it: if the log cannot be written nothing is
    /// printed (<c>print_failed</c>, 503). A prescription saved before instructions, quantity and refills were recorded cannot be printed (<c>print_incomplete</c>, 409): amend it first.
    /// </summary>
    public async Task<PrescriptionPrintContent> PrintAsync(Guid prescriptionId, Guid actor, CancellationToken ct)
    {
        var r = await records.GetAsync(prescriptionId, ct);
        if (!r.HasDetails || r.Instructions is null || r.QuantityValue is null || r.QuantityUnit is null || r.Refills is null)
            throw new PrescriptionException("print_incomplete",
                "This prescription was saved before instructions, quantity and refills were recorded, so there is nothing complete to print. Amend it to add them.", 409);
        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == r.PatientId, ct);
        var practice = await db.PracticeSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var name = string.Join(' ', new[] { patient.FirstName, patient.MiddleName, patient.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var inv = CultureInfo.InvariantCulture;
        var dose = $"{r.DoseValue.ToString("0.###", inv)} {r.DoseUnit}";
        var quantity = $"{r.QuantityValue.Value.ToString("0.##", inv)} {r.QuantityUnit}";
        var allergy = r.AllergyOutcome switch
        {
            AllergyOutcomes.RecordedAllergyMatch => $"At the time of prescribing the medication matched a recorded allergy ({r.AllergyMatches}); the prescriber reviewed it.",
            AllergyOutcomes.NotEstablished => "At the time of prescribing the patient's allergies were not established; the prescriber confirmed this in their own words.",
            _ => "At the time of prescribing no recorded allergy matched this medication's name. That is a name comparison, not a clearance.",
        };
        var prescribedAt = r.PrescribedAtUtc.ToString("O", inv);
        var canonical = string.Join('|', "p1", r.Id.ToString("N"), r.Status, name, patient.DateOfBirth.ToString("O", inv), r.PrescribedByName ?? "", r.Medication, dose, r.Frequency, r.Instructions, quantity, r.Refills.Value.ToString(inv), prescribedAt,
            allergy, r.SupersedesPrescriptionId?.ToString("N") ?? "", r.SupersededByPrescriptionId?.ToString("N") ?? "");
        var content = new PrescriptionPrintContent(r.Id, r.Status, practice is null ? null : new DocumentPractice(practice.Name, practice.Phone, practice.AddressLine), name, patient.DateOfBirth.ToString("O", inv),
            r.PrescribedByName ?? "", r.Medication, dose, r.Frequency, r.Instructions, quantity, r.Refills.Value, prescribedAt, allergy, DecisionSupport.Message, r.SupersedesPrescriptionId, r.SupersededByPrescriptionId,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant());

        var correlation = Guid.NewGuid();
        AuditService.Record(db, "PrescriptionPrinted", nameof(Prescription), r.Id, actor, "A prescription was prepared for printing.", correlationId: correlation);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning("prescription_print_not_logged error_class={ErrorClass} correlation_id={CorrelationId}", ex.InnerException?.GetType().Name ?? ex.GetType().Name, correlation);
            throw new PrescriptionException("print_failed", "The prescription could not be prepared for printing, because the print could not be logged. Nothing was printed. Try again.", 503);
        }
        return content;
    }
}
