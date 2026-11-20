using Microsoft.Data.SqlClient;
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
/// ALV-N025 slice 3: migration <c>AddPatientMrn</c> under the MRN rollback addendum (ADDENDUM-R01), against real SQL Server:
/// - Up on a populated database numbers every patient - merged-away rows included - deterministically: with an empty archive, exactly max(own UTC second, previous + 1)
///   in (CreatedAtUtc, Id) order, and the same again on a rerun;
/// - Down archives and Up restores: patients created after Up (the case the retired 51300 refused) keep their numbers exactly, and a patient created while rolled
///   back gets a new number that is no archived one;
/// - the archive is preserved: an UPDATE or DELETE is refused (51302), a disagreement between the archive and the patients makes Down refuse (51302) and change nothing,
///   and a number is unique in it;
/// - archive on insert: a registered patient has its row; a failed insert leaves none;
/// - the retained table is disclosed: after Down the archive (with its guard) is still there and Patients has no Mrn, index or MRN trigger.
/// </summary>
public class MrnMigrationTests : SafetyTestBase
{
    private const string BeforeMrn = "20261119222212_RestoreAppointmentRetiredRecordGuard";

    private async Task DownAsync() { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeMrn); }
    private async Task UpAsync() { await using var db = Fixture.CreateContext(); await db.Database.MigrateAsync(); }
    private async Task SqlAsync(string sql) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }
    private async Task<int> CountAsync(string sql) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql).SingleAsync(); }

    private async Task<Dictionary<Guid, long>> NumbersAsync()
    {
        await using var db = Fixture.CreateContext();
        return await db.Patients.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Mrn);
    }

    private async Task<Dictionary<Guid, long>> ArchiveAsync()
    {
        await using var db = Fixture.CreateContext();
        return (await db.Database.SqlQuery<ArchiveRow>($"SELECT [PatientId], [Mrn] FROM [PatientMrnArchive]").ToListAsync()).ToDictionary(r => r.PatientId, r => r.Mrn);
    }

    private sealed record ArchiveRow(Guid PatientId, long Mrn);

    private static SqlException SqlOf(Exception e) => Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);

    /// <summary>Test setup only: empties the archive with its guard off, so Up meets a populated database with no archived numbers (the first-install case).</summary>
    private Task ClearArchiveAsync() => SqlAsync("DISABLE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive]; DELETE FROM [PatientMrnArchive]; ENABLE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive];");

    private async Task MergeAsync(Guid source, Guid survivor)
    {
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(source, survivor, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(source, survivor, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
    }

    [Fact]
    public async Task Up_on_a_populated_database_numbers_everyone_by_the_rule_merged_rows_included_and_a_rerun_gives_the_same_numbers()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        await SqlAsync($"UPDATE [Patients] SET [CreatedAtUtc] = '2024-05-01T10:00:00+00:00' WHERE [Id] IN ('{kept}', '{gone}')");   // two in one second: the second steps forward (set before the merge: a merged row never changes)
        await MergeAsync(gone, kept);
        await DownAsync();
        await ClearArchiveAsync();
        await UpAsync();

        var first = await NumbersAsync();
        await using (var db = Fixture.CreateContext())
        {
            var rows = await db.Patients.AsNoTracking().Select(p => new { p.Id, p.CreatedAtUtc }).ToListAsync();
            var ordered = await db.Database.SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [Patients] ORDER BY [CreatedAtUtc], [Id]").ToListAsync();   // SQL Server's own uniqueidentifier order
            long? previous = null;
            foreach (var id in ordered)
            {
                var secs = PatientMrn.SecondsOf(rows.Single(r => r.Id == id).CreatedAtUtc);
                var expected = previous is null || secs > previous ? secs : previous.Value + 1;
                Assert.Equal(expected, first[id]);
                previous = expected;
            }
        }
        Assert.Equal(1, Math.Abs(first[gone] - first[kept]));                                                // one second shared: the merged-away row is numbered too, one step apart
        Assert.Equal(first.Count, first.Values.Distinct().Count());
        Assert.All(first.Values, m => Assert.Matches("^[0-9]{11}$", m.ToString()));

        await DownAsync(); await ClearArchiveAsync(); await UpAsync();                                       // rerun from the same rows
        Assert.Equal(first, await NumbersAsync());
    }

    [Fact]
    public async Task Down_archives_and_up_restores_every_number_and_a_patient_added_while_rolled_back_gets_a_new_one()
    {
        var after = await S.PatientAsync("Eve", "Later");                                                   // created after Up: what 51300 used to refuse
        var before = await NumbersAsync();

        await DownAsync();
        Assert.Equal(before, await ArchiveAsync());
        await SqlAsync($@"INSERT INTO [Patients] ([Id], [FirstName], [LastName], [DateOfBirth], [Phone], [AddressLine1], [City], [State], [PostalCode], [DuplicateKey], [CreatedAtUtc], [IsActive])
VALUES ('{Guid.NewGuid()}', 'Rolled', 'Back', '1990-01-01', '555-111-2222', '5 Old Rd', 'Austin', 'TX', '78701', 'back|rolled|1990-01-01', SYSDATETIMEOFFSET(), 1)");
        await UpAsync();

        var restored = await NumbersAsync();
        foreach (var (id, mrn) in before) Assert.Equal(mrn, restored[id]);                                   // exactly the same numbers, the late patient included
        Assert.Equal(before[after], restored[after]);
        var newcomer = Assert.Single(restored.Keys.Except(before.Keys));
        Assert.DoesNotContain(restored[newcomer], before.Values);
        Assert.Equal(restored, await ArchiveAsync());                                                        // and the newcomer's number is archived too
    }

    [Fact]
    public async Task The_archive_refuses_change_and_removal_and_a_disagreement_makes_down_refuse_without_changing_anything()
    {
        Assert.Equal(51302, SqlOf(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"UPDATE [PatientMrnArchive] SET [Mrn] = [Mrn] + 1000 WHERE [PatientId] = '{Ann}'"))).Number);
        Assert.Equal(51302, SqlOf(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"DELETE FROM [PatientMrnArchive] WHERE [PatientId] = '{Ann}'"))).Number);

        // test setup only: the guard is switched off to make the archive disagree with the patient, as only a change outside the system could
        await SqlAsync($"DISABLE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive]; UPDATE [PatientMrnArchive] SET [Mrn] = [Mrn] + 1000 WHERE [PatientId] = '{Ann}'; ENABLE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive];");
        var numbers = await NumbersAsync();
        var archive = await ArchiveAsync();

        Assert.Equal(51302, SqlOf(await Assert.ThrowsAnyAsync<Exception>(DownAsync)).Number);

        Assert.Equal(numbers, await NumbersAsync());                                                         // the column, every number ...
        Assert.Equal(archive, await ArchiveAsync());                                                         // ... and the archive are as they were
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_Patients_MrnImmutable'"));
        await using var db = Fixture.CreateContext();
        Assert.Contains("20261120011518_AddPatientMrn", await db.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task A_number_is_unique_in_the_archive_and_a_failed_insert_leaves_no_archive_row()
    {
        var annMrn = (await NumbersAsync())[Ann];
        var dup = SqlOf(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync(
            $"ALTER TABLE [PatientMrnArchive] NOCHECK CONSTRAINT [FK_PatientMrnArchive_Patients]; INSERT INTO [PatientMrnArchive] ([PatientId], [Mrn], [ArchivedAtUtc]) VALUES (NEWID(), {annMrn}, SYSDATETIMEOFFSET());")));
        Assert.True(dup.Number is 2627 or 2601);

        var failed = Guid.NewGuid();
        await using (var db = Fixture.CreateContext())
        {
            db.Patients.Add(new Patient
            {
                Id = failed, FirstName = "Fay", LastName = "Fail", DateOfBirth = new DateOnly(1990, 1, 1), Phone = "555-222-3333", AddressLine1 = "1 Gone St", City = "Austin", State = "TX", PostalCode = "78701",
                DuplicateKey = Patient.BuildDuplicateKey("Fay", "Fail", new DateOnly(1990, 1, 1)), CreatedAtUtc = DateTimeOffset.UtcNow, HouseholdId = Guid.NewGuid(),   // no such household: the insert fails
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        Assert.False((await ArchiveAsync()).ContainsKey(failed));
        Assert.True((await ArchiveAsync()).ContainsKey(Ann));                                               // a registered patient has its row
    }

    [Fact]
    public async Task After_down_the_archive_is_retained_and_patients_has_no_mrn_column_index_or_trigger()
    {
        await DownAsync();
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'PatientMrnArchive'"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_PatientMrnArchive_Preserve'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID(N'[Patients]') AND name = 'Mrn'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Patients_Mrn'"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name IN ('TR_Patients_MrnImmutable', 'TR_Patients_ArchiveMrn')"));
        await UpAsync();
    }
}
