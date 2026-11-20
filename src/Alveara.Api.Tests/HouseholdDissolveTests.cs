using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 (F-202, Q-5bd; owner decision OQ-5), against real SQL Server: a household left with one member is dissolved - that member's membership and relationship
/// are cleared with their own History rows and an audit row, and the household is removed; a household that still has two or more members is not touched.
/// </summary>
public class HouseholdDissolveTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private PatientTestSupport _s = null!;

    public Task InitializeAsync() => _fixture.InitializeOwnerAsync(() => { _s = new PatientTestSupport(_fixture); return Task.CompletedTask; });
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Patient> ReloadAsync(Guid id) { await using var db = _fixture.CreateContext(); return await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id); }

    private async Task JoinAsync(Guid member, Guid anchor, string relationship)
    {
        var p = await ReloadAsync(member);
        await using var db = _fixture.CreateContext();
        await _s.Relationships(db).SetHouseholdAsync(member, anchor, relationship, Convert.ToBase64String(p.RowVersion), _s.Actor, default);
    }

    private async Task LeaveAsync(Guid member)
    {
        var p = await ReloadAsync(member);
        await using var db = _fixture.CreateContext();
        await _s.Relationships(db).SetHouseholdAsync(member, null, null, Convert.ToBase64String(p.RowVersion), _s.Actor, default);
    }

    [Fact]
    public async Task When_the_second_to_last_member_leaves_the_household_is_dissolved_with_history_and_an_audit_row()
    {
        var mom = await _s.RegisterAsync("Mia", "Hale", "1980-01-01", "(555) 600-0001");
        var kid = await _s.RegisterAsync("Kit", "Hale", "2012-01-01", "(555) 600-0002");
        await JoinAsync(kid.Id, mom.Id, "Child");
        var household = (await ReloadAsync(mom.Id)).HouseholdId!.Value;

        await LeaveAsync(kid.Id);

        var left = await ReloadAsync(mom.Id);
        Assert.Equal(((Guid?)null, (string?)null), (left.HouseholdId, left.HouseholdRelationship));
        await using var db = _fixture.CreateContext();
        Assert.False(await db.Households.AnyAsync(h => h.Id == household));
        var history = await db.PatientHistory.AsNoTracking().Where(h => h.PatientId == mom.Id && h.ChangeType == "HouseholdChanged").ToListAsync();
        Assert.Contains(history, h => h.FieldName == "householdId" && h.OldValue == household.ToString() && h.NewValue == null);
        var audit = await db.AuditLogEntries.AsNoTracking().Where(a => a.TargetUserAccountId == mom.Id && a.Details.Contains("dissolved")).ToListAsync();
        Assert.Single(audit);
        Assert.Contains(kid.Id.ToString(), audit[0].Details);
        Assert.DoesNotContain("Hale", audit[0].Details);                                                     // PHI-free
    }

    [Fact]
    public async Task A_household_that_keeps_two_members_is_not_dissolved()
    {
        var mom = await _s.RegisterAsync("Mia", "Hale", "1980-01-01", "(555) 600-0001");
        var kid = await _s.RegisterAsync("Kit", "Hale", "2012-01-01", "(555) 600-0002");
        var dad = await _s.RegisterAsync("Dan", "Hale", "1979-01-01", "(555) 600-0003");
        await JoinAsync(kid.Id, mom.Id, "Child");
        await JoinAsync(dad.Id, mom.Id, "Spouse");
        var household = (await ReloadAsync(mom.Id)).HouseholdId;

        await LeaveAsync(kid.Id);

        Assert.Equal(household, (await ReloadAsync(mom.Id)).HouseholdId);
        Assert.Equal(household, (await ReloadAsync(dad.Id)).HouseholdId);
    }
}
