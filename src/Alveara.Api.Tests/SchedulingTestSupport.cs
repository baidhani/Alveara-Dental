using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Scheduling;
using Alveara.Api.Architecture.Time;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// Shared arrangement for STORY-004's tests: a practice location with two operatories, two providers who work Monday-Friday 08:00-17:00 practice
/// time, a 60-minute and a 30-minute appointment type, and patients - all created through the real services against real SQL Server.
/// The test day is a Monday far in the future (2030-01-14, CST = UTC-6), so "start must be in the future" never depends on today's date.
/// </summary>
public sealed class SchedulingTestSupport(TestDatabaseFixture fixture)
{
    public static readonly IPracticeClock Clock = PatientTestSupport.Clock;
    public static readonly DateTime Monday = new(2030, 1, 14);
    public readonly Guid Actor = Guid.NewGuid();
    private readonly PatientTestSupport _patients = new(fixture);
    private int _n;

    public Guid LocationId;
    public Guid Op1, Op2;
    public Guid ProviderA, ProviderB;
    public Guid Long60, Short30;

    public static DateTime At(int hour, int minute = 0, int dayOffset = 0) => Monday.AddDays(dayOffset).AddHours(hour).AddMinutes(minute);

    public async Task ArrangeAsync()
    {
        await using var db = fixture.CreateContext();
        var practice = new PracticeConfigurationService(db, Clock);
        var people = new StaffProviderService(db, Clock);
        LocationId = (await practice.CreateLocationAsync("Main Office", Actor, default)).Id;
        Op1 = (await practice.CreateOperatoryAsync("Op 1", Actor, default)).Id;
        Op2 = (await practice.CreateOperatoryAsync("Op 2", Actor, default)).Id;
        Long60 = (await practice.CreateAppointmentTypeAsync("Exam", 60, Actor, default)).Id;
        Short30 = (await practice.CreateAppointmentTypeAsync("Quick check", 30, Actor, default)).Id;
        ProviderA = await NewProviderAsync(people, "Dr. Rivera");
        ProviderB = await NewProviderAsync(people, "Dr. Patel");
    }

    private async Task<Guid> NewProviderAsync(StaffProviderService people, string name)
    {
        var staff = await people.CreateStaffAsync(name, "Dentist", null, Actor, default);
        var provider = await people.CreateProviderAsync(staff.Id, "General dentistry", Actor, default);
        await people.ReplaceCurrentAsync(provider.Id,
            [.. new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }.Select(d => new AvailabilityWindow(d, "08:00", "17:00"))], Actor);
        return provider.Id;
    }

    public AppointmentScheduler Scheduler(AlveraDbContext db, IMeasurementEventSink? measurements = null) =>
        new(db, Clock, new SchedulingConfiguration(db, Clock), measurements);

    public Task<Guid> PatientAsync(string first = "Ann", string last = "Lee") =>
        _patients.RegisterAsync(first, last, $"19{80 + _n++ % 19}-0{1 + _n % 9}-1{_n % 9}").ContinueWith(t => t.Result.Id);

    public ScheduleAppointmentRequest Request(Guid patient, DateTime startLocal, Guid? provider = null, Guid? operatory = null, Guid? type = null, int? duration = null) =>
        new(patient, provider ?? ProviderA, operatory ?? Op1, type ?? Long60, startLocal, duration);

    public async Task<ScheduleResult> ScheduleAsync(ScheduleAppointmentRequest request, string? key = null)
    {
        await using var db = fixture.CreateContext();
        return await Scheduler(db).ScheduleAsync(request, key ?? $"sched-{Interlocked.Increment(ref _n)}-{Guid.NewGuid():N}", Actor, default);
    }

    public async Task<int> CountAsync() => await CountAsync(db => db.Appointments);

    public async Task<int> CountAsync(Func<AlveraDbContext, IQueryable<object>> query)
    {
        await using var db = fixture.CreateContext();
        return await query(db).CountAsync();
    }

    public async Task BlockProviderAsync(Guid provider, DateTime startLocal, DateTime endLocal)
    {
        await using var db = fixture.CreateContext();
        await new StaffProviderService(db, Clock).AddBlockedTimeAsync(provider, startLocal, endLocal, "Meeting", Actor, default);
    }

    /// <summary>A context whose saves fail whenever an audit entry is part of them, to simulate audit storage being down.</summary>
    public AlveraDbContext FailingAuditContext() =>
        new(new DbContextOptionsBuilder<AlveraDbContext>().UseSqlServer(fixture.ConnectionString).AddInterceptors(new FailAuditWrites()).Options);

    private sealed class FailAuditWrites : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<AuditLogEntry>().Any(e => e.State == EntityState.Added))
                throw new DbUpdateException("simulated audit storage failure");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
