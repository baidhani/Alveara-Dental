using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Forms;

/// <summary>Where one required form stands for one patient at check-in. Only <see cref="Complete"/> counts as done.</summary>
public static class ReadinessStatuses
{
    /// <summary>Signed on the template's CURRENT version (and the signed copy exists).</summary>
    public const string Complete = "Complete";
    /// <summary>A draft has been started but not signed.</summary>
    public const string InProgress = "InProgress";
    /// <summary>Signed, but only on an older version of the wording; the current version has not been signed. Never reported as complete.</summary>
    public const string SignedEarlierVersion = "SignedEarlierVersion";
    /// <summary>Nothing started, or the only forms were voided.</summary>
    public const string Missing = "Missing";
}

public record ReadinessItem(Guid TemplateId, string TemplateKey, string Title, string Category, string Status, int CurrentVersionNumber, int? SignedVersionNumber);

/// <summary>
/// Check-in readiness for one patient: which forms the practice requires at check-in and which of them are done. <see cref="Ready"/> is true only when
/// every required form is <see cref="ReadinessStatuses.Complete"/> - and also when the practice requires none (then <see cref="RequiredCount"/> is 0, so the
/// screen can say nothing is required rather than implying something was completed).
/// </summary>
public record CheckInReadiness(bool Ready, int RequiredCount, int CompleteCount, IReadOnlyList<ReadinessItem> Items);

/// <summary>
/// ALV-011-C01: reads, never writes. Readiness is worked out from what is actually stored - a form counts only if it is Signed, has its immutable signed
/// copy, and was signed on the template's current version - so viewing a form, starting a draft or a stale signature can never make a patient look ready.
/// Only active templates the practice has marked "required at check-in" count; an inactive template cannot be started, so it cannot block anyone. It is
/// informational: check-in itself is never refused because a form is outstanding.
/// </summary>
public class CheckInReadinessService(AlveraDbContext db)
{
    /// <summary>Readiness for one patient; an unknown patient is a 404 (never reported as ready).</summary>
    public async Task<CheckInReadiness> ForPatientAsync(Guid patientId, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct))
            throw new FormException("patient_not_found", "That patient was not found.", 404);
        return (await ForPatientsAsync([patientId], ct))[patientId];
    }

    /// <summary>Readiness for several patients at once (the live board) with a fixed number of queries.</summary>
    public async Task<IReadOnlyDictionary<Guid, CheckInReadiness>> ForPatientsAsync(IReadOnlyCollection<Guid> patientIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, CheckInReadiness>();
        var required = await db.FormTemplates.AsNoTracking().Where(t => t.IsActive && t.RequiredAtCheckIn && t.CurrentVersionId != null)
            .OrderBy(t => t.Category).ThenBy(t => t.Key).ToListAsync(ct);
        if (required.Count == 0 || patientIds.Count == 0)
        {
            foreach (var p in patientIds) result[p] = new CheckInReadiness(true, 0, 0, []);
            return result;
        }

        var templateIds = required.Select(t => t.Id).ToList();
        var versions = await db.FormTemplateVersions.AsNoTracking().Where(v => templateIds.Contains(v.TemplateId))
            .Select(v => new { v.Id, v.TemplateId, v.VersionNumber, v.Title }).ToListAsync(ct);
        var versionById = versions.ToDictionary(v => v.Id);
        // Voided forms are left out only to read fewer rows: the rules below need Status Signed (with its copy) or Draft, which a voided form can never be.
        var forms = await db.PatientForms.AsNoTracking()
            .Where(f => patientIds.Contains(f.PatientId) && templateIds.Contains(f.TemplateId) && f.Status != FormStatuses.Void)
            .Select(f => new { f.Id, f.PatientId, f.TemplateId, f.TemplateVersionId, f.Status }).ToListAsync(ct);
        var withSignedCopy = (await db.SignedFormSnapshots.AsNoTracking().Where(s => patientIds.Contains(s.PatientId)).Select(s => s.PatientFormId).ToListAsync(ct)).ToHashSet();

        foreach (var patient in patientIds)
        {
            var items = new List<ReadinessItem>();
            foreach (var t in required)
            {
                var current = versionById[t.CurrentVersionId!.Value];
                var mine = forms.Where(f => f.PatientId == patient && f.TemplateId == t.Id).ToList();
                var signed = mine.Where(f => f.Status == FormStatuses.Signed && withSignedCopy.Contains(f.Id)).ToList();
                var signedVersion = signed.Count == 0 ? (int?)null : signed.Max(f => versionById[f.TemplateVersionId].VersionNumber);
                var status =
                    signed.Any(f => f.TemplateVersionId == t.CurrentVersionId) ? ReadinessStatuses.Complete
                    : mine.Any(f => f.Status == FormStatuses.Draft) ? ReadinessStatuses.InProgress
                    : signed.Count > 0 ? ReadinessStatuses.SignedEarlierVersion
                    : ReadinessStatuses.Missing;
                items.Add(new ReadinessItem(t.Id, t.Key, current.Title, t.Category, status, current.VersionNumber, signedVersion));
            }
            var done = items.Count(i => i.Status == ReadinessStatuses.Complete);
            result[patient] = new CheckInReadiness(done == items.Count, items.Count, done, items);
        }
        return result;
    }
}
