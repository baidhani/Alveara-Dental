using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Forms;

/// <summary>
/// The shared write pattern for ALV-N010: the caller must echo the row version it read (a stale write is the shared 409
/// concurrency conflict, never a silent overwrite), and history rows and audit entries are staged in the SAME SaveChanges
/// as the change. Audit text never contains a patient's answers or the signer's name - those live in the form's own
/// tables, behind the form-viewing permission.
/// </summary>
internal static class FormWrite
{
    public static void ApplyExpectedVersion<T>(AlveraDbContext db, T entity, string? rowVersion) where T : class
    {
        if (string.IsNullOrWhiteSpace(rowVersion))
            throw new FormException("row_version_required", "The version you are working on is required so a concurrent change is never overwritten. Reload and try again.", 400);
        try
        {
            db.Entry(entity).Property("RowVersion").OriginalValue = Convert.FromBase64String(rowVersion);
        }
        catch (FormatException)
        {
            throw new FormException("row_version_invalid", "The supplied version is not valid. Reload and try again.", 400);
        }
    }

    public static void Event(AlveraDbContext db, PatientForm form, string type, Guid actor, DateTimeOffset now, int versionNumber, string? detail = null) =>
        db.PatientFormEvents.Add(new PatientFormEvent
        {
            Id = Guid.NewGuid(), PatientFormId = form.Id, PatientId = form.PatientId, EventType = type, ActorUserId = actor,
            OccurredAtUtc = now, TemplateVersionNumber = versionNumber, Detail = detail is { Length: > 400 } ? detail[..400] : detail,
        });

    public static void Audit(AlveraDbContext db, string eventType, Guid formId, Guid actor, string details, string? reason = null) =>
        AuditService.Record(db, eventType, nameof(PatientForm), formId, actor, details, reason);
}
