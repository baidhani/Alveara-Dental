using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Time;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N003's scheduling-consumer integration smoke test: configuration written through the
/// configuration services is immediately visible to the read model the scheduling story builds on,
/// and inactive configuration is never offered.
/// </summary>
public class SchedulingConfigurationTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    private readonly Guid _actor = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string Version(byte[] v) => Convert.ToBase64String(v);

    [Fact]
    public async Task Configured_provider_operatory_and_appointment_type_are_immediately_consumable_by_scheduling()
    {
        await using var db = _fixture.CreateContext();
        var practice = new PracticeConfigurationService(db, Clock);
        var people = new StaffProviderService(db, Clock);
        var scheduling = new SchedulingConfiguration(db, Clock);

        var empty = await scheduling.GetSnapshotAsync(default);
        Assert.Empty(empty.Providers);
        Assert.Empty(empty.Operatories);
        Assert.Empty(empty.AppointmentTypes);
        Assert.Null(empty.ActiveLocationId);

        var location = await practice.CreateLocationAsync("Main Office", _actor, default);
        var operatory = await practice.CreateOperatoryAsync("Op 1", _actor, default);
        var type = await practice.CreateAppointmentTypeAsync("New patient exam", 60, _actor, default);
        var staff = await people.CreateStaffAsync("Dr. Rivera", "Dentist", null, _actor, default);
        var provider = await people.CreateProviderAsync(staff.Id, "General dentistry", _actor, default);
        await people.ReplaceAvailabilityAsync(provider.Id, [new AvailabilityWindow(DayOfWeek.Wednesday, "08:00", "16:00")], _actor, default);

        var snapshot = await scheduling.GetSnapshotAsync(default);
        Assert.Equal("America/Chicago", snapshot.TimeZoneId);
        Assert.Equal(location.Id, snapshot.ActiveLocationId);
        Assert.Equal(operatory.Id, Assert.Single(snapshot.Operatories).Id);
        var offeredType = Assert.Single(snapshot.AppointmentTypes);
        Assert.Equal((type.Id, 60), (offeredType.Id, offeredType.DefaultDurationMinutes));
        var offeredProvider = Assert.Single(snapshot.Providers);
        Assert.Equal("Dr. Rivera", offeredProvider.DisplayName);
        var window = Assert.Single(offeredProvider.WeeklyAvailability);
        Assert.Equal((DayOfWeek.Wednesday, "08:00", "16:00"), (window.DayOfWeek, window.StartLocal, window.EndLocal));
    }

    [Fact]
    public async Task Inactive_configuration_is_no_longer_offered_to_scheduling_but_remains_resolvable()
    {
        await using var db = _fixture.CreateContext();
        var practice = new PracticeConfigurationService(db, Clock);
        var people = new StaffProviderService(db, Clock);
        var scheduling = new SchedulingConfiguration(db, Clock);

        await practice.CreateLocationAsync("Main Office", _actor, default);
        var operatory = await practice.CreateOperatoryAsync("Op 1", _actor, default);
        var type = await practice.CreateAppointmentTypeAsync("Cleaning", 45, _actor, default);
        var staff = await people.CreateStaffAsync("Hyg. Chen", null, null, _actor, default);
        var provider = await people.CreateProviderAsync(staff.Id, "Hygiene", _actor, default);

        await practice.SetOperatoryActiveAsync(operatory.Id, false, Version(operatory.RowVersion), _actor, default);
        await practice.SetAppointmentTypeActiveAsync(type.Id, false, Version(type.RowVersion), _actor, default);
        await people.SetProviderActiveAsync(provider.Id, false, Version(provider.RowVersion), _actor, default);

        var snapshot = await scheduling.GetSnapshotAsync(default);
        Assert.Empty(snapshot.Operatories);
        Assert.Empty(snapshot.AppointmentTypes);
        Assert.Empty(snapshot.Providers);

        // A past appointment referencing them can still resolve every one by id.
        Assert.Equal("Op 1", (await practice.GetOperatoryAsync(operatory.Id, default)).Name);
        Assert.Equal(45, (await practice.GetAppointmentTypeAsync(type.Id, default)).DefaultDurationMinutes);
        Assert.Equal("Hyg. Chen", (await people.GetProviderAsync(provider.Id, default)).StaffProfile!.DisplayName);

        var check = await scheduling.CheckProviderAvailabilityAsync(provider.Id, DateTimeOffset.Parse("2026-01-13T16:00:00Z"), 30, default);
        Assert.False(check.Available);
        Assert.Equal("provider_inactive", check.Reason);
    }

    [Fact]
    public async Task Availability_check_honors_working_hours_blocked_time_and_slot_boundaries()
    {
        await using var db = _fixture.CreateContext();
        var people = new StaffProviderService(db, Clock);
        var scheduling = new SchedulingConfiguration(db, Clock);

        var staff = await people.CreateStaffAsync("Dr. Rivera", null, null, _actor, default);
        var provider = await people.CreateProviderAsync(staff.Id, "Dentistry", _actor, default);
        await people.ReplaceAvailabilityAsync(provider.Id, [new AvailabilityWindow(DayOfWeek.Tuesday, "09:00", "17:00")], _actor, default);
        // Tuesday 2026-01-13, block 12:00-13:00 local (CST) = 18:00Z-19:00Z.
        await people.AddBlockedTimeAsync(provider.Id, new DateTime(2026, 1, 13, 12, 0, 0), new DateTime(2026, 1, 13, 13, 0, 0), "Lunch", _actor, default);

        async Task<AvailabilityCheck> At(string utc, int minutes) =>
            await scheduling.CheckProviderAvailabilityAsync(provider.Id, DateTimeOffset.Parse(utc), minutes, default);

        Assert.True((await At("2026-01-13T15:00:00Z", 60)).Available);                        // 09:00-10:00 local, inside hours
        Assert.True((await At("2026-01-13T22:00:00Z", 60)).Available);                        // 16:00-17:00 local, ends exactly at close
        Assert.Equal("outside_working_hours", (await At("2026-01-13T22:30:00Z", 60)).Reason);  // 16:30-17:30 local, runs past close
        Assert.Equal("outside_working_hours", (await At("2026-01-14T16:00:00Z", 30)).Reason);  // Wednesday: no window
        Assert.Equal("blocked_time", (await At("2026-01-13T18:30:00Z", 30)).Reason);           // inside the lunch block
        Assert.Equal("blocked_time", (await At("2026-01-13T17:45:00Z", 30)).Reason);           // overlaps the block's start
        Assert.True((await At("2026-01-13T19:00:00Z", 30)).Available);                         // starts exactly when the block ends
        await Assert.ThrowsAsync<ConfigurationException>(() => At("2026-01-13T15:00:00Z", 7)); // invalid duration is rejected, not guessed
    }
}
