using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 4, migration AddPatientCountry (owner decision DEM-1), against real SQL Server:
/// - Up gives every patient Country 'US' and rewrites ONLY exact (case/space-insensitive) matches of the Sex list and of a US state code or full name, each with a
///   PatientHistory row of ChangeType 'SystemNormalized' and NO actor; an off-list value, a value already canonical and a merged-away record are left exactly as they are;
/// - Down keeps the normalized values and the History rows (data preserving by design); Up again normalizes nothing twice;
/// - the migration's frozen copy of the lists is the same as Demographics (Sex values; US codes and full names).
/// </summary>
public class DemographicsMigrationTests : SafetyTestBase
{
    private const string BeforeCountry = "20261120022648_AddMergeTriggerIntegrityRepairs";

    private async Task MigrateToAsync(string? target)
    {
        await using var db = Fixture.CreateContext();
        if (target is null) await db.Database.MigrateAsync();
        else await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(target);
    }

    private async Task SqlAsync(string sql) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    private sealed record Values(string? Sex, string State);
    private sealed record HistoryRow(Guid PatientId, Guid? ChangedByUserId, string ChangeType, string FieldName, string? OldValue, string? NewValue);

    private async Task<Values> ValuesAsync(Guid id)
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<Values>($"SELECT [Sex], [State] FROM [Patients] WHERE [Id] = {id}").SingleAsync();
    }

    private async Task<List<HistoryRow>> SystemHistoryAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.Database.SqlQuery<HistoryRow>($"SELECT [PatientId], [ChangedByUserId], [ChangeType], [FieldName], [OldValue], [NewValue] FROM [PatientHistory] WHERE [ChangeType] = N'SystemNormalized'").ToListAsync();
    }

    private async Task MergeAsync(Guid source, Guid survivor)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
    }

    [Fact]
    public async Task Up_normalizes_only_exact_variants_with_a_system_history_row_and_down_keeps_them()
    {
        var a = await S.PatientAsync("Ada", "Norm"); var b = await S.PatientAsync("Bea", "Norm"); var c = await S.PatientAsync("Cal", "Norm");
        var d = await S.PatientAsync("Dot", "Norm"); var kept = await S.PatientAsync("Eve", "Kim"); var gone = await S.PatientAsync("Fay", "Kim");
        await SqlAsync($"UPDATE [Patients] SET [Sex] = N'female' WHERE [Id] = '{gone}'");                    // before the merge: a merged row never changes afterwards
        await MergeAsync(gone, kept);

        await MigrateToAsync(BeforeCountry);
        await SqlAsync($@"UPDATE [Patients] SET [Sex] = N' female ', [State] = N'illinois' WHERE [Id] = '{a}';
UPDATE [Patients] SET [Sex] = N'MALE', [State] = N'  new   york ' WHERE [Id] = '{b}';
UPDATE [Patients] SET [Sex] = N'F', [State] = N'Ill.' WHERE [Id] = '{c}';
UPDATE [Patients] SET [Sex] = N'Unknown/unspecified', [State] = N'TX' WHERE [Id] = '{d}';");
        await MigrateToAsync(null);

        Assert.Equal(new Values("Female", "IL"), await ValuesAsync(a));
        Assert.Equal(new Values("Male", "NY"), await ValuesAsync(b));
        Assert.Equal(new Values("F", "Ill."), await ValuesAsync(c));                                        // not recognizable: untouched, left for the review list
        Assert.Equal(new Values("Unknown/unspecified", "TX"), await ValuesAsync(d));                        // already canonical: no change
        Assert.Equal("female", (await ValuesAsync(gone)).Sex);                                              // merged away: never changed

        var history = await SystemHistoryAsync();
        Assert.Equal(4, history.Count);
        Assert.All(history, h => Assert.Null(h.ChangedByUserId));                                           // an automated action, no staff actor
        Assert.Contains(new HistoryRow(a, null, "SystemNormalized", "sex", " female ", "Female"), history);
        Assert.Contains(new HistoryRow(a, null, "SystemNormalized", "state", "illinois", "IL"), history);
        Assert.Contains(new HistoryRow(b, null, "SystemNormalized", "sex", "MALE", "Male"), history);
        Assert.Contains(new HistoryRow(b, null, "SystemNormalized", "state", "  new   york ", "NY"), history);
        await using (var db = Fixture.CreateContext())
            Assert.Equal(0, await db.Patients.CountAsync(p => p.Country != "US"));

        var review = await WithDb(db => new PatientDemographicsReader(db).ReviewAsync(default));
        Assert.Contains(review.Items, i => i.PatientId == c && i.Field == "sex" && i.Value == "F");
        Assert.Contains(review.Items, i => i.PatientId == c && i.Field == "state" && i.Value == "Ill.");
        Assert.DoesNotContain(review.Items, i => i.PatientId == gone);                                      // merged away: not for review

        await MigrateToAsync(BeforeCountry);                                                                // Down: data preserving
        Assert.Equal(new Values("Female", "IL"), await ValuesAsync(a));
        Assert.Equal(4, (await SystemHistoryAsync()).Count);
        await MigrateToAsync(null);                                                                         // Up again: nothing left to normalize
        Assert.Equal(4, (await SystemHistoryAsync()).Count);
    }

    [Fact]
    public void The_migrations_frozen_lists_are_the_same_as_Demographics()
    {
        var mapping = (string)typeof(Migrations.AddPatientCountry).GetField("Mapping", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var rows = Regex.Matches(mapping, @"\(N'([ST])', N'((?:[^']|'')*)', N'((?:[^']|'')*)'\)").Select(m => (Kind: m.Groups[1].Value, Key: m.Groups[2].Value.Replace("''", "'"), Value: m.Groups[3].Value.Replace("''", "'"))).ToList();

        var expectedSex = Demographics.SexValues.Select(v => ("S", Demographics.Key(v), v));
        var expectedStates = Demographics.UsStates.SelectMany(s => new[] { ("T", Demographics.Key(s.Code), s.Code), ("T", Demographics.Key(s.Name), s.Code) });
        Assert.Equal(expectedSex.Concat(expectedStates).OrderBy(r => r.Item1).ThenBy(r => r.Item2, StringComparer.Ordinal).ToList(),
            rows.Select(r => (r.Kind, r.Key, r.Value)).OrderBy(r => r.Kind).ThenBy(r => r.Key, StringComparer.Ordinal).ToList());
    }
}
