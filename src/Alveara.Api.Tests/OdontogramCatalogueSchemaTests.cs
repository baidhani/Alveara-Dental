using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-006-C01 persistence, against real SQL Server: the condition catalogue is seeded with the six conditions the course odontogram shipped with; the database refuses a condition outside
/// the allowed scopes, dentitions and effects, a malformed or duplicate code, deleting a condition, and any change to what a condition MEANS once it exists (while retiring it is allowed);
/// a finding can only name a condition that exists, spelled exactly; a finding's surface must match its stored scope; the condition and link histories are append-only; and a database
/// that already holds findings upgrades with each finding's scope filled in and every finding kept.
/// </summary>
public class OdontogramCatalogueSchemaTests : IAsyncLifetime
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

    private static ConditionType NewType(string code = "Fracture", string scope = "Surface", string appliesTo = "Both", string effect = "None", string label = "Fracture") =>
        new() { Id = Guid.NewGuid(), Code = code, Label = label, Scope = scope, AppliesTo = appliesTo, ToothEffect = effect, IsActive = true, CreatedAtUtc = At };

    private async Task<Guid> AddTypeAsync(Action<ConditionType>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var t = NewType();
        tweak?.Invoke(t);
        db.ConditionTypes.Add(t);
        await db.SaveChangesAsync();
        return t.Id;
    }

    private async Task<Guid> AddFindingAsync(Action<ToothFinding>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var f = new ToothFinding { Id = Guid.NewGuid(), PatientId = _ann, ToothKey = "16", Surface = "O", Condition = "Caries", ConditionScope = "Surface", State = "Diagnosed", Status = "Active", CreatedAtUtc = At };
        tweak?.Invoke(f);
        db.ToothFindings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    // ---------- the seeded catalogue ----------

    [Fact]
    public async Task A_new_database_has_exactly_the_six_course_conditions_with_the_scope_dentition_and_effect_they_always_had()
    {
        await using var db = _fixture.CreateContext();
        var rows = await db.ConditionTypes.AsNoTracking().OrderBy(c => c.Code).ToListAsync();
        Assert.Equal(new[] { "Caries", "Crown", "Implant", "Missing", "Restoration", "RootCanal" }, rows.Select(r => r.Code));
        Assert.All(rows, r => Assert.True(r.IsActive));
        Assert.Equal(new[] { ("Caries", "Surface"), ("Restoration", "Surface") }, rows.Where(r => r.Scope == "Surface").Select(r => (r.Code, r.Scope)).OrderBy(x => x.Code));
        Assert.Equal("Permanent", rows.Single(r => r.Code == "Implant").AppliesTo);                 // implants are not placed in primary teeth
        Assert.All(rows.Where(r => r.Code != "Implant"), r => Assert.Equal("Both", r.AppliesTo));
        Assert.Equal("Absent", rows.Single(r => r.Code == "Missing").ToothEffect);
        Assert.Equal("Replacement", rows.Single(r => r.Code == "Implant").ToothEffect);
        Assert.Equal(FindingConditions.Seeds.Select(s => s.Id).Order(), rows.Select(r => r.Id).Order());   // the same ids in every database
    }

    // ---------- the catalogue's own rules ----------

    [Fact]
    public async Task The_database_accepts_a_new_condition_of_each_scope_dentition_and_effect()
    {
        foreach (var scope in ConditionScopes.All) await AddTypeAsync(t => { t.Code = "S" + scope; t.Scope = scope; });
        foreach (var dentition in ConditionDentitions.All) await AddTypeAsync(t => { t.Code = "D" + dentition; t.AppliesTo = dentition; });
        foreach (var effect in ToothEffects.All) await AddTypeAsync(t => { t.Code = "E" + effect; t.ToothEffect = effect; });
        Assert.Equal(6 + 2 + 3 + 3, await _s.CountAsync(db => db.ConditionTypes));
    }

    [Theory]
    [InlineData("scope", "surface")] [InlineData("scope", "Tooth")] [InlineData("applies", "permanent")] [InlineData("applies", "Adult")] [InlineData("effect", "none")] [InlineData("effect", "Gone")]
    public async Task The_database_refuses_a_scope_dentition_or_effect_outside_the_model_case_sensitively(string field, string value)
    {
        var ex = await RefusedAsync(() => AddTypeAsync(t => { if (field == "scope") t.Scope = value; else if (field == "applies") t.AppliesTo = value; else t.ToothEffect = value; }));
        Assert.Equal(547, ex.Number);
    }

    [Theory]
    [InlineData("F")] [InlineData("fracture")] [InlineData("1Fracture")] [InlineData("Frac ture")] [InlineData("Frac-ture")] [InlineData("Frac_ture")] [InlineData("")]
    public async Task The_database_refuses_a_code_that_is_not_letters_and_digits_starting_with_a_capital(string code)
        => Assert.Equal(547, (await RefusedAsync(() => AddTypeAsync(t => t.Code = code))).Number);

    [Fact]
    public async Task The_database_refuses_a_blank_label_a_code_over_32_characters_and_a_duplicate_code_even_in_another_case()
    {
        Assert.Equal(547, (await RefusedAsync(() => AddTypeAsync(t => t.Label = "   "))).Number);
        await Assert.ThrowsAnyAsync<Exception>(() => AddTypeAsync(t => t.Code = "A" + new string('b', 32)));
        await AddTypeAsync(t => t.Code = "Fracture");
        Assert.Equal(2627, (await RefusedAsync(() => AddTypeAsync(t => t.Code = "Fracture"))).Number);     // the unique key on the code
    }

    [Fact]
    public async Task A_condition_is_never_deleted()
    {
        await using var db = _fixture.CreateContext();
        db.ConditionTypes.Remove(await db.ConditionTypes.SingleAsync(c => c.Code == "Caries"));
        Assert.Equal(51057, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        Assert.Equal(6, await _s.CountAsync(d => d.ConditionTypes));
    }

    [Fact]
    public async Task What_a_condition_means_cannot_change_once_it_exists_but_it_can_be_retired_and_reactivated()
    {
        var id = await AddTypeAsync();
        foreach (var change in new Action<ConditionType>[] { t => t.Label = "Cracked", t => t.Scope = "WholeTooth", t => t.AppliesTo = "Primary", t => t.ToothEffect = "Absent" })
        {
            await using var db = _fixture.CreateContext();
            change(await db.ConditionTypes.SingleAsync(c => c.Id == id));
            Assert.Equal(51060, (await RefusedAsync(() => db.SaveChangesAsync())).Number);
        }
        await using (var db = _fixture.CreateContext())
        {
            var t = await db.ConditionTypes.SingleAsync(c => c.Id == id);
            t.IsActive = false; t.UpdatedAtUtc = At;
            await db.SaveChangesAsync();                                                 // retiring is the one thing that may change
        }
        Assert.False((await _s.CountAsync(d => d.ConditionTypes.Where(c => c.Id == id && !c.IsActive))) == 0);
    }

    [Fact]
    public async Task Condition_history_is_append_only_unique_per_number_and_limited_to_the_known_changes()
    {
        var id = await AddTypeAsync();
        ConditionTypeEvent Ev(int n, string change = "Created") => new() { Id = Guid.NewGuid(), ConditionTypeId = id, Code = "Fracture", EventNumber = n, ChangeType = change, OccurredAtUtc = At };
        Guid first;
        await using (var db = _fixture.CreateContext()) { var e = Ev(1); db.ConditionTypeEvents.Add(e); await db.SaveChangesAsync(); first = e.Id; }
        await using (var db = _fixture.CreateContext()) { db.ConditionTypeEvents.Add(Ev(1, "Retired")); Assert.Equal(2601, (await RefusedAsync(() => db.SaveChangesAsync())).Number); }
        await using (var db = _fixture.CreateContext()) { db.ConditionTypeEvents.Add(Ev(2, "Deleted")); Assert.Equal(547, (await RefusedAsync(() => db.SaveChangesAsync())).Number); }
        await using (var db = _fixture.CreateContext()) { (await db.ConditionTypeEvents.SingleAsync(e => e.Id == first)).Reason = "Rewritten"; Assert.Equal(51058, (await RefusedAsync(() => db.SaveChangesAsync())).Number); }
        await using (var db = _fixture.CreateContext()) { db.ConditionTypeEvents.Remove(await db.ConditionTypeEvents.SingleAsync(e => e.Id == first)); Assert.Equal(51058, (await RefusedAsync(() => db.SaveChangesAsync())).Number); }
        Assert.Equal(1, await _s.CountAsync(d => d.ConditionTypeEvents));
    }

    // ---------- a finding and the catalogue ----------

    [Theory]
    [InlineData("Cavity")]            // not in the catalogue
    [InlineData("caries")]            // spelled differently: the default collation would accept it
    [InlineData("CARIES")]
    [InlineData("")]
    public async Task A_finding_can_only_name_a_condition_that_exists_spelled_exactly(string condition)
        => Assert.Equal(547, (await RefusedAsync(() => AddFindingAsync(f => f.Condition = condition))).Number);

    [Fact]
    public async Task A_finding_can_use_a_condition_added_later_and_a_retired_condition_is_still_referenceable_by_the_findings_that_used_it()
    {
        var id = await AddTypeAsync();
        await AddFindingAsync(f => { f.Condition = "Fracture"; f.ConditionScope = "Surface"; });
        await using (var db = _fixture.CreateContext())
        {
            (await db.ConditionTypes.SingleAsync(c => c.Id == id)).IsActive = false;
            await db.SaveChangesAsync();
        }
        Assert.Equal(1, await _s.CountAsync(d => d.ToothFindings.Where(f => f.Condition == "Fracture")));
    }

    [Theory]
    [InlineData("Surface", null)]         // a surface condition with no surface
    [InlineData("WholeTooth", "O")]       // a whole-tooth condition with a surface
    [InlineData("surface", "O")]
    [InlineData("Area", null)]
    public async Task A_findings_surface_must_match_its_stored_scope_and_the_scope_must_be_a_known_one(string scope, string? surface)
        => Assert.Equal(547, (await RefusedAsync(() => AddFindingAsync(f => { f.ConditionScope = scope; f.Surface = surface; }))).Number);

    [Fact]
    public async Task A_finding_can_be_made_of_either_scope_with_or_without_a_surface_as_the_scope_says()
    {
        await AddFindingAsync(f => { f.Surface = "O"; f.ConditionScope = "Surface"; });
        await AddFindingAsync(f => { f.Condition = "Crown"; f.Surface = null; f.ConditionScope = "WholeTooth"; });
        Assert.Equal(2, await _s.CountAsync(d => d.ToothFindings));
    }

    [Fact]
    public async Task A_finding_history_can_record_a_link_as_a_change()
    {
        var id = await AddFindingAsync();
        await using var db = _fixture.CreateContext();
        db.ToothFindingVersions.Add(new ToothFindingVersion { Id = Guid.NewGuid(), FindingId = id, PatientId = _ann, VersionNumber = 1, ChangeType = "Linked", ToothKey = "16", Surface = "O", Condition = "Caries", State = "Diagnosed", Status = "Active", OccurredAtUtc = At });
        await db.SaveChangesAsync();
        Assert.Equal(1, await _s.CountAsync(d => d.ToothFindingVersions));
    }

    // ---------- links ----------

    private async Task<Guid> AddLinkAsync(Guid finding, string type = "Diagnosis", string reference = "DX-1")
    {
        await using var db = _fixture.CreateContext();
        var l = new ToothFindingLink { Id = Guid.NewGuid(), FindingId = finding, PatientId = _ann, LinkType = type, Reference = reference, CreatedAtUtc = At };
        db.ToothFindingLinks.Add(l);
        await db.SaveChangesAsync();
        return l.Id;
    }

    [Fact]
    public async Task A_link_can_be_made_of_each_type_once_per_reference_and_never_changed_or_deleted()
    {
        var finding = await AddFindingAsync();
        foreach (var type in LinkTypes.All) await AddLinkAsync(finding, type, "REF-1");
        await AddLinkAsync(finding, "Diagnosis", "REF-2");
        Assert.Equal(2601, (await RefusedAsync(() => AddLinkAsync(finding, "Diagnosis", "REF-1"))).Number);     // the same link twice
        var id = await AddLinkAsync(finding, "Procedure", "PROC-9");
        await using (var db = _fixture.CreateContext()) { (await db.ToothFindingLinks.SingleAsync(l => l.Id == id)).Reference = "Rewritten"; Assert.Equal(51059, (await RefusedAsync(() => db.SaveChangesAsync())).Number); }
        await using (var db = _fixture.CreateContext()) { db.ToothFindingLinks.Remove(await db.ToothFindingLinks.SingleAsync(l => l.Id == id)); Assert.Equal(51059, (await RefusedAsync(() => db.SaveChangesAsync())).Number); }
    }

    [Theory]
    [InlineData("diagnosis", "DX-1")] [InlineData("Note", "DX-1")] [InlineData("Diagnosis", "   ")]
    public async Task A_link_needs_a_known_type_and_a_real_reference(string type, string reference)
    {
        var finding = await AddFindingAsync();
        Assert.Equal(547, (await RefusedAsync(() => AddLinkAsync(finding, type, reference))).Number);
    }

    [Fact]
    public async Task A_link_must_belong_to_a_finding_that_exists()
        => Assert.Equal(547, (await RefusedAsync(() => AddLinkAsync(Guid.NewGuid())))?.Number);
}

/// <summary>ALV-006-C01 upgrade: a database that already holds findings (recorded before the catalogue existed) is migrated forward without losing any of them.</summary>
public class OdontogramCatalogueMigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    public OdontogramCatalogueMigrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Upgrading_a_database_that_holds_findings_fills_in_each_scope_seeds_the_catalogue_and_keeps_every_finding_and_its_history()
    {
        var s = new SchedulingTestSupport(_fixture);
        await s.ArrangeAsync();
        var patient = await s.PatientAsync();

        const string before = "20261004175626_AddOdontogram";
        await using (var db = _fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(before);     // roll back to the step before the catalogue

        var (caries, crown, implant) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var at = DateTimeOffset.UtcNow;
        await using (var db = _fixture.CreateContext())
        {
            Assert.Equal(before, (await db.Database.GetAppliedMigrationsAsync()).Last());
            Assert.False(await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'ConditionTypes'").AnyAsync(x => x > 0));           // genuinely the old schema
            foreach (var (id, tooth, surface, condition) in new[] { (caries, "16", "O", "Caries"), (crown, "11", null, "Crown"), (implant, "36", null, "Implant") })
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ToothFindings (Id, PatientId, ToothKey, Surface, Condition, State, Status, CreatedAtUtc) VALUES ({id}, {patient}, {tooth}, {surface}, {condition}, 'Existing', 'Active', {at})");
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ToothFindingVersions (Id, FindingId, PatientId, VersionNumber, ChangeType, ToothKey, Surface, Condition, State, Status, OccurredAtUtc) VALUES ({Guid.NewGuid()}, {id}, {patient}, 1, 'Recorded', {tooth}, {surface}, {condition}, 'Existing', 'Active', {at})");
            }
        }

        await using (var db = _fixture.CreateContext()) await db.Database.MigrateAsync();                                       // the upgrade

        await using (var db = _fixture.CreateContext())
        {
            var rows = await db.ToothFindings.AsNoTracking().OrderBy(f => f.ToothKey).ToListAsync();
            Assert.Equal(new[] { ("11", "Crown", "WholeTooth", (string?)null), ("16", "Caries", "Surface", "O"), ("36", "Implant", "WholeTooth", null) }, rows.Select(r => (r.ToothKey, r.Condition, r.ConditionScope, r.Surface)));
            Assert.Equal(3, await db.ToothFindingVersions.CountAsync());                                                         // history kept
            Assert.Equal(6, await db.ConditionTypes.CountAsync());                                                               // the catalogue is there for them
            Assert.Equal(3, await db.ToothFindings.Join(db.ConditionTypes, f => f.Condition, c => c.Code, (f, c) => c.Code).CountAsync());   // and every finding resolves to its condition
        }
    }
}
