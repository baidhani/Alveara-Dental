using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Alveara.Api.Architecture.Periodontal;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-013-C01 persistence, against real SQL Server: the database keeps the structure of a diagnosis honest by itself. It accepts a diagnosis with no coding at all, every supported coding system, source
/// and region, the Resolved status and the new history change types; refuses an unknown or half-given coding, a code with characters a code cannot have, an unknown source or region, a source note on a
/// manual diagnosis, a region together with a tooth; keeps a link from a diagnosis to a finding or a periodontal chart append-only (51075), unique, and only ever to a record of the same patient (51076);
/// and still holds the treatment-plan reference as plain text with no foreign key.
/// </summary>
public class DiagnosisStructureSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo, _annEncounter;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();
    private int _findings;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = _ann, EncounterAtUtc = At, Status = EncounterStatuses.Draft, CreatedAtUtc = At };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        _annEncounter = e.Id;
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    private async Task<Guid> AddAsync(Action<Diagnosis>? tweak = null, Guid? patient = null)
    {
        await using var db = _fixture.CreateContext();
        var d = new Diagnosis { Id = Guid.NewGuid(), PatientId = patient ?? _ann, EncounterId = _annEncounter, IdempotencyKey = Guid.NewGuid().ToString("N"), Label = "Chronic periodontitis", Status = DiagnosisStatuses.Active, CreatedAtUtc = At, CreatedByUserId = User };
        tweak?.Invoke(d);
        db.Diagnoses.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    private async Task<Guid> FindingAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var f = new ToothFinding { Id = Guid.NewGuid(), PatientId = patient, ToothKey = ToothKeys.All[_findings++ % 32], Surface = null, Condition = "Crown", ConditionScope = "WholeTooth", State = "Diagnosed", Status = "Active", CreatedAtUtc = At };
        db.ToothFindings.Add(f);
        await db.SaveChangesAsync();
        return f.Id;
    }

    private async Task<Guid> ChartAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var e = new PerioExam { Id = Guid.NewGuid(), PatientId = patient, IdempotencyKey = Guid.NewGuid().ToString("N"), RecordedAtUtc = At, RecordedByUserId = User, ReadingCount = 1 };
        db.PerioExams.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private async Task LinkAsync(Guid diagnosis, string type, Guid target, Guid? patient = null)
    {
        await using var db = _fixture.CreateContext();
        db.DiagnosisLinks.Add(new DiagnosisLink { Id = Guid.NewGuid(), DiagnosisId = diagnosis, PatientId = patient ?? _ann, LinkType = type, TargetId = target, CreatedAtUtc = At, CreatedByUserId = User });
        await db.SaveChangesAsync();
    }

    // ---------- what is accepted ----------

    [Fact]
    public async Task A_diagnosis_with_no_coding_is_stored_and_every_supported_system_source_and_region_is_accepted()
    {
        var plain = await AddAsync();
        await using (var db = _fixture.CreateContext())
        {
            var d = await db.Diagnoses.AsNoTracking().SingleAsync(x => x.Id == plain);
            Assert.Equal((null, null, "Manual", null, null), (d.CodingSystem, d.Code, d.Source, d.SourceNote, d.RegionKey));
        }
        foreach (var system in DiagnosisCodingSystems.All) await AddAsync(d => { d.CodingSystem = system; d.Code = "K05.311"; });
        foreach (var source in new[] { DiagnosisSources.Imported, DiagnosisSources.Mapped }) await AddAsync(d => { d.Source = source; d.SourceNote = "From the previous system"; });
        foreach (var region in DiagnosisRegions.All) await AddAsync(d => d.RegionKey = region);
        await AddAsync(d => d.Code = null);
        await AddAsync(d => { d.CodingSystem = "Local"; d.Code = new string('A', 30); });
        await AddAsync(d => { d.CodingSystem = "Local"; d.Code = "a-b.c_9"; });                              // every allowed character, the hyphen included
        Assert.Equal(1 + 3 + 2 + 9 + 1 + 1 + 1, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task A_resolved_diagnosis_needs_no_withdrawal_stamp_and_a_withdrawn_one_still_needs_it()
    {
        await AddAsync(d => d.Status = DiagnosisStatuses.Resolved);
        Assert.Contains("CK_Diagnoses_WithdrawnStamp", (await RefusedAsync(async () => await AddAsync(d => { d.Status = DiagnosisStatuses.Resolved; d.WithdrawnReason = "no"; }))).Message);
        Assert.Contains("CK_Diagnoses_WithdrawnStamp", (await RefusedAsync(async () => await AddAsync(d => d.Status = DiagnosisStatuses.Withdrawn))).Message);
    }

    [Fact]
    public async Task The_history_accepts_the_new_change_types_and_carries_the_structure_in_every_snapshot()
    {
        var id = await AddAsync();
        await using var db = _fixture.CreateContext();
        var n = 1;
        foreach (var change in DiagnosisChangeTypes.All)
            db.DiagnosisVersions.Add(new DiagnosisVersion { Id = Guid.NewGuid(), DiagnosisId = id, PatientId = _ann, VersionNumber = n++, ChangeType = change, Label = "x", Status = DiagnosisStatuses.Active, CodingSystem = "Local", Code = "A1", Source = "Mapped", SourceNote = "m", RegionKey = "UpperArch", OccurredAtUtc = At, ActorUserId = User });
        await db.SaveChangesAsync();
        Assert.Equal(6, await _fixture.CreateContext().DiagnosisVersions.CountAsync(v => v.DiagnosisId == id));
        var bad = _fixture.CreateContext();
        bad.DiagnosisVersions.Add(new DiagnosisVersion { Id = Guid.NewGuid(), DiagnosisId = id, PatientId = _ann, VersionNumber = 99, ChangeType = "amended", Label = "x", Status = "Active", OccurredAtUtc = At });
        Assert.Contains("CK_DiagnosisVersions_ChangeType", (await RefusedAsync(() => bad.SaveChangesAsync())).Message);
    }

    // ---------- what is refused ----------

    [Theory]
    [InlineData("icd-10-cm")] [InlineData("SNOMED")] [InlineData("")]
    public async Task An_unknown_coding_system_is_refused(string system)
        => Assert.Contains("CK_Diagnoses_CodingSystem", (await RefusedAsync(async () => await AddAsync(d => { d.CodingSystem = system; d.Code = "A1"; }))).Message);

    [Fact]
    public async Task A_system_without_a_code_a_code_without_a_system_and_a_blank_code_are_refused()
    {
        Assert.Contains("CK_Diagnoses_CodingPair", (await RefusedAsync(async () => await AddAsync(d => d.CodingSystem = "Local"))).Message);
        Assert.Contains("CK_Diagnoses_CodingPair", (await RefusedAsync(async () => await AddAsync(d => d.Code = "A1"))).Message);
        Assert.Matches("CK_Diagnoses_(CodingPair|CodeChars)", (await RefusedAsync(async () => await AddAsync(d => { d.CodingSystem = "Local"; d.Code = "   "; }))).Message);       // refused by both checks; the database names one
    }

    [Theory]
    [InlineData("A 1")] [InlineData("A/1")] [InlineData("A,1")] [InlineData("é1")] [InlineData("A\n1")]
    public async Task A_code_with_a_character_a_code_cannot_have_is_refused(string code)
        => Assert.Contains("CK_Diagnoses_CodeChars", (await RefusedAsync(async () => await AddAsync(d => { d.CodingSystem = "Local"; d.Code = code; }))).Message);

    [Fact]
    public async Task An_unknown_source_a_note_on_a_manual_diagnosis_and_a_blank_note_are_refused()
    {
        Assert.Contains("CK_Diagnoses_Source", (await RefusedAsync(async () => await AddAsync(d => d.Source = "manual"))).Message);
        Assert.Contains("CK_Diagnoses_SourceNote", (await RefusedAsync(async () => await AddAsync(d => d.SourceNote = "from somewhere"))).Message);
        Assert.Contains("CK_Diagnoses_SourceNote", (await RefusedAsync(async () => await AddAsync(d => { d.Source = "Imported"; d.SourceNote = "  "; }))).Message);
        Assert.Contains("CK_Diagnoses_SourceNote", (await RefusedAsync(async () => await AddAsync(d => { d.Source = "Imported"; d.SourceNote = "two\nlines"; }))).Message);
    }

    [Fact]
    public async Task An_unknown_region_and_a_region_together_with_a_tooth_are_refused()
    {
        Assert.Contains("CK_Diagnoses_Region", (await RefusedAsync(async () => await AddAsync(d => d.RegionKey = "fullmouth"))).Message);
        Assert.Contains("CK_Diagnoses_Region", (await RefusedAsync(async () => await AddAsync(d => { d.RegionKey = "UpperArch"; d.ToothKey = "16"; }))).Message);
    }

    // ---------- links to a finding or a periodontal chart ----------

    [Fact]
    public async Task A_diagnosis_can_be_linked_to_a_finding_and_a_periodontal_chart_of_the_same_patient_once_each()
    {
        var d = await AddAsync();
        var finding = await FindingAsync(_ann);
        var chart = await ChartAsync(_ann);
        await LinkAsync(d, DiagnosisLinkTypes.Finding, finding);
        await LinkAsync(d, DiagnosisLinkTypes.PerioExam, chart);
        Assert.Equal(2, await _s.CountAsync(db => db.DiagnosisLinks));
        Assert.Contains("IX_DiagnosisLinks_DiagnosisId_LinkType_TargetId", (await RefusedAsync(() => LinkAsync(d, DiagnosisLinkTypes.Finding, finding))).Message);    // once per diagnosis, type and target
    }

    [Fact]
    public async Task A_link_to_a_record_of_another_patient_or_to_nothing_is_refused_with_51076()
    {
        var d = await AddAsync();
        Assert.Equal(51076, (await RefusedAsync(async () => await LinkAsync(d, DiagnosisLinkTypes.Finding, await FindingAsync(_bo)))).Number);
        Assert.Equal(51076, (await RefusedAsync(async () => await LinkAsync(d, DiagnosisLinkTypes.PerioExam, await ChartAsync(_bo)))).Number);
        Assert.Equal(51076, (await RefusedAsync(() => LinkAsync(d, DiagnosisLinkTypes.Finding, Guid.NewGuid()))).Number);
        Assert.Equal(51076, (await RefusedAsync(() => LinkAsync(d, DiagnosisLinkTypes.PerioExam, Guid.NewGuid()))).Number);
        Assert.Equal(51076, (await RefusedAsync(async () => await LinkAsync(d, DiagnosisLinkTypes.Finding, await FindingAsync(_bo), _bo))).Number);            // nor may the link carry another patient
        Assert.Equal(0, await _s.CountAsync(db => db.DiagnosisLinks));
    }

    [Fact]
    public async Task A_link_type_that_is_not_supported_is_refused_and_a_treatment_plan_is_not_a_link_type()
    {
        Assert.DoesNotContain("TreatmentPlan", DiagnosisLinkTypes.All);
        var d = await AddAsync();
        Assert.Contains("CK_DiagnosisLinks_LinkType", (await RefusedAsync(() => LinkAsync(d, "Plan", Guid.NewGuid()))).Message);
        Assert.Contains("CK_DiagnosisLinks_LinkType", (await RefusedAsync(async () => await LinkAsync(d, "finding", await FindingAsync(_ann)))).Message);
    }

    [Fact]
    public async Task A_link_can_never_be_edited_or_deleted_with_51075()
    {
        var d = await AddAsync();
        await LinkAsync(d, DiagnosisLinkTypes.Finding, await FindingAsync(_ann));
        await using var db = _fixture.CreateContext();
        var update = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE [DiagnosisLinks] SET [CreatedByUserId] = NULL"));
        var delete = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [DiagnosisLinks]"));
        Assert.Equal((51075, 51075), (update.Number, delete.Number));
        Assert.Equal(1, await _s.CountAsync(x => x.DiagnosisLinks));
    }

    [Fact]
    public async Task The_treatment_plan_reference_still_has_no_link_to_the_treatment_plan_tables()
    {
        await using var db = _fixture.CreateContext();     // STORY-015 added the plan tables; Diagnoses must still have no foreign key to any of them
        Assert.Equal(0, await db.Database.SqlQuery<int>($@"SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk JOIN sys.tables t ON t.object_id = fk.referenced_object_id
            WHERE fk.parent_object_id = OBJECT_ID('Diagnoses') AND t.name LIKE '%TreatmentPlan%'").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($@"SELECT COUNT(*) AS [Value] FROM sys.foreign_key_columns fkc JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            WHERE c.name = 'TreatmentPlanReference'").SingleAsync());
    }
}

/// <summary>ALV-013-C01 migration check: rolling back removes only what this migration added (the structure columns and the links table) and keeps STORY-013's diagnoses; rolling forward again loses nothing.</summary>
[Collection(ParallelismCollections.SerialServer)]
public class DiagnosisStructureMigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    public DiagnosisStructureMigrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private async Task<bool> TableExistsAsync(string name)
    {
        await using var db = _fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {name}").AnyAsync(x => x > 0);
    }

    private async Task<bool> ColumnExistsAsync(string table, string column)
    {
        await using var db = _fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID({table}) AND name = {column}").AnyAsync(x => x > 0);
    }

    [Fact]
    public async Task Rolling_back_removes_the_structure_and_keeps_the_diagnoses_and_rolling_forward_again_loses_nothing()
    {
        var s = new SchedulingTestSupport(_fixture);
        await s.ArrangeAsync();
        var patient = await s.PatientAsync();
        Guid diagnosis;
        await using (var db = _fixture.CreateContext())
        {
            var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = DateTimeOffset.UtcNow, Status = EncounterStatuses.Draft, CreatedAtUtc = DateTimeOffset.UtcNow };
            db.Encounters.Add(e);
            var d = new Diagnosis { Id = Guid.NewGuid(), PatientId = patient, EncounterId = e.Id, IdempotencyKey = "k1", Label = "Gingivitis", Status = DiagnosisStatuses.Active, CreatedAtUtc = DateTimeOffset.UtcNow, TreatmentPlanReference = "plan-1" };
            db.Diagnoses.Add(d);
            await db.SaveChangesAsync();
            diagnosis = d.Id;
        }
        Assert.True(await TableExistsAsync("DiagnosisLinks"));
        Assert.True(await ColumnExistsAsync("Diagnoses", "CodingSystem"));

        await using (var db = _fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync("20261006001202_AddDiagnoses");
        Assert.False(await TableExistsAsync("DiagnosisLinks"));
        Assert.False(await ColumnExistsAsync("Diagnoses", "CodingSystem"));
        Assert.False(await ColumnExistsAsync("DiagnosisVersions", "Source"));
        await using (var db = _fixture.CreateContext())
        {
            var kept = await db.Database.SqlQuery<string>($"SELECT [Label] + '|' + [TreatmentPlanReference] AS [Value] FROM [Diagnoses] WHERE [Id] = {diagnosis}").SingleAsync();
            Assert.Equal("Gingivitis|plan-1", kept);        // STORY-013's data is untouched (read by SQL: the model now has columns the rolled-back table does not)
        }

        await using (var db = _fixture.CreateContext()) await db.Database.MigrateAsync();
        Assert.True(await TableExistsAsync("DiagnosisLinks"));
        Assert.True(await ColumnExistsAsync("Diagnoses", "RegionKey"));
        await using (var db = _fixture.CreateContext())
        {
            var kept = await db.Diagnoses.AsNoTracking().SingleAsync(x => x.Id == diagnosis);
            Assert.Equal(("Gingivitis", "plan-1", "Manual"), (kept.Label, kept.TreatmentPlanReference, kept.Source));        // an old diagnosis reads as a manual one
        }
    }
}
