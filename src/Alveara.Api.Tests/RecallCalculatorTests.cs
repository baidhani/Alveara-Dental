using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Recall;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-009-C01, the pure arithmetic of recall without a database: the next-due date is the practice-local date of the last completed visit plus whole days at 09:00 practice time (Chicago in these tests), unmoved by a
/// late-evening visit, a daylight-saving change or a leap day; and the verdict (due, not yet, opted out, no visit, lapsed, already open, already booked) with its boundaries and its order of precedence.
/// </summary>
public class RecallCalculatorTests
{
    private static readonly Alveara.Api.Architecture.Time.IPracticeClock Clock = PatientTestSupport.Clock;
    private static DateTimeOffset Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0) => new(y, mo, d, h, mi, s, TimeSpan.Zero);

    // ---------- next due ----------

    [Fact]
    public void Next_due_is_the_visit_date_plus_the_interval_at_nine_in_the_morning_practice_time()
    {
        // visit 2026-01-10 10:00 CST (16:00Z) + 180 days = 2026-07-09, 09:00 CDT (14:00Z)
        Assert.Equal(Utc(2026, 7, 9, 14), RecallCalculator.NextDue(Utc(2026, 1, 10, 16), 180, Clock));
    }

    [Fact]
    public void A_late_evening_visit_counts_for_the_day_the_practice_saw_it()
    {
        // 23:30 CST on 10 January is already 11 January in UTC
        var lateEvening = Utc(2026, 1, 11, 5, 30);
        Assert.Equal(Utc(2026, 7, 9, 14), RecallCalculator.NextDue(lateEvening, 180, Clock));
        Assert.Equal(RecallCalculator.NextDue(Utc(2026, 1, 10, 16), 180, Clock), RecallCalculator.NextDue(lateEvening, 180, Clock));   // the same as a morning visit that day
    }

    [Fact]
    public void A_daylight_saving_change_never_moves_the_due_date_by_a_day()
    {
        // spring forward on 8 March 2026: the day after a 7 March visit is 9 o'clock CDT (14:00Z), not 15:00Z
        Assert.Equal(Utc(2026, 3, 8, 14), RecallCalculator.NextDue(Utc(2026, 3, 7, 18), 1, Clock));
        // fall back on 1 November 2026: the day after a 31 October visit is 9 o'clock CST (15:00Z), not 14:00Z
        Assert.Equal(Utc(2026, 11, 1, 15), RecallCalculator.NextDue(Utc(2026, 10, 31, 17), 1, Clock));
        // and the due hour is always 9 o'clock on the wall clock
        foreach (var offset in new[] { 1, 30, 180, 365 })
            Assert.Equal(9, Clock.ToPracticeLocal(RecallCalculator.NextDue(Utc(2026, 3, 7, 18), offset, Clock)).Hour);
    }

    [Fact]
    public void A_leap_day_and_a_month_end_are_counted_in_whole_days()
    {
        // 29 February 2024 + 365 days is 28 February 2025
        Assert.Equal(Utc(2025, 2, 28, 15), RecallCalculator.NextDue(Utc(2024, 2, 29, 18), 365, Clock));
        // 31 January + 30 days is 2 March in a common year
        Assert.Equal(Utc(2026, 3, 2, 15), RecallCalculator.NextDue(Utc(2026, 1, 31, 18), 30, Clock));
    }

    [Theory]
    [InlineData(1, 2026, 1, 2)]
    [InlineData(3650, 2035, 12, 30)]
    public void The_shortest_and_longest_intervals_work(int days, int year, int month, int day)
    {
        var due = Clock.ToPracticeLocal(RecallCalculator.NextDue(Utc(2026, 1, 1, 18), days, Clock));
        Assert.Equal((year, month, day, 9), (due.Year, due.Month, due.Day, due.Hour));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void An_interval_outside_one_to_3650_days_is_refused(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecallCalculator.NextDue(Utc(2026, 1, 1), days, Clock));
        Assert.False(RecallRules.IntervalIsValid(days));
    }

    [Fact]
    public void A_patients_own_interval_replaces_the_default()
    {
        Assert.Equal(90, RecallCalculator.EffectiveInterval(180, 90));
        Assert.Equal(180, RecallCalculator.EffectiveInterval(180, null));
    }

    // ---------- the verdict ----------

    private static readonly DateTimeOffset Now = Utc(2026, 10, 24, 15);              // 10:00 on 24 October 2026 in Chicago

    private static RecallInput Input(DateTimeOffset? visit = null, int def = 180, int? own = null, bool optedOut = false, int lead = 30, int months = 24, bool open = false, bool booked = false) =>
        new(visit, def, own, optedOut, lead, months, open, booked);

    private static RecallDecision Decide(RecallInput input, DateTimeOffset? now = null) => RecallCalculator.Evaluate(input, now ?? Now, Clock);

    [Fact]
    public void A_patient_becomes_due_exactly_when_the_lead_time_starts()
    {
        var visit = Utc(2026, 1, 10, 16);                                              // next due 2026-07-09 14:00Z
        var threshold = Utc(2026, 7, 9, 14).AddDays(-30);
        Assert.Equal(RecallVerdict.Due, Decide(Input(visit), threshold).Verdict);
        Assert.Equal(RecallVerdict.NotDue, Decide(Input(visit), threshold.AddSeconds(-1)).Verdict);
        Assert.Equal(RecallVerdict.Due, Decide(Input(visit), Utc(2026, 9, 1)).Verdict);   // long past due is still due
        var decision = Decide(Input(visit), threshold);
        Assert.Equal((Utc(2026, 7, 9, 14), 180), (decision.NextDueUtc, decision.IntervalDays));
    }

    [Fact]
    public void With_no_lead_time_a_patient_is_due_on_the_due_date_itself()
    {
        var visit = Utc(2026, 1, 10, 16);
        var due = Utc(2026, 7, 9, 14);
        Assert.Equal(RecallVerdict.Due, Decide(Input(visit, lead: 0), due).Verdict);
        Assert.Equal(RecallVerdict.NotDue, Decide(Input(visit, lead: 0), due.AddSeconds(-1)).Verdict);
    }

    [Fact]
    public void The_patients_own_interval_decides_when_they_are_due()
    {
        var visit = Utc(2026, 6, 1, 16);                                               // 90 days: 30 August; 180 days: 28 November
        Assert.Equal(RecallVerdict.Due, Decide(Input(visit, own: 90), Utc(2026, 9, 1)).Verdict);
        Assert.Equal(RecallVerdict.NotDue, Decide(Input(visit), Utc(2026, 9, 1)).Verdict);
        Assert.Equal(90, Decide(Input(visit, own: 90)).IntervalDays);
    }

    [Fact]
    public void A_patient_seen_two_years_ago_is_recalled_but_one_day_longer_has_lapsed()
    {
        Assert.Equal(RecallVerdict.Due, Decide(Input(Utc(2024, 10, 24, 15))).Verdict);          // exactly 24 months before the local date 24 October 2026
        Assert.Equal(RecallVerdict.LapsedPatient, Decide(Input(Utc(2024, 10, 23, 15))).Verdict);
        Assert.Equal(RecallVerdict.Due, Decide(Input(Utc(2024, 10, 23, 15), months: 25)).Verdict);   // a longer limit keeps the patient in
    }

    [Fact]
    public void An_opted_out_patient_and_a_patient_never_seen_are_not_recalled()
    {
        Assert.Equal(RecallVerdict.OptedOut, Decide(Input(Utc(2026, 1, 1), optedOut: true)).Verdict);
        Assert.Equal(RecallVerdict.NoVisit, Decide(Input(null)).Verdict);
        Assert.Null(Decide(Input(null)).NextDueUtc);
    }

    [Fact]
    public void An_open_recall_or_a_future_appointment_means_no_new_recall()
    {
        var visit = Utc(2026, 1, 10, 16);
        Assert.Equal(RecallVerdict.OpenRecall, Decide(Input(visit, open: true)).Verdict);
        Assert.Equal(RecallVerdict.AlreadyBooked, Decide(Input(visit, booked: true)).Verdict);
        Assert.NotNull(Decide(Input(visit, open: true)).NextDueUtc);                   // the date is still worked out
    }

    [Fact]
    public void The_verdicts_have_a_fixed_order_of_precedence()
    {
        var visit = Utc(2026, 1, 10, 16);
        Assert.Equal(RecallVerdict.OptedOut, Decide(Input(null, optedOut: true, open: true, booked: true)).Verdict);            // opting out beats everything
        Assert.Equal(RecallVerdict.NoVisit, Decide(Input(null, open: true, booked: true)).Verdict);                              // nothing to count from beats an open task
        Assert.Equal(RecallVerdict.LapsedPatient, Decide(Input(Utc(2020, 1, 1), open: true, booked: true)).Verdict);             // a lapsed patient is not worked automatically at all
        Assert.Equal(RecallVerdict.OpenRecall, Decide(Input(visit, open: true, booked: true)).Verdict);                          // an open task is named before an appointment
        Assert.Equal(RecallVerdict.AlreadyBooked, Decide(Input(visit, booked: true)).Verdict);
    }

    [Theory]
    [InlineData(-1, 24)]
    [InlineData(366, 24)]
    [InlineData(30, 0)]
    [InlineData(30, 121)]
    public void A_lead_time_or_visit_age_limit_out_of_range_is_refused(int lead, int months)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Decide(Input(Utc(2026, 1, 1), lead: lead, months: months)));
    }

    [Fact]
    public void The_defaults_are_inside_their_own_limits()
    {
        Assert.True(RecallRules.IntervalIsValid(RecallRules.DefaultIntervalDays));
        Assert.InRange(RecallRules.DefaultLeadDays, 0, RecallRules.MaxLeadDays);
        Assert.InRange(RecallRules.DefaultMaxVisitAgeMonths, 1, RecallRules.MaxVisitAgeMonthsLimit);
        Assert.Equal((180, 30, 24, 9), (RecallRules.DefaultIntervalDays, RecallRules.DefaultLeadDays, RecallRules.DefaultMaxVisitAgeMonths, RecallRules.DueHourLocal));
    }
}

/// <summary>ALV-009-C01: who holds the recall-settings permission by default (the owner approved: the administrator and the office manager), and that it is not protected so the policy editor can change it.</summary>
public class RecallPermissionTests
{
    [Theory]
    [InlineData(Role.Admin, true)]
    [InlineData(Role.OfficeManager, true)]
    [InlineData(Role.Dentist, false)]
    [InlineData(Role.Hygienist, false)]
    [InlineData(Role.Assistant, false)]
    [InlineData(Role.FrontDesk, false)]
    [InlineData(Role.Billing, false)]
    [InlineData(Role.Unassigned, false)]
    public void Only_the_administrator_and_the_office_manager_manage_the_recall_settings_by_default(Role role, bool expected)
    {
        Assert.Equal(expected, PermissionMatrix.RoleHas(role, Permission.ManageRecallSettings));
    }

    [Fact]
    public void The_recall_settings_permission_is_not_protected_so_it_can_be_granted_revoked_and_delegated()
    {
        Assert.True(PermissionProtection.IsGrantable(Permission.ManageRecallSettings));
        Assert.True(PermissionProtection.IsDelegable(Permission.ManageRecallSettings));
        Assert.DoesNotContain(Permission.ManageRecallSettings, PermissionProtection.AdminAssignableNeverDelegable);
    }

    [Fact]
    public void Changing_one_patients_recall_interval_needs_only_the_follow_up_permission_the_clinical_roles_already_hold()
    {
        foreach (var role in new[] { Role.Dentist, Role.Hygienist, Role.Assistant, Role.FrontDesk, Role.OfficeManager })
            Assert.True(PermissionMatrix.RoleHas(role, Permission.ManageFollowUps));
    }
}
