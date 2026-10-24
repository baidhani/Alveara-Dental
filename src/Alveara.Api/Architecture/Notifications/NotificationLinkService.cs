using Microsoft.EntityFrameworkCore;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Notifications;

/// <summary>
/// ALV-N015: resolves the link of a notification or message that is about a patient. A list never carries a patient identifier; the client only learns that a link exists and asks the server to resolve it from the
/// notification or message in front of it. The server answers with a path only when the caller may see the notification or message AND may view patient records; otherwise it answers with the same refusal
/// whatever the reason and reveals nothing about the patient (not even that the patient exists).
/// </summary>
public class NotificationLinkService(AlveraDbContext db)
{
    public async Task<PatientLinkTarget> ForNotificationAsync(Guid userId, Guid notificationId, bool mayViewPatients, CancellationToken ct)
    {
        var n = await db.Notifications.AsNoTracking().FirstOrDefaultAsync(x => x.Id == notificationId && x.RecipientUserId == userId, ct)
            ?? throw new NotificationException("notification_not_found", "That notification was not found.", 404);
        if (n.RelatedPatientId is not { } patient || !mayViewPatients) throw Denied();
        return new PatientLinkTarget(n.Kind == NotificationKinds.DocumentDuplicateAdded && n.RelatedDocumentId is { } doc
            ? $"/patients/{patient}/documents?document={doc}"
            : n.Kind == NotificationKinds.FollowUpDue ? $"/patients/{patient}/follow-ups"   // STORY-009: a reminder opens the patient's follow-ups
            : $"/patients/{patient}");
    }

    public async Task<PatientLinkTarget> ForMessageAsync(Guid userId, Guid messageId, bool mayViewPatients, CancellationToken ct)
    {
        var m = await db.Messages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId, ct);
        if (m is null || (m.SenderUserId != userId && !await db.MessageRecipients.AsNoTracking().AnyAsync(r => r.MessageId == messageId && r.RecipientUserId == userId, ct)))
            throw new NotificationException("message_not_found", "That message was not found.", 404);
        if (m.RelatedPatientId is not { } patient || !mayViewPatients) throw Denied();
        return new PatientLinkTarget($"/patients/{patient}");
    }

    /// <summary>One refusal for every reason (no link, no permission): it says nothing about the patient.</summary>
    private static NotificationException Denied() => new("patient_access_denied", "You cannot open that patient's record from here.", 403);
}
