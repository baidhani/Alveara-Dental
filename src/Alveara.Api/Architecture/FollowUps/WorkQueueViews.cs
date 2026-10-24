namespace Alveara.Api.Architecture.FollowUps;

/// <summary>The work queue's filters and paging, as the caller sent them (all optional; checked by the service, which refuses an unknown value with a stable code).</summary>
public sealed record QueueQuery(string? Filter, string? Priority, string? Kind, string? Owner, string? Status, int? Page, int? PageSize);

/// <summary>One line of the work queue: the task, whose patient it is, the linked appointment's time (if any) and how urgent it is (<c>Overdue</c>, <c>DueSoon</c>, <c>Later</c> or <c>Closed</c>).</summary>
public sealed record QueueItem(FollowUpView Task, string PatientName, DateTimeOffset? AppointmentStartUtc, string Urgency);

/// <summary>How many OPEN tasks each filter would show, whatever filter is on now.</summary>
public sealed record QueueCounts(int Overdue, int DueSoon, int Unassigned, int Mine, int All);

public sealed record QueuePage(IReadOnlyList<QueueItem> Items, int Total, int Page, int PageSize, QueueCounts Counts, int DueSoonDays);

public sealed record AssignFollowUpRequest(Guid? AssigneeUserId, string? RowVersion);
public sealed record ClaimFollowUpRequest(string? RowVersion);
public sealed record SetPriorityRequest(string? Priority, string? RowVersion);
public sealed record LinkAppointmentRequest(Guid? AppointmentId, string? RowVersion);

public static class QueueFilters
{
    public const string Overdue = "overdue";
    public const string DueSoon = "duesoon";
    public const string Unassigned = "unassigned";
    public const string Mine = "mine";
    public const string All = "all";
    public static readonly IReadOnlyList<string> Names = [Overdue, DueSoon, Unassigned, Mine, All];

    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
}
