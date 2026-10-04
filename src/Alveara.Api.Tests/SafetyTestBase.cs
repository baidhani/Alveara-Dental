using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Safety;
using Alveara.Api.Data;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N011 test arrangement shared by the safety service tests: a real SQL Server database, a patient, two signed-in-style actors with staff profiles (so attribution shows names),
/// and small helpers to run each service in its own context (as a request would) and to read the audit log.
/// </summary>
public abstract class SafetyTestBase : IAsyncLifetime
{
    protected readonly TestDatabaseFixture Fixture = new();
    protected SchedulingTestSupport S = null!;
    protected Guid Ann;
    protected Guid Bo;
    protected readonly Guid Other = Guid.NewGuid();

    public virtual async Task InitializeAsync()
    {
        await Fixture.InitializeAsync();
        S = new SchedulingTestSupport(Fixture);
        await S.ArrangeAsync();
        Ann = await S.PatientAsync();
        Bo = await S.PatientAsync("Bo", "Kim");
        await using var db = Fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = S.Actor, Username = $"dr-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        db.UserAccounts.Add(new UserAccount { Id = Other, Username = $"hyg-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        var people = new StaffProviderService(db, Clock);
        await people.CreateStaffAsync("Dr. Okafor", null, S.Actor, S.Actor, default);
        await people.CreateStaffAsync("Hana Hygienist", null, Other, S.Actor, default);
    }

    public Task DisposeAsync() => Fixture.DisposeAsync();

    protected async Task<T> WithDb<T>(Func<AlveraDbContext, Task<T>> action)
    {
        await using var db = Fixture.CreateContext();
        return await action(db);
    }

    protected Task<SafetyContext> AlertsAsync(Func<SafetyAlertService, Task<SafetyContext>> action) => WithDb(db => action(new SafetyAlertService(db, Clock)));
    protected Task<SafetyContext> ClearancesAsync(Func<ClearanceService, Task<SafetyContext>> action) => WithDb(db => action(new ClearanceService(db, Clock)));
    protected Task<SafetyContext> ContextAsync(Guid? patient = null, Guid? user = null) => WithDb(db => new SafetyContextService(db, Clock).GetAsync(patient ?? Ann, user ?? S.Actor, default));

    protected Task<SafetyContext> AddAlertAsync(string title = "Prosthetic heart valve", string category = SafetyCategories.Condition, string severity = SafetySeverities.High, string? detail = null,
        string source = "Reported by the patient at intake", Guid? sourceItem = null, Guid? patient = null, Guid? actor = null) =>
        AlertsAsync(s => s.CreateAsync(patient ?? Ann, category, title, detail, severity, source, sourceItem, actor ?? S.Actor, default));

    protected static SafetyEntry AlertNamed(SafetyContext c, string title) => c.Entries.Concat(c.Resolved).Single(e => e.Origin == SafetyOrigins.Alert && e.Title == title);
    protected static ClearanceView ClearanceOf(SafetyContext c, string kind = "Medical") => c.Clearances.First(x => x.Kind == kind);
    protected static async Task<SafetyException> RefusedAsync<T>(Func<Task<T>> action) => await Assert.ThrowsAsync<SafetyException>(action);

    protected async Task<List<(string Type, string Details, Guid? By)>> AuditAsync(string entityType)
    {
        await using var db = Fixture.CreateContext();
        return (await db.AuditLogEntries.AsNoTracking().Where(a => a.EntityType == entityType).OrderBy(a => a.TimestampUtc).ToListAsync()).Select(a => (a.EventType, a.Details, a.PerformedByUserAccountId)).ToList();
    }

    /// <summary>A record item for a patient (allergy / medication / history), added through the real clinical-record service.</summary>
    protected async Task<ClinicalRecordItem> RecordItemAsync(string kind, string name, string? reaction = null, string? severity = null, string? dose = null, string? frequency = null, Guid? patient = null)
    {
        await WithDb(db => new ClinicalRecordService(db, Clock).AddItemAsync(patient ?? Ann, kind, new EntryFields(name, null, reaction, severity, dose, frequency), null, null, S.Actor, default));
        await using var read = Fixture.CreateContext();
        return await read.ClinicalRecordItems.AsNoTracking().SingleAsync(i => i.PatientId == (patient ?? Ann) && i.Kind == kind && i.Name == name);
    }

    protected Task RecordReviewAsync(string section, string state, Guid? patient = null) =>
        WithDb(db => new ClinicalRecordService(db, Clock).SetReviewAsync(patient ?? Ann, section, state, null, S.Actor, default));
}
