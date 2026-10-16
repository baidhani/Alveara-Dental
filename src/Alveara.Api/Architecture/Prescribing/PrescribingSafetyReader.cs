using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Safety;

namespace Alveara.Api.Architecture.Prescribing;

/// <summary>The patient's allergy and medication lists as the shared safety context shows them right now, with the snapshot and fingerprint the workflow compares.</summary>
public sealed record SafetyRead(
    IReadOnlyList<RecordedAllergy> Allergies, string AllergyStatus, string MedicationStatus, IReadOnlyList<string> Lines, string Fingerprint)
{
    /// <summary>The allergy check for one medication against this read: a name comparison only, never a clearance.</summary>
    public AllergyAssessment Assess(string medication) => AllergyCheck.Assess(medication, Allergies, AllergyListStates.From(AllergyStatus));
}

/// <summary>
/// ALV-016-C01: reads the shared patient-safety context (ALV-N011) for prescribing. The active allergies and current medications recorded in the clinical record, and how far each list can be relied on (a section
/// nobody reviewed, or that changed since it was confirmed, is a gap), are reduced to a <see cref="SafetyRead"/>. Nothing is invented: a missing fact stays missing. Any failure to read the context refuses the caller
/// (<c>allergy_check_unavailable</c>, 503): an allergy check that did not happen is never "clear".
/// </summary>
public class PrescribingSafetyReader(ISafetyContextProvider safety, ILogger<PrescribingSafetyReader>? logger = null)
{
    public async Task<SafetyRead> ReadAsync(Guid patientId, Guid actor, CancellationToken ct)
    {
        try
        {
            var context = await safety.GetAsync(patientId, actor, ct);
            var recorded = context.Entries.Where(e => e.Origin == SafetyOrigins.ClinicalRecord && e.Status == SafetyAlertStatuses.Active).ToList();
            var allergies = recorded.Where(e => e.Category == SafetyCategories.Allergy).Select(e => new RecordedAllergy(e.Title, e.Severity, e.Detail)).ToList();
            var items = recorded.Where(e => e.Category is SafetyCategories.Allergy or SafetyCategories.Medication)
                .Select(e => new SnapshotItem(e.Category, e.Title, e.Severity, e.Detail, e.Status)).ToList();
            var allergyStatus = context.Gaps.FirstOrDefault(g => g.Section == EncounterEntryKinds.Allergy)?.Status ?? ClinicalSectionStatuses.Reviewed;
            var medicationStatus = context.Gaps.FirstOrDefault(g => g.Section == EncounterEntryKinds.Medication)?.Status ?? ClinicalSectionStatuses.Reviewed;
            var lines = SafetySnapshot.Lines(items, allergyStatus, medicationStatus);
            return new SafetyRead(allergies, allergyStatus, medicationStatus, lines, SafetySnapshot.Fingerprint(lines));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning("prescribing_safety_unavailable error_class={ErrorClass}", ex.GetType().Name);
            throw new PrescriptionException("allergy_check_unavailable",
                "The allergy check could not be completed, so nothing was saved. Try again; if it keeps failing, contact support.", 503);
        }
    }
}
