using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>
/// ALV-009-C01: how the system makes and closes its own tasks (recalls that came due, accepted treatment nobody has scheduled). It only STAGES rows on the context it is given and never saves: a background-job handler runs inside the
/// runner's transaction, so the task, its history entry, its audit entry, the job's receipt and its status commit together or not at all. A task's id is made from its source key, so a second attempt meets the same primary key
/// (and the database's unique source key) and cannot make the same task twice. A task the system made has no creator and no owner; the audit entry has no person; both say so.
/// </summary>
public static class SystemTaskWriter
{
    public static Guid IdFor(string sourceKey) => FollowUpValidation.DeterministicId("system", sourceKey);

    public static FollowUpTask Create(AlveraDbContext db, string kind, string sourceKey, Guid patientId, string title, DateTimeOffset dueAtUtc, string priority, Guid? planItemId, DateTimeOffset now, string detail)
    {
        var task = new FollowUpTask
        {
            Id = IdFor(sourceKey), PatientId = patientId, AssigneeUserId = null, Title = title, DueAtUtc = dueAtUtc, Status = FollowUpStatuses.Open, Priority = priority, Kind = kind,
            SourceKey = sourceKey, SourcePlanItemId = planItemId, CreatedByUserId = null, CreatedAtUtc = now,
        };
        db.FollowUpTasks.Add(task);
        db.FollowUpTaskEvents.Add(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.SystemCreated, ActorUserId = null, OccurredAtUtc = now, Detail = detail });
        AuditService.Record(db, AuditEventTypes.FollowUpCreated, "FollowUpTask", task.Id, null, $"{kind} follow-up created by the system for patient {patientId}, due {dueAtUtc:O}.");
        return task;
    }

    /// <summary>Closes an open task as the system. <paramref name="completed"/> says whether what it was about was done (Completed) or no longer applies (Cancelled). A task already closed is left alone.</summary>
    public static bool Close(AlveraDbContext db, FollowUpTask task, bool completed, string note, DateTimeOffset now)
    {
        if (task.Status != FollowUpStatuses.Open) return false;
        task.Status = completed ? FollowUpStatuses.Completed : FollowUpStatuses.Cancelled;
        task.ClosedAtUtc = now; task.ClosedByUserId = null; task.ClosedBySystem = true; task.ClosureNote = note;
        db.FollowUpTaskEvents.Add(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = task.Id, Kind = FollowUpEventKinds.SystemClosed, ActorUserId = null, OccurredAtUtc = now, Detail = completed ? "Completed by the system" : "Cancelled by the system" });
        AuditService.Record(db, completed ? AuditEventTypes.FollowUpCompleted : AuditEventTypes.FollowUpCancelled, "FollowUpTask", task.Id, null, $"{task.Kind} follow-up {(completed ? "completed" : "cancelled")} by the system for patient {task.PatientId}.");
        return true;
    }
}
