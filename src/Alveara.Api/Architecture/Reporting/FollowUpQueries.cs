using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.FollowUps;
using Alveara.Api.Data;

namespace Alveara.Api.Architecture.Reporting;

/// <summary>One follow-up task as of a moment: its facts plus three judgements made at that moment. <see cref="Overdue"/>: still open and past its due time. <see cref="Unassigned"/>: open with nobody responsible. <see cref="Late"/>: completed by a person after its due time.</summary>
public sealed class TaskRow
{
    public Guid Id { get; set; }
    public Guid PatientId { get; set; }
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTimeOffset DueAtUtc { get; set; }
    public DateTimeOffset? ClosedAtUtc { get; set; }
    public Guid? AssigneeUserId { get; set; }
    public bool ClosedBySystem { get; set; }
    public bool Overdue { get; set; }
    public bool Unassigned { get; set; }
    public bool Late { get; set; }
}

/// <summary>
/// The outcome cells of the follow-up report. Every task is in exactly ONE of the first five (<see cref="Completed"/> and <see cref="Cancelled"/> by a person, <see cref="ClosedBySystem"/>, <see cref="OpenNotYetDue"/>, <see cref="OpenOverdue"/>); the last two are
/// further cuts (<see cref="CompletedLate"/> of the completed, <see cref="OpenUnassigned"/> of the open).
/// </summary>
public static class FollowUpOutcomes
{
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
    public const string ClosedBySystem = "ClosedBySystem";
    public const string OpenNotYetDue = "OpenNotYetDue";
    public const string OpenOverdue = "OpenOverdue";
    public const string CompletedLate = "CompletedLate";
    public const string OpenUnassigned = "OpenUnassigned";
    public static readonly IReadOnlyList<string> All = [Completed, Cancelled, ClosedBySystem, OpenNotYetDue, OpenOverdue, CompletedLate, OpenUnassigned];
    public static readonly IReadOnlyList<string> Exclusive = [Completed, Cancelled, ClosedBySystem, OpenNotYetDue, OpenOverdue];

    /// <summary>The kinds of task, in the order the report lists them.</summary>
    public static readonly IReadOnlyList<string> Kinds = [FollowUpKinds.Manual, FollowUpKinds.Recall, FollowUpKinds.UnscheduledTreatment, FollowUpKinds.ReferralFollowUp, FollowUpKinds.LabCaseDue];

    /// <summary>The one definition of an outcome, used by the report (on the grouped rows) and by the drill-down (in the database), so the two can never disagree.</summary>
    public static Expression<Func<TaskRow, bool>> Predicate(string outcome) => outcome switch
    {
        Completed => t => t.Status == FollowUpStatuses.Completed && !t.ClosedBySystem,
        Cancelled => t => t.Status == FollowUpStatuses.Cancelled && !t.ClosedBySystem,
        ClosedBySystem => t => t.Status != FollowUpStatuses.Open && t.ClosedBySystem,
        OpenNotYetDue => t => t.Status == FollowUpStatuses.Open && !t.Overdue,
        OpenOverdue => t => t.Status == FollowUpStatuses.Open && t.Overdue,
        CompletedLate => t => t.Late,
        OpenUnassigned => t => t.Unassigned,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}

/// <summary>ALV-N006 slice 4: the follow-up and recall tasks due in a range, judged as of <c>asOf</c> (an open task is overdue when its due time is before that moment).</summary>
public sealed class FollowUpQueries(AlveraDbContext db)
{
    public IQueryable<TaskRow> Tasks(DateTimeOffset start, DateTimeOffset end, DateTimeOffset asOf) =>
        db.FollowUpTasks.AsNoTracking().Where(t => t.DueAtUtc >= start && t.DueAtUtc < end).Select(t => new TaskRow
        {
            Id = t.Id, PatientId = t.PatientId, Kind = t.Kind, Status = t.Status, Priority = t.Priority, DueAtUtc = t.DueAtUtc, ClosedAtUtc = t.ClosedAtUtc, AssigneeUserId = t.AssigneeUserId, ClosedBySystem = t.ClosedBySystem,
            Overdue = t.Status == FollowUpStatuses.Open && t.DueAtUtc < asOf,
            Unassigned = t.Status == FollowUpStatuses.Open && t.AssigneeUserId == null,
            Late = t.Status == FollowUpStatuses.Completed && !t.ClosedBySystem && t.ClosedAtUtc != null && t.ClosedAtUtc > t.DueAtUtc,
        });
}
