using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Patients.Merge;
using Alveara.Api.Data;
using static Alveara.Api.Tests.SchedulingTestSupport;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-017-C01, dismissing a suggested pair as "not a duplicate" and restoring it, against a real SQL Server: the database on its own (rows written around any service: the pair in either order is one pair, one
/// dismissal in force, never deleted, never edited except to be restored once, a merged-away record cannot be dismissed, the migration rolls back and forward while empty and refuses to roll back over a dismissal
/// (51240 to 51243)) and the service (the scan leaves a dismissed pair out and counts it, a restore brings it back, a repeat is the dismissal already made, each change writes two audit entries with the user and the time,
/// and a failing audit store changes nothing).
/// </summary>
public class PatientDuplicateReviewTests : TreatmentPlanTestBase
{
    private const string BeforeDismissals = "20261025052956_AddPatientMerges";

    private async Task<T> WithService<T>(Func<PatientDuplicateReviewService, Task<T>> action) => await WithDb(db => action(new PatientDuplicateReviewService(db, Clock)));
    private async Task<DuplicateScanResult> ScanAsync() => await WithDb(db => new PatientDuplicateScanner(db).ScanAsync(default));
    private async Task<PatientException> RefusedAsync(Func<Task> action) => await Assert.ThrowsAsync<PatientException>(action);
    private async Task<int> CountAsync(string sql, params SqlParameter[] p) { await using var db = Fixture.CreateContext(); return await db.Database.SqlQueryRaw<int>(sql, p).SingleAsync(); }

    private static async Task<Microsoft.Data.SqlClient.SqlException> Refusal(Func<Task> action)
    {
        var e = await Assert.ThrowsAnyAsync<Exception>(action);
        return Assert.IsType<Microsoft.Data.SqlClient.SqlException>(e is DbUpdateException { InnerException: not null } d ? d.InnerException : e);
    }

    /// <summary>Ann and Bo made to look like the same person (the same birth date and surname), so the scan suggests them.</summary>
    private async Task MakeDuplicatesAsync()
    {
        await using var db = Fixture.CreateContext();
        await db.Database.ExecuteSqlRawAsync("UPDATE [Patients] SET [DateOfBirth] = (SELECT [DateOfBirth] FROM [Patients] WHERE [Id] = {0}), [LastName] = (SELECT [LastName] FROM [Patients] WHERE [Id] = {0}), [FirstName] = N'Anne' WHERE [Id] = {1}", Ann, Bo);
    }

    private PatientDuplicateDismissal Row(Guid a, Guid b, Action<PatientDuplicateDismissal>? change = null)
    {
        var d = new PatientDuplicateDismissal { Id = Guid.NewGuid(), FirstPatientId = a, SecondPatientId = b, Reason = "Twins, not the same person", DismissedByUserId = S.Actor, DismissedAtUtc = DateTimeOffset.UtcNow };
        change?.Invoke(d);
        return d;
    }

    private async Task AddAsync(params object[] rows) { await using var db = Fixture.CreateContext(); db.AddRange(rows); await db.SaveChangesAsync(); }

    // ---------- the database on its own ----------

    [Fact]
    public async Task A_dismissal_names_two_different_patients_a_reason_and_who_and_when_and_one_is_in_force_per_pair_in_either_order()
    {
        var first = Row(Ann, Bo); await AddAsync(first);
        Assert.Equal(2, await CountAsync("SELECT COUNT(DISTINCT [PairKey]) + 1 AS [Value] FROM [PatientDuplicateDismissals]"));                          // one pair, computed
        Assert.Equal(547, (await Refusal(() => AddAsync(Row(Ann, Ann)))).Number);                                                                       // not itself
        Assert.Equal(547, (await Refusal(() => AddAsync(Row(Ann, Guid.NewGuid())))).Number);                                                            // a record that exists
        var other = await S.PatientAsync("Cy", "Kim");                                                                                                  // the check constraints answer for a pair not yet dismissed (the unique rule answers first for one that is)
        Assert.Equal(547, (await Refusal(() => AddAsync(Row(Ann, other, d => d.Reason = "  ")))).Number);
        Assert.Equal(547, (await Refusal(() => AddAsync(Row(Ann, other, d => d.DismissedByUserId = Guid.NewGuid())))).Number);
        Assert.Equal(2601, (await Refusal(() => AddAsync(Row(Ann, Bo)))).Number);                                                                       // the same pair
        Assert.Equal(2601, (await Refusal(() => AddAsync(Row(Bo, Ann)))).Number);                                                                       // in the other order too
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));
    }

    [Fact]
    public async Task A_dismissal_is_never_deleted_keeps_its_pair_reason_and_origin_and_is_restored_once_after_which_the_pair_can_be_dismissed_again()
    {
        var d = Row(Ann, Bo); await AddAsync(d);
        await using var db = Fixture.CreateContext();
        Assert.Equal(51240, (await Refusal(() => db.Database.ExecuteSqlAsync($"DELETE FROM [PatientDuplicateDismissals] WHERE [Id] = {d.Id}"))).Number);
        Assert.Equal(51241, (await Refusal(() => db.Database.ExecuteSqlAsync($"UPDATE [PatientDuplicateDismissals] SET [Reason] = N'Changed' WHERE [Id] = {d.Id}"))).Number);
        Assert.Equal(51241, (await Refusal(() => db.Database.ExecuteSqlAsync($"UPDATE [PatientDuplicateDismissals] SET [DismissedAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {d.Id}"))).Number);
        Assert.Equal(547, (await Refusal(() => db.Database.ExecuteSqlAsync($"UPDATE [PatientDuplicateDismissals] SET [RestoredAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {d.Id}"))).Number);        // restored by nobody
        Assert.Equal(1, await db.Database.ExecuteSqlAsync($"UPDATE [PatientDuplicateDismissals] SET [RestoredByUserId] = {S.Actor}, [RestoredAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {d.Id}"));
        Assert.Equal(51241, (await Refusal(() => db.Database.ExecuteSqlAsync($"UPDATE [PatientDuplicateDismissals] SET [RestoredAtUtc] = SYSDATETIMEOFFSET() WHERE [Id] = {d.Id}"))).Number);   // restored once
        await AddAsync(Row(Bo, Ann));                                                                                                                  // and the pair can be dismissed again: a new row, the history stays
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));
    }

    [Fact]
    public async Task A_record_that_was_merged_away_cannot_be_dismissed()
    {
        var retired = await S.PatientAsync("Cy", "Kim"); var survivor = await S.PatientAsync("Dee", "Kim");
        await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(retired, survivor, default).ContinueWith(async p => await new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(retired, survivor, "Same person", (await p).SourceRowVersion, (await p).SurvivorRowVersion, null, true, true), "m", default)).Unwrap());
        Assert.Equal(51242, (await Refusal(() => AddAsync(Row(retired, Ann)))).Number);
        Assert.Equal(51242, (await Refusal(() => AddAsync(Row(Ann, retired)))).Number);
    }

    [Fact]
    public async Task The_migration_rolls_back_and_forward_while_no_dismissal_exists_and_refuses_to_roll_back_over_one()
    {
        await using (var db = Fixture.CreateContext()) await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeDismissals);
        await using (var db = Fixture.CreateContext())
        {
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'PatientDuplicateDismissals'").SingleAsync());
            Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]PatientDuplicateDismissals%'").SingleAsync());
        }
        await using (var db = Fixture.CreateContext()) await db.Database.MigrateAsync();
        await using (var db = Fixture.CreateContext())
            Assert.Equal(2, await db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name LIKE 'TR[_]PatientDuplicateDismissals%'").SingleAsync());
        await AddAsync(Row(Ann, Bo));
        var e = await Refusal(async () => { await using var db = Fixture.CreateContext(); await db.GetInfrastructure().GetRequiredService<IMigrator>().MigrateAsync(BeforeDismissals); });
        Assert.Equal(51243, e.Number);
    }

    // ---------- the service ----------

    [Fact]
    public async Task A_dismissed_pair_leaves_the_scan_in_either_order_and_is_counted_and_a_restore_brings_it_back()
    {
        await MakeDuplicatesAsync();
        Assert.Single((await ScanAsync()).Pairs);
        var dismissed = await WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Bo, Ann, "Not the same person"), default));                    // the other order from the scan's
        Assert.False(dismissed.Replayed); Assert.Equal(S.Actor, dismissed.DismissedByUserId);
        var scan = await ScanAsync();
        Assert.Empty(scan.Pairs); Assert.Equal(1, scan.Dismissed);
        Assert.Single(await WithService(s => s.ListAsync(default)));

        await WithService(s => s.RestoreAsync(S.Actor, dismissed.Id, default));
        var back = await ScanAsync();
        Assert.Single(back.Pairs); Assert.Equal(0, back.Dismissed);
        Assert.Empty(await WithService(s => s.ListAsync(default)));
        var again = await WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, Bo, "Still not the same person"), default));                  // a new decision, a new row
        Assert.NotEqual(dismissed.Id, again.Id);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));
    }

    [Fact]
    public async Task A_repeat_is_the_dismissal_already_made_and_restoring_twice_is_the_same_restore()
    {
        var first = await WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, Bo, "Twins"), default));
        var repeat = await WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Bo, Ann, "Different words"), default));
        Assert.True(repeat.Replayed); Assert.Equal(first.Id, repeat.Id); Assert.Equal("Twins", repeat.Reason);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientDuplicateDismissed'"));

        await WithService(s => s.RestoreAsync(S.Actor, first.Id, default));
        var second = await WithService(s => s.RestoreAsync(S.Actor, first.Id, default));
        Assert.True(second.Replayed);
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientDuplicateRestored'"));              // logged once
    }

    [Fact]
    public async Task Each_change_writes_two_audit_entries_with_the_user_the_time_and_the_reason_and_no_patient_values()
    {
        var d = await WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, Bo, "They are twins"), default));
        await WithService(s => s.RestoreAsync(Other, d.Id, default));
        await using var db = Fixture.CreateContext();
        var entries = await db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == "PatientDuplicateDismissed" || a.EventType == "PatientDuplicateRestored").ToListAsync();
        Assert.Equal(4, entries.Count);
        Assert.All(entries.Where(e => e.EventType == "PatientDuplicateDismissed"), e => { Assert.Equal(S.Actor, e.PerformedByUserAccountId); Assert.Equal("They are twins", e.Reason); Assert.Equal(d.Id, e.CorrelationId); Assert.True(e.TimestampUtc > DateTimeOffset.UtcNow.AddMinutes(-5)); });
        Assert.All(entries.Where(e => e.EventType == "PatientDuplicateRestored"), e => Assert.Equal(Other, e.PerformedByUserAccountId));
        Assert.Equal(new[] { Ann, Bo }.Order().ToArray(), entries.Where(e => e.EventType == "PatientDuplicateDismissed").Select(e => e.TargetUserAccountId).Order().ToArray());
    }

    [Fact]
    public async Task A_bad_request_is_refused_with_a_code_and_changes_nothing()
    {
        var gone = await S.PatientAsync("Cy", "Kim"); var kept = await S.PatientAsync("Dee", "Kim");
        var preview = await WithDb(db => new PatientMergeService(db, Clock).PreviewAsync(gone, kept, default));
        await WithDb(db => new PatientMergeService(db, Clock).MergeAsync(S.Actor, new MergeRequest(gone, kept, "Same person", preview.SourceRowVersion, preview.SurvivorRowVersion, null, true, true), "m1", default));
        Assert.Equal("first_required", (await RefusedAsync(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(null, Bo, "x"), default)))).Code);
        Assert.Equal("second_required", (await RefusedAsync(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, null, "x"), default)))).Code);
        Assert.Equal("reason_invalid", (await RefusedAsync(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, Bo, "  "), default)))).Code);
        Assert.Equal("same_patient", (await RefusedAsync(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, Ann, "x"), default)))).Code);
        Assert.Equal("patient_not_found", (await RefusedAsync(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, Guid.NewGuid(), "x"), default)))).Code);
        var merged = await RefusedAsync(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(gone, Ann, "x"), default)));
        Assert.Equal(("already_merged", 409), (merged.Code, merged.StatusCode));
        Assert.Equal("dismissal_not_found", (await RefusedAsync(() => WithService(s => s.RestoreAsync(S.Actor, Guid.NewGuid(), default)))).Code);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientDuplicateDismissed'"));
    }

    [Fact]
    public async Task Three_simultaneous_dismissals_of_the_same_pair_make_one_and_a_failing_audit_store_changes_nothing()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(i => WithService(s => s.DismissAsync(S.Actor, i % 2 == 0 ? new DismissRequest(Ann, Bo, "Twins") : new DismissRequest(Bo, Ann, "Twins"), default))));
        Assert.Single(results.Select(r => r.Id).Distinct());
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM [AuditLogEntries] WHERE [EventType] = 'PatientDuplicateDismissed'"));

        var third = await S.PatientAsync("Cy", "Kim");
        await using (var db = Fixture.CreateContext()) await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER [TR_TestAuditDownDismissals] ON [AuditLogEntries] AFTER INSERT AS THROW 59001, 'the audit store is down', 1;");
        try
        {
            var failure = await Assert.ThrowsAnyAsync<Exception>(() => WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, third, "Twins"), default)));
            Assert.Equal(59001, Assert.IsType<SqlException>(failure is DbUpdateException { InnerException: not null } d ? d.InnerException : failure).Number);
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM [PatientDuplicateDismissals]"));                                          // the failed one left no row
        }
        finally { await using var db = Fixture.CreateContext(); await db.Database.ExecuteSqlRawAsync("DROP TRIGGER [TR_TestAuditDownDismissals]"); }
        Assert.False((await WithService(s => s.DismissAsync(S.Actor, new DismissRequest(Ann, third, "Twins"), default))).Replayed);                    // and the same request works once it is back
    }
}
