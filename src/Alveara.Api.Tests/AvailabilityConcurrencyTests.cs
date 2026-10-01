using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>Test convenience: replace a schedule using whatever revision is current right now.</summary>
public static class AvailabilityTestExtensions
{
    public static async Task<ProviderAvailabilitySchedule> ReplaceCurrentAsync(
        this StaffProviderService service, Guid providerId, IReadOnlyList<AvailabilityWindow> windows, Guid actor)
    {
        var current = await service.GetAvailabilityScheduleAsync(providerId, default);
        return await service.ReplaceAvailabilityAsync(providerId, windows, current.Revision, actor, default);
    }
}

/// <summary>
/// ALV-N003 R02 (review finding ALV-N003-R01-02): the provider's weekly schedule is a versioned
/// aggregate. These are real SQL Server races - a SaveChanges barrier forces both replacements to
/// have finished reading before either commits, the exact interleaving that previously merged two
/// replacements into an overlapping union.
/// </summary>
public class AvailabilityConcurrencyTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private static readonly IPracticeClock Clock = new PracticeClock(TimeZoneInfo.FindSystemTimeZoneById("America/Chicago"));
    private readonly Guid _actor = Guid.NewGuid();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    /// <summary>Holds each participating SaveChanges until all of them have reached it, then releases them together.</summary>
    private sealed class SaveBarrierInterceptor(Barrier barrier) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await Task.Run(() => barrier.SignalAndWait(TimeSpan.FromSeconds(30)), cancellationToken);
            return result;
        }
    }

    private AlveraDbContext BarrierContext(Barrier barrier) =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(_fixture.ConnectionString).AddInterceptors(new SaveBarrierInterceptor(barrier)).Options);

    private async Task<Guid> SeedProviderAsync()
    {
        await using var db = _fixture.CreateContext();
        var service = new StaffProviderService(db, Clock);
        var staff = await service.CreateStaffAsync("Dr. Race", null, null, _actor, default);
        return (await service.CreateProviderAsync(staff.Id, "Dentistry", _actor, default)).Id;
    }

    private async Task<(List<ProviderWeeklyAvailability> Rows, int Revision, int AuditCount)> ReadStateAsync(Guid providerId)
    {
        await using var db = _fixture.CreateContext();
        var rows = await db.ProviderWeeklyAvailabilities.Where(a => a.ProviderProfileId == providerId).OrderBy(a => a.StartLocal).ToListAsync();
        var revision = (await db.ProviderProfiles.SingleAsync(p => p.Id == providerId)).AvailabilityRevision;
        var audits = await db.AuditLogEntries.CountAsync(a => a.TargetUserAccountId == providerId && a.EventType == ConfigurationAuditEvents.ProviderAvailabilityReplaced);
        return (rows, revision, audits);
    }

    private async Task RaceAsync(Guid providerId, int revisionBothRead, AvailabilityWindow first, AvailabilityWindow second)
    {
        using var barrier = new Barrier(2);
        await using var dbA = BarrierContext(barrier);
        await using var dbB = BarrierContext(barrier);
        var a = new StaffProviderService(dbA, Clock).ReplaceAvailabilityAsync(providerId, [first], revisionBothRead, _actor, default);
        var b = new StaffProviderService(dbB, Clock).ReplaceAvailabilityAsync(providerId, [second], revisionBothRead, _actor, default);

        var results = await Task.WhenAll(Settle(a), Settle(b));
        Assert.Equal(1, results.Count(r => r is null));                                        // exactly one winner
        Assert.Equal(1, results.Count(r => r is ConcurrencyConflictException));                // the other is rejected as a conflict, not merged
    }

    private static async Task<Exception?> Settle(Task task)
    {
        try { await task; return null; }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public async Task Two_concurrent_replacements_of_an_initially_empty_schedule_cannot_merge_into_an_overlapping_union()
    {
        var providerId = await SeedProviderAsync();
        var monday = new AvailabilityWindow(DayOfWeek.Monday, "09:00", "12:00");
        var overlapping = new AvailabilityWindow(DayOfWeek.Monday, "11:00", "14:00");

        await RaceAsync(providerId, 0, monday, overlapping);

        var (rows, revision, audits) = await ReadStateAsync(providerId);
        var only = Assert.Single(rows);                         // one caller's schedule, never both
        Assert.True(only.StartLocal == new TimeOnly(9, 0) || only.StartLocal == new TimeOnly(11, 0));
        Assert.Equal(1, revision);
        Assert.Equal(1, audits);                                // the loser's audit entry rolled back with its change
    }

    [Fact]
    public async Task Two_concurrent_replacements_of_a_populated_schedule_also_produce_exactly_one_valid_result()
    {
        var providerId = await SeedProviderAsync();
        await using (var db = _fixture.CreateContext())
            await new StaffProviderService(db, Clock).ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Friday, "08:00", "10:00")], _actor);

        await RaceAsync(providerId, 1, new AvailabilityWindow(DayOfWeek.Monday, "09:00", "12:00"), new AvailabilityWindow(DayOfWeek.Monday, "11:00", "14:00"));

        var (rows, revision, audits) = await ReadStateAsync(providerId);
        var only = Assert.Single(rows);                         // the old Friday window is gone and only one new window won
        Assert.Equal(DayOfWeek.Monday, only.DayOfWeek);
        Assert.Equal(2, revision);
        Assert.Equal(2, audits);
    }

    [Fact]
    public async Task A_stale_editor_is_rejected_and_neither_the_schedule_nor_the_audit_trail_changes()
    {
        var providerId = await SeedProviderAsync();
        int staleRevision;
        await using (var reader = _fixture.CreateContext())
            staleRevision = (await new StaffProviderService(reader, Clock).GetAvailabilityScheduleAsync(providerId, default)).Revision;

        await using (var other = _fixture.CreateContext())
            await new StaffProviderService(other, Clock).ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Tuesday, "09:00", "17:00")], _actor);
        var before = await ReadStateAsync(providerId);

        await using var stale = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            new StaffProviderService(stale, Clock).ReplaceAvailabilityAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "12:00")], staleRevision, _actor, default));
        Assert.Equal("ProviderAvailability", ex.EntityType);

        var after = await ReadStateAsync(providerId);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.AuditCount, after.AuditCount);
        Assert.Equal(DayOfWeek.Tuesday, Assert.Single(after.Rows).DayOfWeek); // the winner's schedule is intact
    }

    [Fact]
    public async Task A_replacement_without_the_revision_it_read_is_refused()
    {
        var providerId = await SeedProviderAsync();
        await using var db = _fixture.CreateContext();
        var ex = await Assert.ThrowsAsync<ConfigurationException>(() =>
            new StaffProviderService(db, Clock).ReplaceAvailabilityAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "12:00")], null, _actor, default));
        Assert.Equal("revision_required", ex.Code);
    }

    [Fact]
    public async Task Each_successful_replacement_advances_the_revision_and_editing_the_specialty_does_not_disturb_it()
    {
        var providerId = await SeedProviderAsync();
        await using var db = _fixture.CreateContext();
        var service = new StaffProviderService(db, Clock);

        var first = await service.ReplaceCurrentAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "12:00")], _actor);
        Assert.Equal(1, first.Revision);

        var provider = await service.GetProviderAsync(providerId, default);
        await service.UpdateProviderAsync(providerId, "Endodontics", Convert.ToBase64String(provider.RowVersion), _actor, default);
        Assert.Equal(1, (await service.GetAvailabilityScheduleAsync(providerId, default)).Revision);

        var second = await service.ReplaceAvailabilityAsync(providerId, [new AvailabilityWindow(DayOfWeek.Monday, "08:00", "13:00")], first.Revision, _actor, default);
        Assert.Equal(2, second.Revision);
    }
}
