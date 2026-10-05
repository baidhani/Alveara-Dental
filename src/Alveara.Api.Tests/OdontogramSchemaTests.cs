using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-006 persistence, against real SQL Server: the database itself keeps the tooth chart honest, independently of any application code. It accepts every one of the 52 keys and
/// each condition with the right surface; refuses a tooth that is not an FDI key (case, spacing, Universal numbers), a surface that does not exist on that kind of tooth, a surface
/// on a whole-tooth condition and a missing surface on a surface condition; refuses a state, status or condition outside the model; allows one active finding per tooth, surface
/// and condition; refuses a withdrawal without who, when and why; never deletes a finding; and keeps the history append-only.
/// </summary>
public class OdontogramSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    /// <summary>The scope the seeded catalogue gives a condition (a finding stores it so the database can check a surface against it).</summary>
    private static string ScopeOf(string condition) => FindingConditions.Seeds.SingleOrDefault(c => c.Code == condition)?.Scope ?? ConditionScopes.Surface;

    private ToothFinding New(string tooth = "16", string? surface = "O", string condition = "Caries", string state = "Diagnosed", string status = "Active") => new()
    {
        Id = Guid.NewGuid(), PatientId = _ann, ToothKey = tooth, Surface = surface, Condition = condition, ConditionScope = ScopeOf(condition), State = state, Status = status, CreatedAtUtc = At,
    };

    private async Task<Guid> AddAsync(Action<ToothFinding>? tweak = null, Guid? patient = null)
    {
        await using var db = _fixture.CreateContext();
        var f = New();
        if (patient is not null) f.PatientId = patient.Value;
        var (conditionBefore, scopeBefore) = (f.Condition, f.ConditionScope);
        tweak?.Invoke(f);
        if (f.Condition != conditionBefore && f.ConditionScope == scopeBefore) f.ConditionScope = ScopeOf(f.Condition);   // a test that changes the condition means its scope too, unless it sets the scope itself
        db.ToothFindings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    [Fact]
    public async Task The_database_accepts_all_52_teeth_each_condition_and_every_state()
    {
        foreach (var key in ToothKeys.All) await AddAsync(f => { f.ToothKey = key; f.Surface = null; f.Condition = "Crown"; });
        Assert.Equal(52, await _s.CountAsync(db => db.ToothFindings));

        var surfaces = new[] { "M", "O", "D", "B" };
        for (var i = 0; i < FindingStates.All.Count; i++)
            await AddAsync(f => { f.ToothKey = "26"; f.Surface = surfaces[i]; f.State = FindingStates.All[i]; });
        var other = await _s.PatientAsync();
        foreach (var condition in FindingConditions.Seeds.Where(c => c.Scope == ConditionScopes.WholeTooth).Select(c => c.Code))
            await AddAsync(f => { f.ToothKey = "36"; f.Surface = null; f.Condition = condition; }, other);
    }

    [Theory]
    [InlineData("19")] [InlineData("10")] [InlineData("49")] [InlineData("56")] [InlineData("86")] [InlineData("90")]
    [InlineData("1")] [InlineData(" 1")] [InlineData("1A")] [InlineData("UR")]
    public async Task The_database_refuses_a_tooth_that_is_not_one_of_the_52_fdi_keys(string key)
    {
        var ex = await RefusedAsync(() => AddAsync(f => { f.ToothKey = key; f.Surface = null; f.Condition = "Crown"; }));
        Assert.Equal(547, ex.Number);
        Assert.Equal(0, await _s.CountAsync(db => db.ToothFindings));
    }

    [Theory]
    [InlineData("O", "11")]     // occlusal does not exist on an incisor
    [InlineData("B", "21")]     // buccal does not exist on an anterior tooth
    [InlineData("I", "16")]     // incisal does not exist on a molar
    [InlineData("F", "36")]     // facial does not exist on a molar
    [InlineData("O", "53")]     // primary canine is anterior
    [InlineData("I", "55")]     // primary molar is posterior
    [InlineData("m", "16")]     // case-sensitive
    [InlineData("X", "16")]
    public async Task The_database_refuses_a_surface_that_does_not_exist_on_that_kind_of_tooth(string surface, string tooth)
    {
        var ex = await RefusedAsync(() => AddAsync(f => { f.ToothKey = tooth; f.Surface = surface; }));
        Assert.Equal(547, ex.Number);
    }

    [Theory]
    [InlineData("M", "16")] [InlineData("O", "16")] [InlineData("D", "16")] [InlineData("B", "16")] [InlineData("L", "16")]
    [InlineData("M", "11")] [InlineData("I", "11")] [InlineData("D", "11")] [InlineData("F", "11")] [InlineData("L", "11")]
    [InlineData("O", "54")] [InlineData("I", "52")]
    public async Task The_database_accepts_the_surfaces_that_do_exist(string surface, string tooth)
    {
        await AddAsync(f => { f.ToothKey = tooth; f.Surface = surface; });
        Assert.Equal(1, await _s.CountAsync(db => db.ToothFindings));
    }

    [Fact]
    public async Task A_surface_condition_needs_a_surface_and_a_whole_tooth_condition_must_not_have_one()
    {
        Assert.Equal(547, (await RefusedAsync(() => AddAsync(f => { f.Condition = "Caries"; f.Surface = null; }))).Number);
        Assert.Equal(547, (await RefusedAsync(() => AddAsync(f => { f.Condition = "Restoration"; f.Surface = null; }))).Number);
        foreach (var whole in new[] { "Crown", "Missing", "Implant", "RootCanal" })
            Assert.Equal(547, (await RefusedAsync(() => AddAsync(f => { f.Condition = whole; f.Surface = "O"; }))).Number);
    }

    [Theory]
    [InlineData("Cavity", "Diagnosed", "Active")]       // not one of the six
    [InlineData("caries", "Diagnosed", "Active")]       // case-sensitive: the default collation would accept it
    [InlineData("Caries", "diagnosed", "Active")]
    [InlineData("Caries", "Proposed", "Active")]
    [InlineData("Caries", "Diagnosed", "active")]
    [InlineData("Caries", "Diagnosed", "Deleted")]
    public async Task The_database_refuses_a_condition_state_or_status_outside_the_model(string condition, string state, string status)
    {
        var ex = await RefusedAsync(() => AddAsync(f => { f.Condition = condition; f.State = state; f.Status = status; }));
        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task One_active_finding_per_tooth_surface_and_condition_but_a_withdrawn_one_can_be_entered_again()
    {
        await AddAsync();
        Assert.Equal(2601, (await RefusedAsync(() => AddAsync())).Number);
        await AddAsync(f => f.Surface = "M");                                             // another surface
        await AddAsync(f => { f.Surface = "O"; f.Condition = "Restoration"; });          // another condition
        await AddAsync(f => f.ToothKey = "17");                                           // another tooth
        await AddAsync(patient: await _s.PatientAsync());                                 // another patient
        await AddAsync(f =>
        {
            f.Surface = "B"; f.Status = "Withdrawn"; f.WithdrawnAtUtc = At; f.WithdrawnByUserId = Guid.NewGuid(); f.WithdrawnReason = "Wrong tooth";
        });
        await AddAsync(f => f.Surface = "B");                                             // the withdrawn twin does not block a correct entry
        Assert.Equal(7, await _s.CountAsync(db => db.ToothFindings));
    }

    [Theory]
    [InlineData(false, true, true, "reason")]
    [InlineData(true, false, true, "reason")]
    [InlineData(true, true, false, null)]
    [InlineData(true, true, true, "   ")]
    public async Task A_withdrawn_finding_needs_who_when_and_a_real_reason_in_the_database_too(bool when, bool who, bool hasReason, string? reason)
    {
        var ex = await RefusedAsync(() => AddAsync(f =>
        {
            f.Status = "Withdrawn"; f.WithdrawnAtUtc = when ? At : null; f.WithdrawnByUserId = who ? Guid.NewGuid() : null; f.WithdrawnReason = hasReason ? reason : null;
        }));
        Assert.Equal(547, ex.Number);
    }

    [Fact]
    public async Task An_active_finding_cannot_carry_withdrawal_fields()
        => Assert.Equal(547, (await RefusedAsync(() => AddAsync(f => f.WithdrawnReason = "Leftover"))).Number);

    [Fact]
    public async Task A_finding_must_belong_to_a_patient_who_exists()
        => Assert.Equal(547, (await RefusedAsync(() => AddAsync(patient: Guid.NewGuid()))).Number);

    [Fact]
    public async Task A_finding_is_never_deleted()
    {
        var id = await AddAsync();
        await using var db = _fixture.CreateContext();
        db.ToothFindings.Remove(await db.ToothFindings.SingleAsync(x => x.Id == id));
        Assert.Equal(51055, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        Assert.Equal(1, await _s.CountAsync(d => d.ToothFindings));
    }

    [Fact]
    public async Task A_stale_edit_of_a_finding_is_refused_by_its_row_version()
    {
        var id = await AddAsync();
        await using var first = _fixture.CreateContext();
        await using var second = _fixture.CreateContext();
        var a = await first.ToothFindings.SingleAsync(x => x.Id == id);
        var b = await second.ToothFindings.SingleAsync(x => x.Id == id);
        a.State = "Planned";
        await first.SaveChangesAsync();
        b.State = "Completed";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    private static ToothFindingVersion Version(Guid finding, Guid patient, int number, string change = "Recorded") => new()
    {
        Id = Guid.NewGuid(), FindingId = finding, PatientId = patient, VersionNumber = number, ChangeType = change, ToothKey = "16", Surface = "O", Condition = "Caries",
        State = "Diagnosed", Status = "Active", OccurredAtUtc = At,
    };

    [Fact]
    public async Task Finding_history_is_append_only_unique_per_version_and_limited_to_the_known_change_types()
    {
        var id = await AddAsync();
        Guid version;
        await using (var db = _fixture.CreateContext())
        {
            var v = Version(id, _ann, 1);
            db.ToothFindingVersions.Add(v);
            await db.SaveChangesAsync();
            version = v.Id;
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ToothFindingVersions.Add(Version(id, _ann, 1, "StateChanged"));
            Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ToothFindingVersions.Add(Version(id, _ann, 2, "Deleted"));
            Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            (await db.ToothFindingVersions.SingleAsync(v => v.Id == version)).State = "Completed";
            Assert.Equal(51056, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            db.ToothFindingVersions.Remove(await db.ToothFindingVersions.SingleAsync(v => v.Id == version));
            Assert.Equal(51056, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        Assert.Equal(1, await _s.CountAsync(d => d.ToothFindingVersions));
    }
}
