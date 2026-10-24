using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Permissions;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>
/// STORY-009: follow-up tasks for a patient (docs/design/FOLLOW_UP_TASKS_DESIGN.md). Every write does its task change, its history events and its audit entry in ONE save, so any failure (the audit write included)
/// changes nothing. A create is idempotent by construction: the task's id is made from the person and the Idempotency-Key, so a repeat meets the same primary key and answers with the task already made (a different
/// request under the same key is refused). Edit, completion and cancellation carry the row version the person saw; a stale one is <c>task_changed</c> (409) and nothing is stored. Completing or cancelling a task
/// that the same person already completed or cancelled the same way is answered quietly and changes nothing; by anyone else it is <c>task_closed</c> (409). Completing a task with a recall interval creates the next
/// task in the same save; its id is made from the completed task's id, and the database allows one recall per task. The responsible party must be an enabled account whose role can view follow-ups.
/// Who may call this is decided by the permission attributes on the controller; this service decides what is allowed to be stored.
/// </summary>
public class FollowUpService(AlveraDbContext db, IPracticeClock clock, IEffectivePermissions permissions)
{
    // ---------- writes ----------

    public async Task<FollowUpResult> CreateAsync(Guid actor, Guid patientId, CreateFollowUpRequest request, string? idempotencyKey, CancellationToken ct)
    {
        var key = FollowUpValidation.Key(idempotencyKey);
        var now = clock.UtcNow;
        var title = FollowUpValidation.Title(request.Title);
        var note = FollowUpValidation.Note(request.Note);
        var due = FollowUpValidation.Due(request.DueAtUtc, now);
        var interval = FollowUpValidation.Interval(request.RecallIntervalDays);
        var assignee = request.AssigneeUserId ?? throw new FollowUpException("assignee_invalid", "Choose who is responsible for the follow-up.", 400);
        var id = FollowUpValidation.DeterministicId("create", actor, key);

        var existing = await db.FollowUpTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (existing is not null) return await ReplayCreateAsync(existing, patientId, title, note, due, assignee, interval, ct);

        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new FollowUpException("patient_not_found", "That patient was not found.", 404);
        await RequireAssigneeAsync(assignee, ct);

        var task = new FollowUpTask
        {
            Id = id, PatientId = patientId, AssigneeUserId = assignee, Title = title, Note = note, DueAtUtc = due, Status = FollowUpStatuses.Open, RecallIntervalDays = interval,
            CreatedByUserId = actor, CreatedAtUtc = now,
        };
        db.FollowUpTasks.Add(task);
        AddEvent(task.Id, FollowUpEventKinds.Created, actor, now, $"Due {due:O}");
        AuditService.Record(db, AuditEventTypes.FollowUpCreated, "FollowUpTask", task.Id, actor, $"Follow-up created for patient {patientId}, assigned to {assignee}, due {due:O}.");
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            // a concurrent request with the same key won the race: answer with the task it made
            db.ChangeTracker.Clear();
            var winner = await db.FollowUpTasks.AsNoTracking().FirstAsync(t => t.Id == id, ct);
            return await ReplayCreateAsync(winner, patientId, title, note, due, assignee, interval, ct);
        }
        return new FollowUpResult(await ViewAsync(task, ct), false, null);
    }

    public async Task<FollowUpResult> UpdateAsync(Guid actor, Guid taskId, UpdateFollowUpRequest request, CancellationToken ct)
    {
        var version = FollowUpValidation.Version(request.RowVersion);
        var now = clock.UtcNow;
        var title = FollowUpValidation.Title(request.Title);
        var note = FollowUpValidation.Note(request.Note);
        var due = FollowUpValidation.Due(request.DueAtUtc, now);
        var interval = FollowUpValidation.Interval(request.RecallIntervalDays);
        var assignee = request.AssigneeUserId ?? throw new FollowUpException("assignee_invalid", "Choose who is responsible for the follow-up.", 400);

        var task = await LoadAsync(taskId, ct);
        if (task.Status != FollowUpStatuses.Open) throw new FollowUpException("task_closed", "That follow-up is already closed and cannot be changed.", 409);
        if (assignee != task.AssigneeUserId) await RequireAssigneeAsync(assignee, ct);   // an unchanged assignee is not re-checked: editing the date of a task whose assignee has since left must still be possible

        var textChanged = title != task.Title || note != task.Note || interval != task.RecallIntervalDays;
        var reassigned = assignee != task.AssigneeUserId;
        var dueChanged = due != task.DueAtUtc;
        if (!textChanged && !reassigned && !dueChanged) throw new FollowUpException("no_change", "Nothing was changed.", 409);

        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;
        task.Title = title; task.Note = note; task.RecallIntervalDays = interval; task.AssigneeUserId = assignee; task.DueAtUtc = due;
        if (textChanged) AddEvent(task.Id, FollowUpEventKinds.Edited, actor, now, null);
        if (reassigned) AddEvent(task.Id, FollowUpEventKinds.Reassigned, actor, now, $"Assignee {assignee}");
        if (dueChanged) AddEvent(task.Id, FollowUpEventKinds.DueChanged, actor, now, $"Due {due:O}");
        AuditService.Record(db, AuditEventTypes.FollowUpChanged, "FollowUpTask", task.Id, actor, $"Follow-up changed ({Describe(textChanged, reassigned, dueChanged)}).");
        await SaveAsync(task.Id, ct);
        return new FollowUpResult(await ViewAsync(task, ct), false, null);
    }

    public async Task<FollowUpResult> CompleteAsync(Guid actor, Guid taskId, CompleteFollowUpRequest request, CancellationToken ct)
    {
        var note = FollowUpValidation.Note(request.Note);
        var task = await LoadAsync(taskId, ct);
        if (task.Status == FollowUpStatuses.Completed && task.ClosedByUserId == actor && task.ClosureNote == note)
            return new FollowUpResult(await ViewAsync(task, ct), true, await RecallIdAsync(task.Id, ct));   // the same person completing it the same way again: nothing to do
        if (task.Status != FollowUpStatuses.Open) throw new FollowUpException("task_closed", $"That follow-up was already {task.Status.ToLowerInvariant()}.", 409);
        var version = FollowUpValidation.Version(request.RowVersion);
        var now = clock.UtcNow;

        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;
        Close(task, FollowUpStatuses.Completed, actor, now, note);
        AddEvent(task.Id, FollowUpEventKinds.Completed, actor, now, null);
        AuditService.Record(db, AuditEventTypes.FollowUpCompleted, "FollowUpTask", task.Id, actor, $"Follow-up completed for patient {task.PatientId}.");

        Guid? recallId = null;
        if (task.RecallIntervalDays is { } days)
        {
            recallId = FollowUpValidation.DeterministicId("recall", task.Id);
            // the next task goes to the same person when they can still receive it, otherwise to the person completing this one (an unowned recall must not be lost)
            var recipient = await CanReceiveAsync(task.AssigneeUserId, ct) ? task.AssigneeUserId : actor;
            var recall = new FollowUpTask
            {
                Id = recallId.Value, PatientId = task.PatientId, AssigneeUserId = recipient, Title = task.Title, Note = task.Note, DueAtUtc = now.AddDays(days), Status = FollowUpStatuses.Open,
                RecallIntervalDays = days, RecallOfTaskId = task.Id, CreatedByUserId = actor, CreatedAtUtc = now,
            };
            db.FollowUpTasks.Add(recall);
            AddEvent(recall.Id, FollowUpEventKinds.Created, actor, now, $"Recall of {task.Id}");
            AddEvent(task.Id, FollowUpEventKinds.RecallCreated, actor, now, $"Recall {recall.Id}");
            AuditService.Record(db, AuditEventTypes.FollowUpCreated, "FollowUpTask", recall.Id, actor, $"Recall created for patient {task.PatientId} after follow-up {task.Id}, assigned to {recipient}, due {recall.DueAtUtc:O}.");
        }
        await SaveAsync(task.Id, ct);
        return new FollowUpResult(await ViewAsync(task, ct), false, recallId);
    }

    public async Task<FollowUpResult> CancelAsync(Guid actor, Guid taskId, CancelFollowUpRequest request, CancellationToken ct)
    {
        var note = FollowUpValidation.CancelNote(request.Note);
        var task = await LoadAsync(taskId, ct);
        if (task.Status == FollowUpStatuses.Cancelled && task.ClosedByUserId == actor && task.ClosureNote == note)
            return new FollowUpResult(await ViewAsync(task, ct), true, null);
        if (task.Status != FollowUpStatuses.Open) throw new FollowUpException("task_closed", $"That follow-up was already {task.Status.ToLowerInvariant()}.", 409);
        var version = FollowUpValidation.Version(request.RowVersion);
        var now = clock.UtcNow;

        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;
        Close(task, FollowUpStatuses.Cancelled, actor, now, note);
        AddEvent(task.Id, FollowUpEventKinds.Cancelled, actor, now, null);
        AuditService.Record(db, AuditEventTypes.FollowUpCancelled, "FollowUpTask", task.Id, actor, $"Follow-up cancelled for patient {task.PatientId}.");
        await SaveAsync(task.Id, ct);
        return new FollowUpResult(await ViewAsync(task, ct), false, null);
    }

    // ---------- reads ----------

    public async Task<FollowUpDetail> GetAsync(Guid taskId, CancellationToken ct)
    {
        var task = await db.FollowUpTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, ct) ?? throw NotFound();
        var events = await db.FollowUpTaskEvents.AsNoTracking().Where(e => e.TaskId == taskId).OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Kind).ToListAsync(ct);
        var names = await NamesAsync(events.Where(e => e.ActorUserId != null).Select(e => e.ActorUserId!.Value).ToList(), ct);
        return new FollowUpDetail(await ViewAsync(task, ct),
            events.Select(e => new FollowUpEventView(e.Kind, e.ActorUserId, e.ActorUserId is { } a && names.TryGetValue(a, out var n) ? n : null, e.OccurredAtUtc, e.Detail)).ToList());
    }

    /// <summary><paramref name="status"/>: <c>open</c> (default), <c>closed</c> or <c>all</c>.</summary>
    public async Task<FollowUpPage> ForPatientAsync(Guid patientId, string? status, CancellationToken ct)
    {
        if (!await db.Patients.AsNoTracking().AnyAsync(p => p.Id == patientId, ct)) throw new FollowUpException("patient_not_found", "That patient was not found.", 404);
        return await PageAsync(db.FollowUpTasks.AsNoTracking().Where(t => t.PatientId == patientId), status, ct);
    }

    public Task<FollowUpPage> MineAsync(Guid user, string? status, CancellationToken ct) =>
        PageAsync(db.FollowUpTasks.AsNoTracking().Where(t => t.AssigneeUserId == user), status, ct);

    /// <summary>The people a task may be assigned to, by name (at most 500).</summary>
    public async Task<IReadOnlyList<FollowUpAssignee>> AssigneesAsync(CancellationToken ct)
    {
        var roles = new List<Role>();
        foreach (var r in Enum.GetValues<Role>())
            if (await permissions.HasAsync(r, Permission.ViewFollowUps, ct)) roles.Add(r);
        var rows = await db.UserAccounts.AsNoTracking().Where(u => !u.IsDisabled && roles.Contains(u.Role)).OrderBy(u => u.Username).Take(500).Select(u => new { u.Id, u.Username, u.Role }).ToListAsync(ct);
        return rows.Select(u => new FollowUpAssignee(u.Id, u.Username, u.Role.ToString())).ToList();
    }

    // ---------- helpers ----------

    private async Task<FollowUpPage> PageAsync(IQueryable<FollowUpTask> query, string? status, CancellationToken ct)
    {
        query = (status ?? "open").ToLowerInvariant() switch
        {
            "open" => query.Where(t => t.Status == FollowUpStatuses.Open),
            "closed" => query.Where(t => t.Status != FollowUpStatuses.Open),
            "all" => query,
            _ => throw new FollowUpException("status_invalid", "The status filter is open, closed or all.", 400),
        };
        var rows = await query.OrderBy(t => t.Status == FollowUpStatuses.Open ? 0 : 1).ThenBy(t => t.DueAtUtc).ThenByDescending(t => t.ClosedAtUtc).Take(FollowUpRules.MaxListSize + 1).ToListAsync(ct);
        var more = rows.Count > FollowUpRules.MaxListSize;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new FollowUpPage(await ViewsAsync(rows, ct), rows.Count, more);
    }

    private async Task<FollowUpTask> LoadAsync(Guid taskId, CancellationToken ct) =>
        await db.FollowUpTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct) ?? throw NotFound();

    private static FollowUpException NotFound() => new("task_not_found", "That follow-up was not found.", 404);

    private void AddEvent(Guid taskId, string kind, Guid? actor, DateTimeOffset at, string? detail) =>
        db.FollowUpTaskEvents.Add(new FollowUpTaskEvent { Id = Guid.NewGuid(), TaskId = taskId, Kind = kind, ActorUserId = actor, OccurredAtUtc = at, Detail = detail });

    private static void Close(FollowUpTask task, string status, Guid actor, DateTimeOffset at, string? note)
    {
        task.Status = status; task.ClosedAtUtc = at; task.ClosedByUserId = actor; task.ClosureNote = note;
    }

    private static string Describe(bool text, bool reassigned, bool due) =>
        string.Join(", ", new[] { text ? "text" : null, reassigned ? "assignee" : null, due ? "due date" : null }.Where(x => x != null));

    /// <summary>Saves everything staged in one go; a stale row version (or a recall already made by a concurrent completion) is a 409 and nothing is stored.</summary>
    private async Task SaveAsync(Guid taskId, CancellationToken ct)
    {
        try
        {
            await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, "FollowUpTask", taskId, ct);
        }
        catch (ConcurrencyConflictException)
        {
            throw new FollowUpException("task_changed", "Someone else changed this follow-up. Reload it and try again.", 409);
        }
        catch (DbUpdateException ex) when (IsDuplicateKey(ex))
        {
            throw new FollowUpException("task_changed", "Someone else changed this follow-up. Reload it and try again.", 409);
        }
    }

    private static bool IsDuplicateKey(DbUpdateException ex) => ex.InnerException is SqlException { Number: 2627 or 2601 };

    private async Task<bool> CanReceiveAsync(Guid userId, CancellationToken ct)
    {
        var account = await db.UserAccounts.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Role, u.IsDisabled }).FirstOrDefaultAsync(ct);
        return account is { IsDisabled: false } && await permissions.HasAsync(account.Role, Permission.ViewFollowUps, ct);
    }

    private async Task RequireAssigneeAsync(Guid userId, CancellationToken ct)
    {
        if (!await CanReceiveAsync(userId, ct))
            throw new FollowUpException("assignee_invalid", "The responsible person must be an enabled account that can see follow-ups.", 400);
    }

    private async Task<FollowUpResult> ReplayCreateAsync(FollowUpTask existing, Guid patientId, string title, string? note, DateTimeOffset due, Guid assignee, int? interval, CancellationToken ct)
    {
        var same = existing.PatientId == patientId && existing.Title == title && existing.Note == note && existing.DueAtUtc == due && existing.AssigneeUserId == assignee && existing.RecallIntervalDays == interval;
        if (!same) throw new FollowUpException("idempotency_key_reused", "That key was already used for a different follow-up.", 409);
        return new FollowUpResult(await ViewAsync(existing, ct), true, null);
    }

    private async Task<Guid?> RecallIdAsync(Guid taskId, CancellationToken ct) =>
        await db.FollowUpTasks.AsNoTracking().Where(t => t.RecallOfTaskId == taskId).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, string>> NamesAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : await db.UserAccounts.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

    private async Task<FollowUpView> ViewAsync(FollowUpTask task, CancellationToken ct) => (await ViewsAsync([task], ct))[0];

    private async Task<IReadOnlyList<FollowUpView>> ViewsAsync(IReadOnlyList<FollowUpTask> tasks, CancellationToken ct)
    {
        if (tasks.Count == 0) return [];
        var ids = tasks.Select(t => t.Id).ToList();
        var recalls = await db.FollowUpTasks.AsNoTracking().Where(t => t.RecallOfTaskId != null && ids.Contains(t.RecallOfTaskId!.Value)).Select(t => new { Of = t.RecallOfTaskId!.Value, t.Id }).ToListAsync(ct);
        var names = await NamesAsync(tasks.Select(t => t.AssigneeUserId).Distinct().ToList(), ct);
        var now = clock.UtcNow;
        return tasks.Select(t => new FollowUpView(
            t.Id, t.PatientId, t.Title, t.Note, t.DueAtUtc, t.Status, t.Status == FollowUpStatuses.Open && t.DueAtUtc < now, t.AssigneeUserId, names.GetValueOrDefault(t.AssigneeUserId), t.RecallIntervalDays,
            t.RecallOfTaskId, recalls.FirstOrDefault(r => r.Of == t.Id)?.Id, t.CreatedByUserId, t.CreatedAtUtc, t.ClosedAtUtc, t.ClosedByUserId, t.ClosureNote, Convert.ToBase64String(t.RowVersion))).ToList();
    }
}
