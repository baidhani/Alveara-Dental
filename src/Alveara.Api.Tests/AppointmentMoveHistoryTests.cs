using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N021 A2 (F-046, F-049, F-062, F-074; migration M4, owner decision 2026-11-15) against real SQL Server: an appointment's history names who did each thing, and every reschedule, provider change
/// and room change records where it went FROM and TO in the same save as the move. The destination is read back exactly as recorded: it is never derived from later events or from where the
/// appointment is now, so a later move, a rename of the provider or the room, or two moves at the same instant cannot change what an earlier row says. A request that changes nothing writes no event.
/// </summary>
public class AppointmentMoveHistoryTests : IAsyncLifetime
{
    private const string Why = "Dr. Rivera called in sick";
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(async () =>
    {
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AppointmentView> BookAsync(int hour = 9) => (await _s.ScheduleAsync(_s.Request(_ann, At(hour)))).Appointment;

    /// <summary>A reschedule made by Dr. Rivera's own account, so the history can name the person.</summary>
    private Task<AppointmentView> RescheduleAsync(AppointmentView a, DateTime startLocal, Guid provider, Guid operatory, IPracticeClock? clock = null) =>
        _s.ManageAsync(m => m.RescheduleAsync(a.Id, new RescheduleRequest(provider, operatory, startLocal, null, a.RowVersion), _s.ProviderAUser, default), clock);

    private Task<AppointmentView> AssignAsync(AppointmentView a, Guid provider, Guid operatory, string? reason = Why) =>
        _s.AssignAsync(v => v.AssignAsync(a.Id, new AssignVisitRequest(provider, operatory, a.RowVersion, reason), new VisitActor(_s.ProviderAUser, Role.OfficeManager), default));

    private async Task<AppointmentEventView> OnlyAsync(Guid id, string type) => Assert.Single(await _s.HistoryAsync(id), e => e.EventType == type);

    private async Task<int> MoveEventsAsync(Guid id) =>
        await _s.CountAsync(db => db.AppointmentEvents.Where(e => e.AppointmentId == id && MoveDestination.EventTypes.Contains(e.EventType)));

    private async Task SqlAsync(string sql, params object[] args)
    {
        await using var db = _fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync(sql, args);
    }

    // ---------- who ----------

    [Fact]
    public async Task Every_row_names_the_person_who_did_it_and_an_account_without_a_staff_profile_reads_as_staff_member()
    {
        var a = await BookAsync();                                                      // booked by the test actor, who has no staff profile
        await RescheduleAsync(a, At(10), _s.ProviderA, _s.Op1);                         // by Dr. Rivera's own account

        var history = await _s.HistoryAsync(a.Id);
        Assert.Equal("Staff member", history.Single(e => e.EventType == AppointmentEventTypes.Scheduled).ActorName);
        Assert.Equal("Dr. Rivera", history.Single(e => e.EventType == AppointmentEventTypes.Rescheduled).ActorName);
    }

    // ---------- from and to, per move type ----------

    [Fact]
    public async Task A_reschedule_records_its_full_destination_with_the_move()
    {
        var a = await BookAsync();
        await RescheduleAsync(a, At(11), _s.ProviderB, _s.Op2);

        var e = await OnlyAsync(a.Id, AppointmentEventTypes.Rescheduled);
        Assert.Equal(("2030-01-14T09:00", "Dr. Rivera", "Op 1"), (e.PreviousStartLocal, e.PreviousProviderName, e.PreviousOperatoryName));
        Assert.Equal(("2030-01-14T11:00", "Dr. Patel", "Op 2"), (e.NewStartLocal, e.NewProviderName, e.NewOperatoryName));

        await using var db = _fixture.CreateContext();
        var row = await db.AppointmentEvents.AsNoTracking().SingleAsync(x => x.AppointmentId == a.Id && x.EventType == AppointmentEventTypes.Rescheduled);
        Assert.Equal((_s.ProviderB, _s.Op2), (row.NewProviderProfileId, row.NewOperatoryId));
    }

    [Fact]
    public async Task A_provider_change_records_the_new_provider_and_the_new_room_when_both_change()
    {
        var a = await BookAsync();
        await AssignAsync(a, _s.ProviderB, _s.Op2);

        var e = await OnlyAsync(a.Id, AppointmentEventTypes.ProviderReassigned);
        Assert.Equal(("Dr. Rivera", "Op 1"), (e.PreviousProviderName, e.PreviousOperatoryName));
        Assert.Equal(("Dr. Patel", "Op 2", null), (e.NewProviderName, e.NewOperatoryName, e.NewStartLocal));   // a reassignment never moves the time
        Assert.Equal(("Dr. Rivera", Why), (e.ActorName, e.Reason));
    }

    [Fact]
    public async Task A_provider_change_alone_records_no_room_destination()
    {
        var a = await BookAsync();
        await AssignAsync(a, _s.ProviderB, _s.Op1);
        var e = await OnlyAsync(a.Id, AppointmentEventTypes.ProviderReassigned);
        Assert.Equal(("Dr. Patel", null), (e.NewProviderName, e.NewOperatoryName));
    }

    [Fact]
    public async Task A_room_change_records_only_the_new_room()
    {
        var a = await BookAsync();
        await AssignAsync(a, _s.ProviderA, _s.Op2, reason: null);
        var e = await OnlyAsync(a.Id, AppointmentEventTypes.RoomChanged);
        Assert.Equal(("Op 1", "Op 2", null, null), (e.PreviousOperatoryName, e.NewOperatoryName, e.NewProviderName, e.NewStartLocal));
    }

    // ---------- no-op ----------

    [Fact]
    public async Task A_request_that_changes_nothing_writes_no_move_event()
    {
        var a = await BookAsync();
        await RescheduleAsync(a, At(9), _s.ProviderA, _s.Op1);     // the same place and time
        await AssignAsync(a, _s.ProviderA, _s.Op1);                // the same provider and room
        Assert.Equal(0, await MoveEventsAsync(a.Id));
    }

    // ---------- recorded, never derived ----------

    [Fact]
    public async Task A_later_move_and_a_later_rename_do_not_change_what_an_earlier_row_says()
    {
        var a = await BookAsync();
        a = await RescheduleAsync(a, At(11), _s.ProviderB, _s.Op2);    // first move: to 11:00, Dr. Patel, Op 2
        await RescheduleAsync(a, At(14), _s.ProviderA, _s.Op1);        // second move: back to Dr. Rivera, Op 1, 14:00
        await SqlAsync("UPDATE [Operatories] SET [Name] = N'Surgery 2' WHERE [Id] = {0}", _s.Op2);
        await SqlAsync("UPDATE s SET s.[DisplayName] = N'Dr. Patel-Ng' FROM [StaffProfiles] s JOIN [ProviderProfiles] p ON p.[StaffProfileId] = s.[Id] WHERE p.[Id] = {0}", _s.ProviderB);

        var moves = (await _s.HistoryAsync(a.Id)).Where(e => e.EventType == AppointmentEventTypes.Rescheduled).ToList();
        var first = Assert.Single(moves, e => e.NewStartLocal == "2030-01-14T11:00");
        Assert.Equal(("Dr. Patel", "Op 2"), (first.NewProviderName, first.NewOperatoryName));             // as recorded, not the current names or the current place
        var second = Assert.Single(moves, e => e.NewStartLocal == "2030-01-14T14:00");
        Assert.Equal(("Dr. Rivera", "Op 1"), (second.NewProviderName, second.NewOperatoryName));
    }

    [Fact]
    public async Task Two_moves_at_the_same_instant_each_keep_their_own_destination()
    {
        var frozen = new TestClock(new DateTimeOffset(2030, 1, 14, 7, 45, 0, TimeSpan.FromHours(-6)));
        var a = await BookAsync();
        a = await RescheduleAsync(a, At(11), _s.ProviderA, _s.Op1, frozen);
        await using (var db = _fixture.CreateContext())
        {
            var assignment = new VisitAssignmentService(db, frozen, new SchedulingConfiguration(db, frozen), PolicyTestSupport.Defaults(), null);
            await assignment.AssignAsync(a.Id, new AssignVisitRequest(_s.ProviderA, _s.Op2, a.RowVersion, null), _s.OfficeManager, default);
        }

        var history = await _s.HistoryAsync(a.Id);
        var moved = history.Single(e => e.EventType == AppointmentEventTypes.Rescheduled);
        var room = history.Single(e => e.EventType == AppointmentEventTypes.RoomChanged);
        Assert.Equal(moved.OccurredAtUtc, room.OccurredAtUtc);                                                // the same instant: order cannot tell them apart
        Assert.Equal(("2030-01-14T11:00", "Op 1"), (moved.NewStartLocal, moved.NewOperatoryName));          // each row still says where IT went
        Assert.Equal(("Op 1", "Op 2"), (room.PreviousOperatoryName, room.NewOperatoryName));
    }

    // ---------- events written before M4 ----------

    [Fact]
    public async Task An_event_written_before_M4_shows_where_it_came_from_and_no_destination()
    {
        var a = await BookAsync();
        await SqlAsync(@"INSERT INTO [AppointmentEvents] ([Id], [AppointmentId], [EventType], [ActorUserId], [OccurredAtUtc], [PreviousStartUtc], [PreviousProviderProfileId], [PreviousOperatoryId], [Detail])
                         VALUES (NEWID(), {0}, 'Rescheduled', NULL, SYSUTCDATETIME(), {1}, {2}, {3}, N'Rescheduled for 60 minutes.')",
            a.Id, a.StartUtc.AddHours(-1), _s.ProviderA, _s.Op1);

        var e = await OnlyAsync(a.Id, AppointmentEventTypes.Rescheduled);
        Assert.Equal(("Dr. Rivera", "Op 1"), (e.PreviousProviderName, e.PreviousOperatoryName));
        Assert.Equal((null, null, null, null), (e.NewStartLocal, e.NewProviderName, e.NewOperatoryName, e.ActorName));   // unknown stays unknown
    }
}
