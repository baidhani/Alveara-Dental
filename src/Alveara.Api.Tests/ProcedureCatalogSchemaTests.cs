using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Procedures;
using Alveara.Api.Data;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N005 against real SQL Server: the database refuses, on its own, what the service refuses, and what no service rule can see. These tests go around the service on purpose - a script, a
/// future module or a person with database access cannot delete a procedure, change its identity, rewrite a version or an event, skip a version number, or start a version before the one it follows.
/// </summary>
public class ProcedureCatalogSchemaTests : ProcedureTestBase
{
    private static async Task<SqlException> Sql(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    private ProcedureDefinition NewDefinition(string system = "Local", string code = "SCHEMA-1") =>
        new() { Id = Guid.NewGuid(), CodeSystem = system, Code = code, IsActive = true, CurrentVersionNumber = 1, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };

    private ProcedureVersion NewVersion(Guid procedureId, int number = 1, decimal fee = 10m, string description = "Visit", string category = "Diagnostic", string scope = "WholeMouth",
        string dentition = "Both", DateOnly? from = null, DateOnly? through = null) =>
        new() { Id = Guid.NewGuid(), ProcedureId = procedureId, VersionNumber = number, Description = description, Category = category, Scope = scope, Dentition = dentition, Fee = fee,
            EffectiveFrom = from ?? Today, ValidThrough = through, CreatedAtUtc = DateTimeOffset.UtcNow, CreatedByUserId = S.Actor };

    /// <summary>Inserts a definition (with its first version) straight into the database, and returns it.</summary>
    private async Task<ProcedureDefinition> InsertAsync(Action<ProcedureDefinition>? tweak = null, Action<ProcedureVersion>? tweakVersion = null)
    {
        var def = NewDefinition();
        tweak?.Invoke(def);
        var version = NewVersion(def.Id);
        tweakVersion?.Invoke(version);
        await using var db = Fixture.CreateContext();
        db.ProcedureDefinitions.Add(def);
        db.ProcedureVersions.Add(version);
        await db.SaveChangesAsync();
        return def;
    }

    // ---------- check constraints ----------

    [Theory]
    [InlineData("Local", "D1234", "CK_ProcedureDefinitions_LocalNotCdt")]
    [InlineData("CDT", "D12", "CK_ProcedureDefinitions_CdtShape")]
    [InlineData("Local", "lower", "CK_ProcedureDefinitions_CodeShape")]
    [InlineData("Local", "HAS SPACE", "CK_ProcedureDefinitions_CodeShape")]
    [InlineData("Bogus", "ABC-1", "CK_ProcedureDefinitions_CodeSystem")]
    public async Task The_database_refuses_a_code_that_does_not_fit_its_code_system(string system, string code, string constraint)
    {
        var e = await Sql(() => InsertAsync(d => { d.CodeSystem = system; d.Code = code; }));
        Assert.Equal(547, e.Number);
        Assert.Contains(constraint, e.Message);
    }

    [Theory]
    [InlineData(-0.01, "CK_ProcedureVersions_Fee")]
    [InlineData(1000000.01, "CK_ProcedureVersions_Fee")]
    public async Task The_database_refuses_a_fee_outside_its_range(double fee, string constraint)
    {
        var e = await Sql(() => InsertAsync(tweakVersion: v => v.Fee = (decimal)fee));
        Assert.Contains(constraint, e.Message);
    }

    [Theory]
    [InlineData("category", "CK_ProcedureVersions_Category")]
    [InlineData("scope", "CK_ProcedureVersions_Scope")]
    [InlineData("dentition", "CK_ProcedureVersions_Dentition")]
    [InlineData("blank", "CK_ProcedureVersions_Description")]
    [InlineData("control", "CK_ProcedureVersions_Description")]
    [InlineData("wholeMouthPrimary", "CK_ProcedureVersions_DentitionMatchesScope")]
    [InlineData("dates", "CK_ProcedureVersions_Dates")]
    public async Task The_database_refuses_a_version_that_breaks_a_list_or_a_shape_rule(string which, string constraint)
    {
        var e = await Sql(() => InsertAsync(tweakVersion: v =>
        {
            switch (which)
            {
                case "category": v.Category = "Cosmetic"; break;
                case "scope": v.Scope = "Mouth"; break;
                case "dentition": v.Dentition = "Adult"; break;
                case "blank": v.Description = "   "; break;
                case "control": v.Description = "bad\u0007text"; break;
                case "wholeMouthPrimary": v.Dentition = "Primary"; break;
                case "dates": v.ValidThrough = v.EffectiveFrom.AddDays(-1); break;
            }
        }));
        Assert.Contains(constraint, e.Message);
    }

    [Fact]
    public async Task The_database_refuses_the_same_code_twice_in_one_code_system_but_allows_it_in_another()
    {
        await InsertAsync();
        var e = await Sql(() => InsertAsync());
        Assert.Contains(e.Number, new[] { 2601, 2627 });

        await InsertAsync(d => d.CodeSystem = "External", v => v.SourceName = "State schedule");          // same code, other system: fine
    }

    // ---------- triggers ----------

    [Fact]
    public async Task A_procedure_cannot_be_deleted()
    {
        var def = await InsertAsync();
        await using var db = Fixture.CreateContext();
        var e = await Sql(() => db.Database.ExecuteSqlRawAsync("DELETE FROM ProcedureDefinitions WHERE Id = {0}", def.Id));
        Assert.Equal(51077, e.Number);
    }

    [Fact]
    public async Task The_code_and_code_system_cannot_be_changed_but_other_columns_can()
    {
        var def = await InsertAsync();
        await using var db = Fixture.CreateContext();
        Assert.Equal(51078, (await Sql(() => db.Database.ExecuteSqlRawAsync("UPDATE ProcedureDefinitions SET Code = 'OTHER-1' WHERE Id = {0}", def.Id))).Number);
        Assert.Equal(51078, (await Sql(() => db.Database.ExecuteSqlRawAsync("UPDATE ProcedureDefinitions SET CodeSystem = 'External' WHERE Id = {0}", def.Id))).Number);
        Assert.Equal(1, await db.Database.ExecuteSqlRawAsync("UPDATE ProcedureDefinitions SET IsActive = 0 WHERE Id = {0}", def.Id));
    }

    [Fact]
    public async Task A_version_cannot_be_changed_or_removed()
    {
        var def = await InsertAsync();
        await using var db = Fixture.CreateContext();
        Assert.Equal(51079, (await Sql(() => db.Database.ExecuteSqlRawAsync("UPDATE ProcedureVersions SET Fee = 1 WHERE ProcedureId = {0}", def.Id))).Number);
        Assert.Equal(51079, (await Sql(() => db.Database.ExecuteSqlRawAsync("DELETE FROM ProcedureVersions WHERE ProcedureId = {0}", def.Id))).Number);
        Assert.Equal(10m, await db.ProcedureVersions.Where(v => v.ProcedureId == def.Id).Select(v => v.Fee).SingleAsync());
    }

    [Theory]
    [InlineData("CDT", "D1234", null, null, 51083)]
    [InlineData("CDT", "D1234", null, "2026", 51083)]            // an edition alone is not a source
    [InlineData("External", "EXT-1", null, null, 51083)]
    [InlineData("Local", "SCHEMA-1", "Somewhere", null, 51084)]
    [InlineData("Local", "SCHEMA-1", null, "2026", 51084)]
    public async Task The_database_refuses_a_version_whose_source_does_not_fit_its_code_system(string system, string code, string? sourceName, string? sourceVersion, int error)
    {
        var e = await Sql(() => InsertAsync(d => { d.CodeSystem = system; d.Code = code; }, v => { v.SourceName = sourceName; v.SourceVersion = sourceVersion; }));
        Assert.Equal(error, e.Number);
        await using var db = Fixture.CreateContext();
        Assert.Equal(0, await db.ProcedureVersions.CountAsync());                       // the whole insert was rolled back, no orphan version
    }

    [Theory]
    [InlineData("CDT", "D1234", "Licensed set", null)]            // the edition is optional
    [InlineData("CDT", "D1234", "Licensed set", "2026")]
    [InlineData("External", "EXT-1", "State schedule", "FY2030")]
    [InlineData("Local", "SCHEMA-1", null, null)]
    public async Task The_database_accepts_a_version_whose_source_fits_its_code_system(string system, string code, string? sourceName, string? sourceVersion)
    {
        await InsertAsync(d => { d.CodeSystem = system; d.Code = code; }, v => { v.SourceName = sourceName; v.SourceVersion = sourceVersion; });
        await using var db = Fixture.CreateContext();
        Assert.Equal(1, await db.ProcedureVersions.CountAsync());
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("", null)]
    [InlineData("Licensed set", "  ")]
    public async Task The_database_refuses_a_blank_source_name_or_edition(string? sourceName, string? sourceVersion)
    {
        var e = await Sql(() => InsertAsync(d => { d.CodeSystem = "CDT"; d.Code = "D1234"; }, v => { v.SourceName = sourceName; v.SourceVersion = sourceVersion; }));
        Assert.Contains("CK_ProcedureVersions_SourceText", e.Message);
    }

    [Fact]
    public async Task A_history_event_cannot_be_changed_or_removed()
    {
        var created = await CreateAsync();
        await using var db = Fixture.CreateContext();
        Assert.Equal(51080, (await Sql(() => db.Database.ExecuteSqlRawAsync("UPDATE ProcedureEvents SET Reason = 'x' WHERE ProcedureId = {0}", created.Summary.Id))).Number);
        Assert.Equal(51080, (await Sql(() => db.Database.ExecuteSqlRawAsync("DELETE FROM ProcedureEvents WHERE ProcedureId = {0}", created.Summary.Id))).Number);
    }

    [Fact]
    public async Task Version_numbers_must_follow_one_another_and_a_version_cannot_start_before_the_one_it_follows()
    {
        var def = await InsertAsync();
        await using var db = Fixture.CreateContext();

        db.ProcedureVersions.Add(NewVersion(def.Id, number: 3));
        Assert.Equal(51081, (await Sql(() => db.SaveChangesAsync())).Number);
        db.ChangeTracker.Clear();

        db.ProcedureVersions.Add(NewVersion(def.Id, number: 2, from: Today.AddDays(-1)));
        Assert.Equal(51082, (await Sql(() => db.SaveChangesAsync())).Number);
        db.ChangeTracker.Clear();

        db.ProcedureVersions.Add(NewVersion(def.Id, number: 2, fee: 12m, from: Today));       // the same day is allowed
        await db.SaveChangesAsync();
        Assert.Equal(2, await db.ProcedureVersions.CountAsync(v => v.ProcedureId == def.Id));
    }

    [Fact]
    public async Task An_event_must_carry_the_reason_when_it_revises_or_inactivates()
    {
        var created = await CreateAsync();
        await using var db = Fixture.CreateContext();
        db.ProcedureEvents.Add(new ProcedureEvent { Id = Guid.NewGuid(), ProcedureId = created.Summary.Id, EventNumber = 99, ChangeType = "Inactivated", Reason = null, ActorUserId = S.Actor, OccurredAtUtc = DateTimeOffset.UtcNow });
        var e = await Sql(() => db.SaveChangesAsync());
        Assert.Contains("CK_ProcedureEvents_Reason", e.Message);
    }
}
