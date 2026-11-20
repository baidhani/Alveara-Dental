using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Architecture.Patients.Portability;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N025 slice 3 (PD-21; brief section 5 with the MRN rollback addendum, ADDENDUM-R01) against real SQL Server:
/// - a new patient's MRN is the current UTC second (11 digits), stepped past every number taken - on a patient or ONLY in the archive - and archived on insert;
/// - concurrent registrations never share a number; past the retry cap the save gives 503 mrn_busy and leaves nothing behind;
/// - guard 51298 refuses any change of an MRN by direct SQL, and EF refuses to send one;
/// - search: exactly 11 digits finds that patient first, before the phone rule; merge and unmerge keep both numbers;
/// - the export file carries source.mrn (informational), and an imported patient gets its own new number.
/// </summary>
public class MrnTests : SafetyTestBase
{
    private async Task<long> MrnOfAsync(Guid id) => await WithDb(db => db.Patients.AsNoTracking().Where(p => p.Id == id).Select(p => p.Mrn).SingleAsync());

    private async Task<long?> ArchivedAsync(Guid id) =>
        await WithDb(async db => (await db.Database.SqlQuery<long>($"SELECT [Mrn] AS [Value] FROM [PatientMrnArchive] WHERE [PatientId] = {id}").ToListAsync()).Cast<long?>().SingleOrDefault());

    private async Task SqlAsync(string sql) { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync(sql); }

    private static SqlException SqlOf(Exception e) => Assert.IsType<SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);

    [Fact]
    public void Numbers_are_given_from_the_start_second_past_every_taken_one_in_order()
    {
        var a = new Patient { FirstName = "A", LastName = "A", Phone = "1", AddressLine1 = "1", City = "c", State = "TX", PostalCode = "1", DuplicateKey = "a" };
        var b = new Patient { FirstName = "B", LastName = "B", Phone = "1", AddressLine1 = "1", City = "c", State = "TX", PostalCode = "1", DuplicateKey = "b" };
        PatientMrn.Assign([a, b], 63_900_000_000, [63_900_000_000, 63_900_000_001, 63_900_000_003]);
        Assert.Equal((63_900_000_002L, 63_900_000_004L), (a.Mrn, b.Mrn));
        Assert.True(PatientMrn.IsMrnText("63900000002"));
        Assert.False(PatientMrn.IsMrnText("6390000000"));                                                    // 10 digits
        Assert.False(PatientMrn.IsMrnText("6390000000x"));
        Assert.Equal(63_900_000_000L, PatientMrn.SecondsOf(new DateTimeOffset(63_900_000_000L * TimeSpan.TicksPerSecond + 9_999_999, TimeSpan.Zero).ToOffset(TimeSpan.FromHours(5))));   // UTC whole seconds, whatever the offset
    }

    [Fact]
    public async Task A_registered_patient_gets_an_11_digit_number_from_the_current_second_and_it_is_archived_on_insert()
    {
        var before = PatientMrn.SecondsOf(DateTimeOffset.UtcNow);
        var id = await S.PatientAsync("Mia", "Stone");
        var mrn = await MrnOfAsync(id);
        Assert.Matches("^[0-9]{11}$", mrn.ToString());
        Assert.InRange(mrn, before, PatientMrn.SecondsOf(DateTimeOffset.UtcNow) + 10);                       // the current second, stepped past at most the setup's few patients
        Assert.Equal(mrn, await ArchivedAsync(id));
        Assert.NotEqual(await MrnOfAsync(Ann), mrn);
    }

    [Fact]
    public async Task A_number_only_in_the_archive_is_never_handed_out_again()
    {
        var seeded = new List<Guid>();
        for (var i = 0; i < 20; i++) seeded.Add(await S.PatientAsync($"Seed{i}", "Archive"));
        var max = await WithDb(db => db.Patients.MaxAsync(p => p.Mrn));
        var archiveOnly = Enumerable.Range(1, 20).Select(i => max + i).ToHashSet();
        // test setup only: the archive guard (51302) is switched off to point the seeded patients' archive rows at the next numbers, so they exist ONLY in the archive
        var updates = string.Join("\n", seeded.Select((id, i) => $"UPDATE [PatientMrnArchive] SET [Mrn] = {max + i + 1} WHERE [PatientId] = '{id}';"));
        await SqlAsync($"DISABLE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive];\n{updates}\nENABLE TRIGGER [TR_PatientMrnArchive_Preserve] ON [PatientMrnArchive];");

        var fresh = await MrnOfAsync(await S.PatientAsync("Nia", "Fresh"));
        Assert.DoesNotContain(fresh, archiveOnly);
        Assert.True(fresh > max);
    }

    [Fact]
    public async Task Concurrent_registrations_never_share_a_number_over_twenty_rounds()
    {
        var ids = new List<Guid>();
        for (var round = 0; round < 20; round++)
        {
            var batch = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => S.PatientAsync($"Race{round}x{i}", "Concurrent")));
            ids.AddRange(batch);
        }
        var numbers = await WithDb(db => db.Patients.AsNoTracking().Where(p => ids.Contains(p.Id)).Select(p => p.Mrn).ToListAsync());
        Assert.Equal(80, numbers.Count);
        Assert.Equal(80, numbers.Distinct().Count());
        var archived = await WithDb(db => db.Database.SqlQuery<long>($"SELECT COUNT_BIG(*) AS [Value] FROM [PatientMrnArchive] WHERE [Mrn] IN (SELECT [Mrn] FROM [Patients])").SingleAsync());
        Assert.True(archived >= 80);
    }

    [Fact]
    public async Task Past_the_retry_cap_the_save_is_mrn_busy_and_leaves_no_patient_and_no_archive_row()
    {
        // test setup only: the archive's foreign key is not checked while 1,100 numbers from now on are archived for patients that do not exist - more than the
        // allocator's look-ahead (1,000), so its first pick past the window collides on the archive index 60 times in a row
        var now = PatientMrn.SecondsOf(DateTimeOffset.UtcNow);
        await SqlAsync($@"ALTER TABLE [PatientMrnArchive] NOCHECK CONSTRAINT [FK_PatientMrnArchive_Patients];
INSERT INTO [PatientMrnArchive] ([PatientId], [Mrn], [ArchivedAtUtc]) SELECT NEWID(), {now} + n, SYSDATETIMEOFFSET()
FROM (SELECT TOP (1100) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS n FROM sys.all_objects a CROSS JOIN sys.all_objects b) x
WHERE NOT EXISTS (SELECT 1 FROM [PatientMrnArchive] e WHERE e.[Mrn] = {now} + n);");                   // the setup's own patients already hold a few of these
        var count = await WithDb(db => db.Patients.CountAsync());
        var archivedBefore = await WithDb(db => db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [PatientMrnArchive]").SingleAsync());

        var busy = await Assert.ThrowsAsync<PatientException>(() => WithDb(db => new PatientRegistrationService(db, Clock)
            .RegisterAsync(PatientTestSupport.Request("Bea", "Busy", "1991-01-01", "(555) 321-7654"), "busy-1", S.Actor, default)));

        Assert.Equal(("mrn_busy", 503), (busy.Code, busy.StatusCode));
        Assert.Equal(count, await WithDb(db => db.Patients.CountAsync()));
        Assert.Equal(archivedBefore, await WithDb(db => db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [PatientMrnArchive]").SingleAsync()));   // the refused save added no archive row
    }

    [Fact]
    public async Task A_number_never_changes_by_sql_or_through_ef()
    {
        var mrn = await MrnOfAsync(Ann);
        var e = SqlOf(await Assert.ThrowsAnyAsync<Exception>(() => SqlAsync($"UPDATE [Patients] SET [Mrn] = [Mrn] + 1000000 WHERE [Id] = '{Ann}'")));
        Assert.Equal(51298, e.Number);

        await using (var db = Fixture.CreateContext())
        {
            var p = await db.Patients.SingleAsync(x => x.Id == Ann);
            p.Mrn += 1;
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        }
        Assert.Equal(mrn, await MrnOfAsync(Ann));
    }

    [Fact]
    public async Task The_mrn_guard_holds_inside_a_merge_session()
    {
        // owner decision 2026-11-19: 51298 is absolute - a merge in progress does not make an MRN changeable
        var mrn = await MrnOfAsync(Ann);
        await using (var db = Fixture.CreateContext())
        {
            await db.Database.OpenConnectionAsync();
            await db.Database.ExecuteSqlRawAsync("EXEC sp_set_session_context N'alveara.patient_merge', 1");
            var e = SqlOf(await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync($"UPDATE [Patients] SET [Mrn] = [Mrn] + 1000000 WHERE [Id] = '{Ann}'")));
            Assert.Equal(51298, e.Number);                                                                       // the MRN guard fires first (sp_settriggerorder), merge session or not
        }
        await using (var db = Fixture.CreateContext())
            Assert.Equal(1, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.sql_modules WHERE [object_id] = OBJECT_ID(N'[TR_Patients_MrnImmutable]') AND [definition] NOT LIKE N'%alveara.patient_merge%'").SingleAsync());
        Assert.Equal(mrn, await MrnOfAsync(Ann));
    }

    [Fact]
    public async Task Exactly_eleven_digits_finds_that_patient_first_and_the_phone_rule_still_applies()
    {
        var target = await S.PatientAsync("Tia", "Target");
        var mrn = (await MrnOfAsync(target)).ToString();
        var phoneMatch = await S.PatientAsync("Pia", "Phone");
        await SqlAsync($"UPDATE [Patients] SET [Phone] = '+{mrn}' WHERE [Id] = '{phoneMatch}'");               // a phone holding the same 11 digits

        var found = await WithDb(db => new PatientDirectory(db, Clock).SearchAsync(mrn, false, 10, default));

        Assert.Equal(target, found[0].Id);
        Assert.Equal(long.Parse(mrn), found[0].Mrn);
        Assert.Contains(found, p => p.Id == phoneMatch);
        Assert.Single(found, p => p.Id == target);                                                           // listed once
        Assert.Empty(await WithDb(db => new PatientDirectory(db, Clock).SearchAsync("99999999999", false, 10, default)));
    }

    [Fact]
    public async Task Merge_and_unmerge_keep_both_numbers()
    {
        var kept = await S.PatientAsync("Cy", "Kim"); var gone = await S.PatientAsync("Dee", "Kim");
        var (keptMrn, goneMrn) = (await MrnOfAsync(kept), await MrnOfAsync(gone));
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(gone, kept, default));
        var merged = await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(gone, kept, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), Guid.NewGuid().ToString("N"), default));
        Assert.Equal((keptMrn, goneMrn), (await MrnOfAsync(kept), await MrnOfAsync(gone)));                  // the merged-away number stays on its retired row

        var undo = await WithDb(db => new PatientUnmergeService(db, Clock).PreviewAsync(merged.MergeId, default));
        await WithDb(db => new PatientUnmergeService(db, Clock).UnmergeAsync(S.Actor, new UnmergeRequest(merged.MergeId, "Different people", undo.SourceRowVersion, undo.SurvivorRowVersion, true), Guid.NewGuid().ToString("N"), default));
        Assert.Equal((keptMrn, goneMrn), (await MrnOfAsync(kept), await MrnOfAsync(gone)));
    }

    [Fact]
    public async Task The_export_file_carries_the_number_and_an_imported_patient_gets_its_own()
    {
        var source = await MrnOfAsync(Bo);
        var file = await WithDb(db => new PatientExportService(db, Clock).ExportAsync(Bo, S.Actor, default));
        var json = JsonNode.Parse(file.Content)!.AsObject();
        Assert.Equal(source, json["source"]!["mrn"]!.GetValue<long>());

        json["patient"]!["firstName"] = "Bodhi"; json["patient"]!["dateOfBirth"] = "2001-02-03";              // another person: no duplicate of any kind
        json["patient"]!["phone"] = "(555) 900-1234"; json["patient"]!["addressLine1"] = "77 Elsewhere Rd";
        var imported = await WithDb(db => new PatientImportService(db, new PatientRegistrationService(db, Clock), Clock).ImportAsync(JsonSerializer.SerializeToUtf8Bytes(json), S.Actor, default));
        var mrn = await MrnOfAsync(imported.PatientId);
        Assert.NotEqual(source, mrn);
        Assert.Matches("^[0-9]{11}$", mrn.ToString());
    }
}
