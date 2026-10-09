using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Alveara.Api.Architecture.Clinical;
using Alveara.Api.Architecture.Odontogram;
using Xunit;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-013 persistence, against real SQL Server: the database itself keeps diagnoses honest, independently of <see cref="DiagnosisRules"/>. It accepts every FDI tooth, the length limits and a withdrawn
/// diagnosis with who, when and why; refuses a blank label, a tooth that is not an FDI key, a blank or multi-line treatment-plan reference, a bad status, a withdrawal without its stamp and a second
/// diagnosis under one idempotency key; refuses an encounter that is not the patient's and any change to a diagnosis's patient, encounter or origin; never deletes a diagnosis; keeps the history
/// append-only; and holds the treatment-plan reference as plain text with no foreign key and no treatment-plan table behind it.
/// </summary>
public class DiagnosisSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private SchedulingTestSupport _s = null!;
    private Guid _ann, _bo, _annEncounter, _boEncounter;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _s = new SchedulingTestSupport(_fixture);
        await _s.ArrangeAsync();
        _ann = await _s.PatientAsync();
        _bo = await _s.PatientAsync("Bo", "Kim");
        _annEncounter = await EncounterAsync(_ann);
        _boEncounter = await EncounterAsync(_bo);
    }
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> EncounterAsync(Guid patient)
    {
        await using var db = _fixture.CreateContext();
        var e = new Encounter { Id = Guid.NewGuid(), PatientId = patient, EncounterAtUtc = At, Status = EncounterStatuses.Draft, CreatedAtUtc = At };
        db.Encounters.Add(e);
        await db.SaveChangesAsync();
        return e.Id;
    }

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    private async Task<Guid> AddAsync(Action<Diagnosis>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var d = new Diagnosis { Id = Guid.NewGuid(), PatientId = _ann, EncounterId = _annEncounter, IdempotencyKey = Guid.NewGuid().ToString("N"), Label = "Chronic periodontitis", Status = DiagnosisStatuses.Active, CreatedAtUtc = At, CreatedByUserId = User };
        tweak?.Invoke(d);
        db.Diagnoses.Add(d);
        await db.SaveChangesAsync();
        return d.Id;
    }

    private async Task<SqlException> SqlRefusedAsync(string sql)
    {
        await using var db = _fixture.CreateContext();
        return await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlRawAsync(sql));
    }

    // ---------- what is accepted ----------

    [Fact]
    public async Task The_database_accepts_every_fdi_tooth_the_limits_a_diagnosis_with_nothing_optional_and_a_reference_of_any_text()
    {
        foreach (var key in ToothKeys.All) await AddAsync(d => d.ToothKey = key);
        await AddAsync(d => d.Label = new string('l', 200));
        await AddAsync(d => d.Notes = new string('n', 1000));
        await AddAsync(d => d.TreatmentPlanReference = new string('p', 100));
        await AddAsync();                                                                                  // no tooth, notes or reference
        await AddAsync(d => d.TreatmentPlanReference = "00000000-0000-0000-0000-000000000000");            // text that names nothing: it is never looked up
        Assert.Equal(52 + 5, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task The_treatment_plan_reference_is_plain_text_with_no_link_to_the_treatment_plan_tables()
    {
        await using var db = _fixture.CreateContext();     // STORY-015 added the plan tables; the old reference text stays plain text (reconciling it is ALV-015-C01's job)
        Assert.Equal(0, await db.Database.SqlQuery<int>($@"SELECT COUNT(*) AS [Value] FROM sys.foreign_key_columns fkc JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            WHERE c.name = 'TreatmentPlanReference'").SingleAsync());
        Assert.Equal(["EncounterId", "PatientId"], (await db.Database.SqlQuery<string>($@"SELECT c.name AS [Value] FROM sys.foreign_key_columns fkc JOIN sys.columns c ON c.object_id = fkc.parent_object_id AND c.column_id = fkc.parent_column_id
            WHERE fkc.parent_object_id = OBJECT_ID('Diagnoses') ORDER BY c.name").ToListAsync()).ToArray());     // the patient and the encounter are the real links
    }

    [Fact]
    public async Task A_withdrawn_diagnosis_with_who_when_and_why_is_accepted_and_a_correction_is_just_an_update()
    {
        var id = await AddAsync(d => { d.Status = DiagnosisStatuses.Withdrawn; d.WithdrawnAtUtc = At; d.WithdrawnByUserId = User; d.WithdrawnReason = "Entered on the wrong patient"; });
        var active = await AddAsync(d => d.TreatmentPlanReference = "plan-7");
        await using var db = _fixture.CreateContext();
        var d = await db.Diagnoses.SingleAsync(x => x.Id == active);
        (d.Label, d.TreatmentPlanReference, d.UpdatedAtUtc, d.UpdatedByUserId) = ("Generalized gingivitis", "plan-7", At.AddMinutes(5), User);          // a correction keeps the reference
        await db.SaveChangesAsync();
        Assert.Equal(("Generalized gingivitis", "plan-7"), (await _fixture.CreateContext().Diagnoses.AsNoTracking().SingleAsync(x => x.Id == active)).Let(x => (x.Label, x.TreatmentPlanReference!)));
        Assert.NotEqual(Guid.Empty, id);
    }

    // ---------- what is refused ----------

    [Theory]
    [InlineData("")] [InlineData("   ")]
    public async Task A_blank_label_is_refused(string label)
        => Assert.Contains("CK_Diagnoses_Label", (await RefusedAsync(async () => await AddAsync(d => d.Label = label))).Message);

    [Theory]
    [InlineData("line\none")] [InlineData("tab\there")] [InlineData("cr\rhere")] [InlineData("bell\a")]
    public async Task A_label_with_a_line_break_or_control_character_is_refused(string label)
        => Assert.Contains("CK_Diagnoses_LabelLine", (await RefusedAsync(async () => await AddAsync(d => d.Label = label))).Message);

    [Theory]
    [InlineData("19")] [InlineData("1")] [InlineData("56")] [InlineData("A1")] [InlineData("  ")]
    public async Task A_tooth_that_is_not_an_fdi_key_is_refused(string tooth)
        => Assert.Contains("CK_Diagnoses_ToothKey", (await RefusedAsync(async () => await AddAsync(d => d.ToothKey = tooth))).Message);

    [Theory]
    [InlineData("")] [InlineData("   ")]
    public async Task A_blank_treatment_plan_reference_is_refused_and_not_stored_as_none(string reference)
        => Assert.Contains("CK_Diagnoses_PlanReference", (await RefusedAsync(async () => await AddAsync(d => d.TreatmentPlanReference = reference))).Message);

    [Theory]
    [InlineData("plan\n7")] [InlineData("plan\t7")] [InlineData("plan\r7")] [InlineData("plan\a7")]
    public async Task A_treatment_plan_reference_with_a_line_break_or_control_character_is_refused(string reference)
        => Assert.Contains("CK_Diagnoses_PlanReferenceLine", (await RefusedAsync(async () => await AddAsync(d => d.TreatmentPlanReference = reference))).Message);

    [Fact]
    public async Task A_bad_status_a_blank_key_and_a_withdrawal_without_its_stamp_are_refused()
    {
        Assert.Contains("CK_Diagnoses_Status", (await RefusedAsync(async () => await AddAsync(d => d.Status = "active"))).Message);                                           // case matters
        Assert.Contains("CK_Diagnoses_Key", (await RefusedAsync(async () => await AddAsync(d => d.IdempotencyKey = "  "))).Message);
        Assert.Contains("CK_Diagnoses_WithdrawnStamp", (await RefusedAsync(async () => await AddAsync(d => d.Status = DiagnosisStatuses.Withdrawn))).Message);               // withdrawn with no who, when or why
        Assert.Contains("CK_Diagnoses_WithdrawnStamp", (await RefusedAsync(async () => await AddAsync(d => { d.Status = DiagnosisStatuses.Withdrawn; d.WithdrawnAtUtc = At; d.WithdrawnByUserId = User; d.WithdrawnReason = "  "; }))).Message);
        Assert.Contains("CK_Diagnoses_WithdrawnStamp", (await RefusedAsync(async () => await AddAsync(d => d.WithdrawnReason = "stray"))).Message);                          // an active one carrying a withdrawal
    }

    [Fact]
    public async Task One_idempotency_key_makes_one_diagnosis_per_patient_and_another_patient_can_reuse_it()
    {
        await AddAsync(d => d.IdempotencyKey = "visit-1");
        Assert.Contains("IX_Diagnoses_PatientId_IdempotencyKey", (await RefusedAsync(async () => await AddAsync(d => d.IdempotencyKey = "visit-1"))).Message);
        await AddAsync(d => { d.IdempotencyKey = "visit-1"; d.PatientId = _bo; d.EncounterId = _boEncounter; });
    }

    [Fact]
    public async Task A_diagnosis_cannot_name_a_patient_or_an_encounter_that_does_not_exist()
    {
        Assert.Contains("FK_Diagnoses_Patients", (await RefusedAsync(async () => await AddAsync(d => d.PatientId = Guid.NewGuid()))).Message);
        Assert.Contains("FK_Diagnoses_Encounters", (await RefusedAsync(async () => await AddAsync(d => d.EncounterId = Guid.NewGuid()))).Message);
    }

    [Fact]
    public async Task A_diagnosis_cannot_be_linked_to_an_encounter_of_a_different_patient()
    {
        var e = await RefusedAsync(async () => await AddAsync(d => d.EncounterId = _boEncounter));                                    // Ann's diagnosis, Bo's encounter
        Assert.Equal(51074, e.Number);
        Assert.Equal(0, await _s.CountAsync(db => db.Diagnoses));
    }

    [Fact]
    public async Task The_patient_the_encounter_and_the_origin_of_a_diagnosis_never_change_but_its_content_can_be_corrected()
    {
        var id = await AddAsync(d => d.TreatmentPlanReference = "plan-7");
        foreach (var set in new[] { $"PatientId = '{_bo}'", $"EncounterId = '{_boEncounter}'", "IdempotencyKey = 'other'", "CreatedAtUtc = '2031-01-01'" })
            Assert.Equal(51073, (await SqlRefusedAsync($"UPDATE Diagnoses SET {set} WHERE Id = '{id}'")).Number);
        Assert.Equal(51073, (await SqlRefusedAsync($"UPDATE Diagnoses SET PatientId = '{_bo}', EncounterId = '{_boEncounter}' WHERE Id = '{id}'")).Number);   // moving both to a consistent pair is still a change of link
        await using var db = _fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync($"UPDATE Diagnoses SET Label = 'Corrected', Notes = 'n', TreatmentPlanReference = 'plan-8' WHERE Id = '{id}'");
        Assert.Equal(("Corrected", "plan-8"), (await db.Diagnoses.AsNoTracking().SingleAsync(x => x.Id == id)).Let(x => (x.Label, x.TreatmentPlanReference!)));
    }

    [Fact]
    public async Task A_diagnosis_is_never_deleted()
    {
        await AddAsync();
        Assert.Equal(51071, (await SqlRefusedAsync("DELETE FROM Diagnoses")).Number);
        Assert.Equal(1, await _s.CountAsync(db => db.Diagnoses));
    }

    // ---------- the history ----------

    private async Task VersionAsync(Guid diagnosis, int number, Action<DiagnosisVersion>? tweak = null)
    {
        await using var db = _fixture.CreateContext();
        var v = new DiagnosisVersion { Id = Guid.NewGuid(), DiagnosisId = diagnosis, PatientId = _ann, VersionNumber = number, ChangeType = DiagnosisChangeTypes.Recorded, Label = "x", Status = DiagnosisStatuses.Active, OccurredAtUtc = At, ActorUserId = User };
        tweak?.Invoke(v);
        db.DiagnosisVersions.Add(v);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_history_accepts_each_change_type_keeps_the_reference_in_every_snapshot_and_numbers_versions_once()
    {
        var id = await AddAsync();
        await VersionAsync(id, 1, v => v.TreatmentPlanReference = "plan-7");
        await VersionAsync(id, 2, v => { v.ChangeType = DiagnosisChangeTypes.Corrected; v.TreatmentPlanReference = "plan-7"; v.Reason = "Wrong tooth"; });
        await VersionAsync(id, 3, v => { v.ChangeType = DiagnosisChangeTypes.Withdrawn; v.Status = DiagnosisStatuses.Withdrawn; v.TreatmentPlanReference = "plan-7"; v.Reason = "Wrong patient"; });
        Assert.Contains("IX_DiagnosisVersions_DiagnosisId_VersionNumber", (await RefusedAsync(async () => await VersionAsync(id, 2))).Message);
        Assert.Contains("CK_DiagnosisVersions_ChangeType", (await RefusedAsync(async () => await VersionAsync(id, 4, v => v.ChangeType = "Edited"))).Message);
        Assert.Contains("CK_DiagnosisVersions_Status", (await RefusedAsync(async () => await VersionAsync(id, 4, v => v.Status = "withdrawn"))).Message);
        Assert.Equal(3, await _s.CountAsync(db => db.DiagnosisVersions));
        Assert.All(await _fixture.CreateContext().DiagnosisVersions.AsNoTracking().ToListAsync(), v => Assert.Equal("plan-7", v.TreatmentPlanReference));
    }

    [Fact]
    public async Task The_history_is_append_only()
    {
        var id = await AddAsync();
        await VersionAsync(id, 1);
        Assert.Equal(51072, (await SqlRefusedAsync("UPDATE DiagnosisVersions SET Label = 'changed'")).Number);
        Assert.Equal(51072, (await SqlRefusedAsync("DELETE FROM DiagnosisVersions")).Number);
        Assert.Equal("x", (await _fixture.CreateContext().DiagnosisVersions.AsNoTracking().SingleAsync()).Label);
    }
}

/// <summary>ALV-N… style migration check for STORY-013: rolling back removes only what the migration added, and rolling forward again loses nothing.</summary>
[Collection(ParallelismCollections.SerialServer)]
public class DiagnosisMigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;
    public DiagnosisMigrationTests(TestDatabaseFixture fixture) => _fixture = fixture;

    private async Task<bool> TableExistsAsync(string name)
    {
        await using var db = _fixture.CreateContext();
        return await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = {name}").AnyAsync(x => x > 0);
    }

    [Fact]
    public async Task Rolling_back_removes_only_the_diagnosis_tables_and_rolling_forward_again_loses_nothing_else()
    {
        var s = new SchedulingTestSupport(_fixture);
        await s.ArrangeAsync();
        var patient = await s.PatientAsync();
        Assert.True(await TableExistsAsync("Diagnoses"));

        await using (var db = _fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync("20261005143339_AddPerioSessionsAndMeasures");
        Assert.False(await TableExistsAsync("Diagnoses"));
        Assert.False(await TableExistsAsync("DiagnosisVersions"));
        await using (var db = _fixture.CreateContext()) Assert.True(await db.Patients.AnyAsync(p => p.Id == patient));        // everything else is as it was

        await using (var db = _fixture.CreateContext()) await db.Database.MigrateAsync();
        Assert.True(await TableExistsAsync("Diagnoses"));
        Assert.True(await TableExistsAsync("DiagnosisVersions"));
        await using (var db = _fixture.CreateContext()) Assert.True(await db.Patients.AnyAsync(p => p.Id == patient));
    }
}

internal static class DiagnosisTestExtensions
{
    public static TResult Let<T, TResult>(this T value, Func<T, TResult> map) => map(value);
}
