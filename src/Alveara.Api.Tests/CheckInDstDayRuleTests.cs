using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2, briefing section 8 row "Day boundaries" (injected clock: 23:59/00:00, zone behind UTC, both DST days), the part VisitMovePolicyTests did not cover: the check-in day
/// rule (OD-12, briefing 5.5) on the two daylight-saving days of the practice zone (America/Chicago, behind UTC). 2030-03-10 has 23 hours (02:00 does not exist); 2030-11-03 has
/// 25 hours (01:00-02:00 happens twice). On both, check-in is refused one minute before the practice day starts and once the next practice day has begun, allowed in the day's first
/// and last minute (when the UTC date is already the next day) - and the actions list the screens draw agrees with the route at each of those instants.
/// </summary>
public class CheckInDstDayRuleTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        await using var db = _fixture.CreateContext();   // both DST days are Sundays: Dr. Rivera works every day here
        await new StaffProviderService(db, Clock).ReplaceCurrentAsync(_s.ProviderA,
            [.. Enum.GetValues<DayOfWeek>().Select(d => new AvailabilityWindow(d, "08:00", "17:00"))], _s.Actor);
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>An instant given as practice-local wall time with its offset that day (CST -6, CDT -5).</summary>
    private static IPracticeClock At(int year, int month, int day, int hour, int minute, int offsetHours) =>
        new TestClock(new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours)));

    private async Task<AppointmentView> ConfirmedAsync(Guid patient, DateTime startLocal, IPracticeClock clock)
    {
        var a = (await _s.ScheduleAsync(_s.Request(patient, startLocal))).Appointment;
        return await _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.Confirmed, a.RowVersion, _s.Desk, default), clock);
    }

    private Task<AppointmentView> CheckInAsync(AppointmentView a, IPracticeClock clock) =>
        _s.FlowAsync(f => f.TransitionAsync(a.Id, VisitStates.CheckedIn, a.RowVersion, _s.Desk, default), clock);

    /// <summary>What the actions list says about check-in at that instant: null when it is offered enabled, otherwise its blocking code.</summary>
    private async Task<string?> CheckInBlockAsync(Guid id, IPracticeClock clock)
    {
        await using var db = _fixture.CreateContext();
        var list = await new VisitActionsService(db, clock, PolicyTestSupport.Defaults()).ForAsync(id, _s.Desk, default);
        var checkIn = Assert.Single(list.Actions, x => x.Code == VisitActionCodes.CheckIn);
        return checkIn.Enabled ? null : checkIn.BlockedBy;
    }

    public static TheoryData<string, int, int, int, int, int, int> Days => new()
    {
        // name,             month, day, offset before the change (day start), offset after it (day end), offset the evening before, offset of the next midnight
        { "spring forward",  3,     10,  -6,                                   -5,                          -6,                         -5 },
        { "fall back",       11,    3,   -5,                                   -6,                          -5,                         -6 },
    };

    [Theory]
    [MemberData(nameof(Days))]
    public async Task On_a_daylight_saving_day_check_in_follows_the_practice_day_and_the_actions_list_agrees(string name, int month, int day, int startOffset, int endOffset, int eveningOffset, int nextMidnightOffset)
    {
        var dayDate = new DateTime(2030, month, day);
        var before = At(2030, month, day - 1, 23, 59, eveningOffset);             // one minute before the practice day
        var first = At(2030, month, day, 0, 0, startOffset);                      // its first minute
        var last = At(2030, month, day, 23, 59, endOffset);                       // its last minute: already the next day in UTC
        var after = At(2030, month, day + 1, 0, 0, nextMidnightOffset);           // the next practice day has begun
        Assert.Equal(dayDate.Day + 1, last.UtcNow.UtcDateTime.Day);                          // the zone is behind UTC: the UTC date has moved on

        var early = await ConfirmedAsync(_ann, dayDate.AddHours(9), first);
        foreach (var (clock, when) in new[] { (before, "the evening before"), (after, "the next day") })
        {
            Assert.Equal("check_in_wrong_day", (await Assert.ThrowsAsync<SchedulingException>(() => CheckInAsync(early, clock))).Code);
            Assert.Equal("check_in_wrong_day", await CheckInBlockAsync(early.Id, clock));
        }
        Assert.Null(await CheckInBlockAsync(early.Id, first));
        Assert.Equal(VisitStates.CheckedIn, (await CheckInAsync(early, first)).FlowState);

        var late = await ConfirmedAsync(_bo, dayDate.AddHours(11), first);
        Assert.Null(await CheckInBlockAsync(late.Id, last));
        Assert.Equal(VisitStates.CheckedIn, (await CheckInAsync(late, last)).FlowState);   // a late arrival on the long or short day is still that day
        Assert.False(string.IsNullOrEmpty(name));
    }
}
