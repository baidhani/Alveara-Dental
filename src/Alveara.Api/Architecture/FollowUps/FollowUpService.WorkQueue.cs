using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;

namespace Alveara.Api.Architecture.FollowUps;

/// <summary>
/// ALV-009-C01: the work queue and its quick actions, on top of the follow-up service (same rules, same helpers). The queue is the open tasks, most urgent first (priority, then due time), with the filters staff use all day:
/// overdue, due soon (within the practice's "due soon" setting), unassigned, mine, all; narrowed by priority, kind and owner; and the counts of each filter so the dashboard can say so without fetching them. The actions are assign
/// (also reassign and unassign), claim (take it for yourself), set priority and link or unlink an appointment of the patient; each carries the version the screen showed (a stale one is <c>task_changed</c> 409 and nothing is stored),
/// each records a history event and an audit entry in the same save, and doing what is already done is a quiet replay. A closed task cannot be changed (<c>task_closed</c>). Who may call them is the controller's business.
/// </summary>
public partial class FollowUpService
{
    // ---------- the queue ----------

    public async Task<QueuePage> QueueAsync(Guid actor, QueueQuery q, CancellationToken ct)
    {
        var filter = (q.Filter ?? QueueFilters.All).ToLowerInvariant();
        if (!QueueFilters.Names.Contains(filter)) throw new FollowUpException("filter_invalid", "The filter is overdue, duesoon, unassigned, mine or all.", 400);
        var status = (q.Status ?? "open").ToLowerInvariant();
        if (status is not ("open" or "closed" or "all")) throw new FollowUpException("status_invalid", "The status filter is open, closed or all.", 400);
        if (q.Priority is not null && !FollowUpPriorities.All.Contains(q.Priority)) throw new FollowUpException("priority_invalid", "The priority is Low, Normal, High or Urgent.", 400);
        if (q.Kind is not null && !FollowUpKinds.All.Contains(q.Kind)) throw new FollowUpException("kind_invalid", "The kind is Manual, Recall or UnscheduledTreatment.", 400);
        Guid? owner = null;
        var noOwner = q.Owner is not null && q.Owner.Equals("none", StringComparison.OrdinalIgnoreCase);
        if (q.Owner is not null && !noOwner) owner = Guid.TryParse(q.Owner, out var g) ? g : throw new FollowUpException("owner_invalid", "The owner is a person or none.", 400);
        var pageSize = Math.Clamp(q.PageSize ?? QueueFilters.DefaultPageSize, 1, QueueFilters.MaxPageSize);
        var page = Math.Max(q.Page ?? 1, 1);

        var settings = await db.RecallSettings.AsNoTracking().SingleAsync(ct);
        var now = clock.UtcNow;
        var soon = now.AddDays(settings.DueSoonDays);
        var open = db.FollowUpTasks.AsNoTracking().Where(t => t.Status == FollowUpStatuses.Open);
        var counts = new QueueCounts(
            await open.CountAsync(t => t.DueAtUtc < now, ct), await open.CountAsync(t => t.DueAtUtc >= now && t.DueAtUtc <= soon, ct), await open.CountAsync(t => t.AssigneeUserId == null, ct),
            await open.CountAsync(t => t.AssigneeUserId == actor, ct), await open.CountAsync(ct));

        IQueryable<FollowUpTask> query = db.FollowUpTasks.AsNoTracking();
        // the named filters are about open work; "all" with a status shows what the status says
        query = filter switch
        {
            QueueFilters.Overdue => query.Where(t => t.Status == FollowUpStatuses.Open && t.DueAtUtc < now),
            QueueFilters.DueSoon => query.Where(t => t.Status == FollowUpStatuses.Open && t.DueAtUtc >= now && t.DueAtUtc <= soon),
            QueueFilters.Unassigned => query.Where(t => t.Status == FollowUpStatuses.Open && t.AssigneeUserId == null),
            QueueFilters.Mine => query.Where(t => t.Status == FollowUpStatuses.Open && t.AssigneeUserId == actor),
            _ => status switch { "open" => query.Where(t => t.Status == FollowUpStatuses.Open), "closed" => query.Where(t => t.Status != FollowUpStatuses.Open), _ => query },
        };
        if (q.Priority is not null) query = query.Where(t => t.Priority == q.Priority);
        if (q.Kind is not null) query = query.Where(t => t.Kind == q.Kind);
        if (noOwner) query = query.Where(t => t.AssigneeUserId == null);
        else if (owner is { } who) query = query.Where(t => t.AssigneeUserId == who);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(t => t.Status == FollowUpStatuses.Open ? 0 : 1)
            .ThenBy(t => t.Priority == FollowUpPriorities.Urgent ? 0 : t.Priority == FollowUpPriorities.High ? 1 : t.Priority == FollowUpPriorities.Normal ? 2 : 3)
            .ThenBy(t => t.DueAtUtc).ThenBy(t => t.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var views = await ViewsAsync(rows, ct);
        var patientIds = rows.Select(r => r.PatientId).Distinct().ToList();
        var patients = await db.Patients.AsNoTracking().Where(p => patientIds.Contains(p.Id)).Select(p => new { p.Id, p.FirstName, p.LastName }).ToDictionaryAsync(p => p.Id, p => $"{p.FirstName} {p.LastName}", ct);
        var appointmentIds = rows.Where(r => r.AppointmentId != null).Select(r => r.AppointmentId!.Value).Distinct().ToList();
        var starts = appointmentIds.Count == 0 ? [] : await db.Appointments.AsNoTracking().Where(a => appointmentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.StartUtc, ct);
        var items = views.Select(v => new QueueItem(v, patients.GetValueOrDefault(v.PatientId, ""), v.AppointmentId is { } a && starts.TryGetValue(a, out var s) ? s : null,
            v.Status != FollowUpStatuses.Open ? "Closed" : v.DueAtUtc < now ? "Overdue" : v.DueAtUtc <= soon ? "DueSoon" : "Later")).ToList();
        return new QueuePage(items, total, page, pageSize, counts, settings.DueSoonDays);
    }

    // ---------- the quick actions ----------

    /// <summary>Gives the task to a person, or to nobody (null puts it back in the unassigned queue). The same owner again is a quiet replay.</summary>
    public async Task<FollowUpResult> AssignAsync(Guid actor, Guid taskId, AssignFollowUpRequest request, CancellationToken ct)
    {
        var task = await LoadOpenAsync(taskId, ct);
        if (task.AssigneeUserId == request.AssigneeUserId) return new FollowUpResult(await ViewAsync(task, ct), true, null);
        var version = FollowUpValidation.Version(request.RowVersion);
        if (request.AssigneeUserId is { } person) await RequireAssigneeAsync(person, ct);
        if (request.AssigneeUserId is null && task.Kind == FollowUpKinds.Manual) throw new FollowUpException("assignee_invalid", "A follow-up someone created needs a responsible person.", 400);
        return await ChangeOwnerAsync(actor, task, request.AssigneeUserId, version, ct);
    }

    /// <summary>Takes the task for the person asking (they must be someone who may be given follow-ups). Already theirs is a quiet replay.</summary>
    public async Task<FollowUpResult> ClaimAsync(Guid actor, Guid taskId, ClaimFollowUpRequest request, CancellationToken ct)
    {
        var task = await LoadOpenAsync(taskId, ct);
        if (task.AssigneeUserId == actor) return new FollowUpResult(await ViewAsync(task, ct), true, null);
        var version = FollowUpValidation.Version(request.RowVersion);
        await RequireAssigneeAsync(actor, ct);
        return await ChangeOwnerAsync(actor, task, actor, version, ct);
    }

    public async Task<FollowUpResult> SetPriorityAsync(Guid actor, Guid taskId, SetPriorityRequest request, CancellationToken ct)
    {
        var priority = request.Priority is { } p && FollowUpPriorities.All.Contains(p) ? p : throw new FollowUpException("priority_invalid", "The priority is Low, Normal, High or Urgent.", 400);
        var task = await LoadOpenAsync(taskId, ct);
        if (task.Priority == priority) return new FollowUpResult(await ViewAsync(task, ct), true, null);
        var version = FollowUpValidation.Version(request.RowVersion);
        var now = clock.UtcNow;
        var before = task.Priority;
        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;
        task.Priority = priority;
        AddEvent(task.Id, FollowUpEventKinds.PriorityChanged, actor, now, $"Priority {priority}");
        AuditService.Record(db, AuditEventTypes.FollowUpPriorityChanged, "FollowUpTask", task.Id, actor, $"Follow-up priority changed from {before} to {priority}.");
        await SaveAsync(task.Id, ct);
        return new FollowUpResult(await ViewAsync(task, ct), false, null);
    }

    /// <summary>Links an appointment of the task's own patient, or removes the link (null). The same link again is a quiet replay.</summary>
    public async Task<FollowUpResult> LinkAppointmentAsync(Guid actor, Guid taskId, LinkAppointmentRequest request, CancellationToken ct)
    {
        var task = await LoadOpenAsync(taskId, ct);
        if (task.AppointmentId == request.AppointmentId) return new FollowUpResult(await ViewAsync(task, ct), true, null);
        var version = FollowUpValidation.Version(request.RowVersion);
        if (request.AppointmentId is { } appointment && !await db.Appointments.AsNoTracking().AnyAsync(a => a.Id == appointment && a.PatientId == task.PatientId, ct))
            throw new FollowUpException("appointment_invalid", "Choose an appointment of this patient.", 400);
        var now = clock.UtcNow;
        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;
        task.AppointmentId = request.AppointmentId;
        AddEvent(task.Id, FollowUpEventKinds.Edited, actor, now, request.AppointmentId is null ? "Appointment unlinked" : "Appointment linked");
        AuditService.Record(db, AuditEventTypes.FollowUpChanged, "FollowUpTask", task.Id, actor, $"Follow-up appointment link {(request.AppointmentId is null ? "removed" : "set")}.");
        await SaveAsync(task.Id, ct);
        return new FollowUpResult(await ViewAsync(task, ct), false, null);
    }

    // ---------- helpers ----------

    private async Task<FollowUpTask> LoadOpenAsync(Guid taskId, CancellationToken ct)
    {
        var task = await LoadAsync(taskId, ct);
        if (task.Status != FollowUpStatuses.Open) throw new FollowUpException("task_closed", "That follow-up is already closed and cannot be changed.", 409);
        return task;
    }

    private async Task<FollowUpResult> ChangeOwnerAsync(Guid actor, FollowUpTask task, Guid? newOwner, byte[] version, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var wasUnassigned = task.AssigneeUserId is null;
        db.Entry(task).Property(t => t.RowVersion).OriginalValue = version;
        task.AssigneeUserId = newOwner;
        AddEvent(task.Id, wasUnassigned && newOwner is not null ? FollowUpEventKinds.Assigned : FollowUpEventKinds.Reassigned, actor, now, $"Assignee {newOwner?.ToString() ?? "none"}");
        AuditService.Record(db, AuditEventTypes.FollowUpAssigned, "FollowUpTask", task.Id, actor, newOwner is null ? "Follow-up returned to the unassigned queue." : $"Follow-up assigned to {newOwner}.");
        await SaveAsync(task.Id, ct);
        return new FollowUpResult(await ViewAsync(task, ct), false, null);
    }
}
