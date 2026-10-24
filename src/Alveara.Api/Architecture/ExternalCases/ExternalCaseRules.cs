using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.ExternalCases;

/// <summary>ALV-009-C02: the two kinds of outside work tracked here, and the two kinds of outside party that do it.</summary>
public static class ExternalCaseKinds
{
    public const string Referral = "Referral";
    public const string Lab = "Lab";
    public static readonly IReadOnlyList<string> All = [Referral, Lab];

    /// <summary>The kind of party a case of this kind must name: a referral goes to a referral provider, a lab case to a lab vendor.</summary>
    public static string PartyKindFor(string caseKind) => caseKind == Lab ? ExternalPartyKinds.LabVendor : ExternalPartyKinds.ReferralProvider;
}

public static class ExternalPartyKinds
{
    public const string ReferralProvider = "ReferralProvider";
    public const string LabVendor = "LabVendor";
    public static readonly IReadOnlyList<string> All = [ReferralProvider, LabVendor];
}

/// <summary>
/// ALV-009-C02: the statuses of a referral or lab case, the moves between them, what a move needs, and when a case is overdue. Pure and deterministic: the service applies these rules, the database holds the same sets
/// as check constraints, and the tests prove both. A referral is Draft, Sent (to the provider), Received (their report is back), Completed or Cancelled; a lab case also has InProgress (the lab acknowledged it) and
/// may go back from Received to Sent when the work is returned for a remake (which needs a note). Completed and Cancelled are final; a cancellation always needs a reason.
/// </summary>
public static class ExternalCaseRules
{
    public const string Draft = "Draft";
    public const string Sent = "Sent";
    public const string InProgress = "InProgress";
    public const string Received = "Received";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> ReferralStatuses = [Draft, Sent, Received, Completed, Cancelled];
    public static readonly IReadOnlyList<string> LabStatuses = [Draft, Sent, InProgress, Received, Completed, Cancelled];

    public const int NoteMaxLength = 1000;
    public const int ReasonMaxLength = 500;
    public const int PartyNameMaxLength = 200;
    public const int ContactMaxLength = 200;
    public const int MaterialMaxLength = 100;
    public const int ShadeMaxLength = 50;
    public const int TitleMaxLength = 200;
    /// <summary>A case's due date may be this many days in the past (back-filling a case already sent) and this many years ahead.</summary>
    public const int DueDateMaxDaysPast = 365;
    public const int DueDateMaxYearsAhead = 2;
    /// <summary>The hour (practice time) a case's follow-up task is due on its due date, the same as a recall task's.</summary>
    public const int TaskDueHourLocal = 9;

    public static IReadOnlyList<string> StatusesOf(string kind) => kind == ExternalCaseKinds.Lab ? LabStatuses : ReferralStatuses;

    public static bool IsKnown(string kind, string status) => StatusesOf(kind).Contains(status);

    public static bool IsFinal(string status) => status is Completed or Cancelled;

    /// <summary>A case still being worked: anything not final.</summary>
    public static bool IsOpen(string status) => !IsFinal(status);

    /// <summary>The statuses a case can move to from <paramref name="from"/> (empty when final or unknown).</summary>
    public static IReadOnlyList<string> NextStatuses(string kind, string from)
    {
        var lab = kind == ExternalCaseKinds.Lab;
        return from switch
        {
            Draft => [Sent, Cancelled],
            Sent => lab ? [InProgress, Received, Cancelled] : [Received, Cancelled],
            InProgress when lab => [Received, Cancelled],
            Received => lab ? [Completed, Sent, Cancelled] : [Completed, Cancelled],
            _ => [],
        };
    }

    public static bool CanMove(string kind, string from, string to) => NextStatuses(kind, from).Contains(to);

    /// <summary>A cancellation needs a reason; sending a received lab case back for a remake needs a note saying why.</summary>
    public static bool NoteRequired(string kind, string from, string to) => to == Cancelled || (kind == ExternalCaseKinds.Lab && from == Received && to == Sent);

    /// <summary>The statuses in which a due date means something: the case has gone out and has not been finished. A draft has not been sent, so it cannot be late.</summary>
    public static bool Tracks(string status) => status is Sent or InProgress or Received;

    /// <summary>Overdue: it has gone out, is not finished, and its due date (a practice-local calendar date) is before today's.</summary>
    public static bool IsOverdue(string status, DateOnly? dueOn, DateOnly todayLocal) => Tracks(status) && dueOn is { } due && due < todayLocal;

    /// <summary>The follow-up task for a case exists from its due date until the case is final: due today or earlier and still going out or waiting.</summary>
    public static bool WantsTask(string status, DateOnly? dueOn, DateOnly todayLocal) => Tracks(status) && dueOn is { } due && due <= todayLocal;

    /// <summary>The moment the follow-up task is due: the due date at <see cref="TaskDueHourLocal"/> practice time.</summary>
    public static DateTimeOffset TaskDueAt(DateOnly dueOn, IPracticeClock clock) =>
        clock.FromPracticeLocal(dueOn.ToDateTime(new TimeOnly(TaskDueHourLocal, 0)), LocalTimeAmbiguityPolicy.EarlierInstant);

    /// <summary>A due date is acceptable when it is not absurdly far back or ahead of today's practice-local date.</summary>
    public static bool DueDateInRange(DateOnly dueOn, DateOnly todayLocal) =>
        dueOn >= todayLocal.AddDays(-DueDateMaxDaysPast) && dueOn <= todayLocal.AddYears(DueDateMaxYearsAhead);

    /// <summary>A date a case went out or came back must not be in the future of the practice's today, nor before 2000.</summary>
    public static bool EventDateValid(DateOnly date, DateOnly todayLocal) => date >= new DateOnly(2000, 1, 1) && date <= todayLocal;
}
