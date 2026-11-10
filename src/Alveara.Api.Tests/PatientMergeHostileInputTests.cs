using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// F-003 (ALV-N019 R02, review correction 04): injection-like text sent through every user-controlled input of a merge and of an undo, against a real SQL Server. The inputs are the reason, the field choices (key and value),
/// the two row versions and the idempotency key. Each hostile value is either accepted and stored as plain data, character for character, or refused with its normal stable code; in every case no table or row is lost,
/// nothing is created that the request did not ask for, and a refused request changes nothing at all. The identifiers the merge interpolates into its SQL come from the model and the catalog, never from these inputs.
/// </summary>
public class PatientMergeHostileInputTests : MergeChartTestBase
{
    public static IEnumerable<object[]> HostileText() =>
    [
        ["'; DROP TABLE [Patients]; --"],
        ["x]]; DELETE FROM [Patients]; --"],
        ["' OR 1=1 --"],
        ["\"; EXEC xp_cmdshell 'dir'; --"],
        ["Robert'); DROP TABLE PatientMerges;--"],
        ["<script>alert(1)</script>"],
        ["%s%s%n {0} {1} ${jndi:ldap://x}"],
    ];

    private readonly record struct Fingerprint(int Tables, int Patients, int Merges, int MovedRows, int Undos);

    private async Task<Fingerprint> FingerprintAsync()
    {
        await using var db = Fixture.CreateContext();
        async Task<int> N(string sql) => await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        return new Fingerprint(await N("SELECT COUNT(*) AS [Value] FROM sys.tables"), await N("SELECT COUNT(*) AS [Value] FROM [Patients]"), await N("SELECT COUNT(*) AS [Value] FROM [PatientMerges]"),
            await N("SELECT COUNT(*) AS [Value] FROM [PatientMergeMovedRows]"), await N("SELECT COUNT(*) AS [Value] FROM [PatientUnmerges]"));
    }

    private async Task<MergePreview> PreviewAsync() => await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(Ann, Bo, default));

    private async Task<MergeResult> MergeWithAsync(MergePreview preview, string reason, string? sourceVersion = null, string? survivorVersion = null, Dictionary<string, string>? choices = null, string? key = null)
        => await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(Ann, Bo, reason, sourceVersion ?? preview.SourceRowVersion, survivorVersion ?? preview.SurvivorRowVersion,
            choices ?? preview.Conflicts.ToDictionary(c => c.Field, _ => "Source"), true, true), key ?? Guid.NewGuid().ToString("N"), default));

    private async Task<PatientException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<PatientException>(action);

    private async Task SetAsync(Guid patient, string column, string? value)
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync($"UPDATE [Patients] SET {PatientMergeCatalog.Q(column)} = @v WHERE [Id] = @p", new SqlParameter("@v", (object?)value ?? DBNull.Value), new SqlParameter("@p", patient));
    }

    private async Task PrepareAsync()
    {
        await GiveRetiringChartAsync(); await GiveSurvivingChartAsync();
        await SetAsync(Ann, "Phone", "(555) 010-0001"); await SetAsync(Bo, "Phone", "(555) 010-0002");        // a conflict, so field choices are really asked for
    }

    // ---------- the reason ----------

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_reason_is_stored_as_plain_text_character_for_character_and_nothing_else_happens(string hostile)
    {
        await PrepareAsync();
        var before = await FingerprintAsync();
        var preview = await PreviewAsync();

        var result = await MergeWithAsync(preview, hostile);

        Assert.Equal(hostile, result.Reason);
        var after = await FingerprintAsync();
        Assert.Equal((before.Tables, before.Patients, before.Merges + 1, before.Undos), (after.Tables, after.Patients, after.Merges, after.Undos));      // no table lost, no patient lost, exactly one merge
        await using var db = Fixture.CreateContext();
        Assert.Equal(hostile, await db.PatientMerges.AsNoTracking().Where(m => m.Id == result.MergeId).Select(m => m.Reason).SingleAsync());
        Assert.Equal(0, await AgreementViolationsAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("two\nlines")]
    [InlineData("tab\there")]
    public async Task An_empty_or_multi_line_reason_is_refused_with_its_stable_code_and_changes_nothing(string reason)
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();

        var ex = await RefusedAsync(() => MergeWithAsync(preview, reason));

        Assert.Equal("reason_invalid", ex.Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task A_reason_over_500_characters_is_refused_even_when_it_is_all_injection_text_and_changes_nothing()
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();

        var ex = await RefusedAsync(() => MergeWithAsync(preview, string.Concat(Enumerable.Repeat("'; DROP TABLE [Patients]; --", 20))));

        Assert.Equal("reason_invalid", ex.Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    // ---------- the row versions ----------

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_row_version_is_refused_as_not_valid_and_changes_nothing(string hostile)
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();

        var source = await RefusedAsync(() => MergeWithAsync(preview, "Registered twice", sourceVersion: hostile));
        var survivor = await RefusedAsync(() => MergeWithAsync(preview, "Registered twice", survivorVersion: hostile));

        Assert.Equal(("row_version_invalid", "row_version_invalid"), (source.Code, survivor.Code));
        Assert.Equal(before, await FingerprintAsync());
    }

    [Fact]
    public async Task A_well_formed_but_wrong_row_version_is_refused_as_changed_and_changes_nothing()
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();

        var ex = await RefusedAsync(() => MergeWithAsync(preview, "Registered twice", sourceVersion: Convert.ToBase64String(new byte[8])));

        Assert.Equal("patient_changed", ex.Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    // ---------- the field choices ----------

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_field_choice_value_is_refused_and_changes_nothing(string hostile)
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();
        Assert.NotEmpty(preview.Conflicts);

        var ex = await RefusedAsync(() => MergeWithAsync(preview, "Registered twice", choices: preview.Conflicts.ToDictionary(c => c.Field, _ => hostile)));

        Assert.Contains(ex.Code, new[] { "choice_invalid", "choice_required" });
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_field_choice_key_is_never_used_as_a_column_name_and_changes_nothing(string hostile)
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();
        var choices = preview.Conflicts.ToDictionary(c => c.Field, _ => "Source");
        choices[hostile] = "Source";

        // the unknown key is either refused or ignored; either way the schema and every row are intact
        try { await MergeWithAsync(preview, "Registered twice", choices: choices); }
        catch (PatientException ex) { Assert.Contains(ex.Code, new[] { "choice_invalid", "choice_required" }); }

        var after = await FingerprintAsync();
        Assert.Equal((before.Tables, before.Patients), (after.Tables, after.Patients));
        Assert.True(after.Merges - before.Merges is 0 or 1);
        Assert.Equal(0, await AgreementViolationsAsync());
    }

    // ---------- the idempotency key ----------

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_idempotency_key_on_one_line_is_stored_as_data_and_replays_as_the_same_merge(string hostile)
    {
        await PrepareAsync();
        var preview = await PreviewAsync();
        var key = hostile.Length > 100 ? hostile[..100] : hostile;

        var first = await MergeWithAsync(preview, "Registered twice", key: key);
        var before = await FingerprintAsync();
        var second = await MergeWithAsync(preview, "Registered twice", key: key);

        Assert.Equal((first.MergeId, true), (second.MergeId, second.Replayed));
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("line\nbreak")]
    public async Task An_empty_or_multi_line_idempotency_key_is_refused_and_changes_nothing(string key)
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();

        var ex = await RefusedAsync(() => MergeWithAsync(preview, "Registered twice", key: key));

        Assert.Equal("idempotency_key_invalid", ex.Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    // ---------- the undo ----------

    private async Task<(Guid MergeId, UnmergePreview Preview)> MergedAsync()
    {
        await PrepareAsync();
        var merged = await MergeWithAsync(await PreviewAsync(), "Registered twice");
        return (merged.MergeId, await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(merged.MergeId, default)));
    }

    private Task<UnmergeResult> UnmergeAsync(Guid mergeId, UnmergePreview p, string reason, string? sourceVersion = null, string? survivorVersion = null, string? key = null)
        => WithDb(db => new PatientUnmergeService(db, Clock).UnmergeAsync(S.Actor, new UnmergeRequest(mergeId, reason, sourceVersion ?? p.SourceRowVersion, survivorVersion ?? p.SurvivorRowVersion, true), key ?? Guid.NewGuid().ToString("N"), default));

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_reason_for_an_undo_is_stored_as_plain_text_and_nothing_else_happens(string hostile)
    {
        var (mergeId, preview) = await MergedAsync();
        var before = await FingerprintAsync();

        var result = await UnmergeAsync(mergeId, preview, hostile);

        Assert.Equal(hostile, result.Reason);
        var after = await FingerprintAsync();
        Assert.Equal((before.Tables, before.Patients, before.Merges, before.Undos + 1), (after.Tables, after.Patients, after.Merges, after.Undos));
        Assert.Equal(0, await AgreementViolationsAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("two\nlines")]
    public async Task An_empty_or_multi_line_undo_reason_is_refused_and_changes_nothing(string reason)
    {
        var (mergeId, preview) = await MergedAsync();
        var before = await FingerprintAsync();

        var ex = await RefusedAsync(() => UnmergeAsync(mergeId, preview, reason));

        Assert.Equal("reason_invalid", ex.Code);
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_row_version_for_an_undo_is_refused_and_changes_nothing(string hostile)
    {
        var (mergeId, preview) = await MergedAsync();
        var before = await FingerprintAsync();

        var source = await RefusedAsync(() => UnmergeAsync(mergeId, preview, "They are different people", sourceVersion: hostile));
        var survivor = await RefusedAsync(() => UnmergeAsync(mergeId, preview, "They are different people", survivorVersion: hostile));

        Assert.Equal(("row_version_invalid", "row_version_invalid"), (source.Code, survivor.Code));
        Assert.Equal(before, await FingerprintAsync());
    }

    [Theory]
    [MemberData(nameof(HostileText))]
    public async Task A_hostile_idempotency_key_for_an_undo_on_one_line_is_stored_as_data_and_replays_as_the_same_undo(string hostile)
    {
        var (mergeId, preview) = await MergedAsync();
        var key = hostile.Length > 100 ? hostile[..100] : hostile;

        var first = await UnmergeAsync(mergeId, preview, "They are different people", key: key);
        var before = await FingerprintAsync();
        var second = await UnmergeAsync(mergeId, preview, "They are different people", key: key);

        Assert.Equal((first.UnmergeId, true), (second.UnmergeId, second.Replayed));
        Assert.Equal(before, await FingerprintAsync());
    }

    // ---------- the identifiers the SQL does interpolate ----------

    [Fact]
    public async Task Every_hostile_input_together_cannot_reach_the_interpolated_identifiers()
    {
        await PrepareAsync();
        var before = await FingerprintAsync(); var preview = await PreviewAsync();
        var hostile = "'; DROP TABLE [Patients]; --";

        var ex = await RefusedAsync(() => MergeWithAsync(preview, hostile, sourceVersion: hostile, survivorVersion: hostile, choices: new Dictionary<string, string> { [hostile] = hostile }, key: hostile));

        Assert.Equal("row_version_invalid", ex.Code);
        Assert.Equal(before, await FingerprintAsync());
        Assert.Equal(0, await AgreementViolationsAsync());
    }
}
