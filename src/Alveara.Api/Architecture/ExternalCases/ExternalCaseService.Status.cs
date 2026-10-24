using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;

namespace Alveara.Api.Architecture.ExternalCases;

public partial class ExternalCaseService
{
    /// <summary>
    /// Moves a case to another status, as <see cref="ExternalCaseRules"/> allows. Going out sets the sent date (today, or the date given, never in the future); coming back sets the received date (not before it went
    /// out); a lab case sent back from Received for a remake clears the received date and needs a note; Completed and Cancelled close the case with who and when, and a cancellation needs its reason. A stale version
    /// is <c>case_changed</c> (409); a move the rules do not allow is <c>status_invalid</c> (409), which is also what a second click on the same move gets once the first has been applied.
    /// </summary>
    public async Task<ExternalCaseResult> ChangeStatusAsync(Guid actor, string kind, Guid id, ChangeStatusRequest r, CancellationToken ct)
    {
        var to = ExternalCaseValidation.Kind(r.ToStatus, ExternalCaseRules.StatusesOf(kind), "status_invalid", "a status");
        var version = ExternalCaseValidation.Version(r.RowVersion);
        var c = await LoadAsync(kind, id, true, ct);
        var from = c.Status;
        if (!ExternalCaseRules.CanMove(kind, from, to))
            throw new ExternalCaseException("status_invalid", ExternalCaseRules.IsFinal(from) ? $"That case is already {from.ToLowerInvariant()} and cannot be changed." : $"A {kind.ToLowerInvariant()} case cannot move from {from} to {to}.", 409);
        var note = ExternalCaseRules.NoteRequired(kind, from, to)
            ? ExternalCaseValidation.RequiredNote(r.Note, to == ExternalCaseRules.Cancelled ? "why the case is being cancelled" : "why the work is being sent back")
            : ExternalCaseValidation.Note(r.Note, ExternalCaseRules.ReasonMaxLength);

        var today = Today;
        var now = clock.UtcNow;
        var date = r.Date ?? today;
        if ((to == ExternalCaseRules.Sent || to == ExternalCaseRules.Received) && !ExternalCaseRules.EventDateValid(date, today))
            throw new ExternalCaseException("date_invalid", "The date cannot be in the future or before the year 2000.", 400);

        db.Entry(c).Property(x => x.RowVersion).OriginalValue = version;
        switch (to)
        {
            case ExternalCaseRules.Sent:
                c.SentOn = date; c.ReceivedOn = null;                                               // going out (again, for a remake) starts the clock again
                break;
            case ExternalCaseRules.Received:
                if (c.SentOn is { } sent && date < sent) throw new ExternalCaseException("date_before_sent", "It cannot have come back before it was sent.", 400);
                c.ReceivedOn = date;
                break;
            case ExternalCaseRules.Completed:
                Close(c, actor, now, note);
                break;
            case ExternalCaseRules.Cancelled:
                Close(c, actor, now, note);
                break;
        }
        c.Status = to; c.UpdatedAtUtc = now;
        AddEvent(kind, c.Id, ExternalCaseEventKinds.StatusChanged, actor, now, from, to, note: note);
        AuditService.Record(db, kind == ExternalCaseKinds.Lab ? AuditEventTypes.LabCaseStatusChanged : AuditEventTypes.ReferralCaseStatusChanged, kind == ExternalCaseKinds.Lab ? "LabCase" : "ReferralCase", c.Id, actor,
            $"{kind} case moved from {from} to {to}.");
        await SaveAsync(c.Id, ct);
        return new ExternalCaseResult(await ViewAsync(c, ct), false);
    }

    private static void Close(IExternalCase c, Guid actor, DateTimeOffset now, string? note)
    {
        c.ClosedAtUtc = now; c.ClosedByUserId = actor; c.ClosureNote = note;
    }
}
