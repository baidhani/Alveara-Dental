using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Patients.Portability.Package;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C01, the export history against a real SQL Server, independently of any application code: a row is written as Started and may be finished once, with its totals; it is never deleted; what it recorded (who,
/// when, what, for whom) never changes (51251); a finished row never changes (51252); the export id is unique; and the states outside the model are refused (an unknown kind, scope or result; a one-patient export with no
/// patient or an all-patients export with one; a readable record for all patients or with document files; a finished state with no finish time; a bad hash; negative totals). The rollback guard (51253) is not exercised here.
/// </summary>
public class DataExportHistorySchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private Guid _actor;
    private Guid _patient;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        var s = new SchedulingTestSupport(_fixture);
        await s.ArrangeAsync();
        _actor = s.Actor;
        _patient = await s.PatientAsync();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _actor, Username = $"admin-{Guid.NewGuid():N}", CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private DataExportHistory Row(Action<DataExportHistory>? change = null)
    {
        var row = new DataExportHistory
        {
            Id = Guid.NewGuid(), ExportId = Guid.NewGuid(), Kind = DataExportKind.Package, Scope = DataExportScope.OnePatient, SubjectRecordId = _patient,
            RequestedByUserId = _actor, RequestedAtUtc = At, Result = DataExportResult.Started,
        };
        change?.Invoke(row);
        return row;
    }

    private async Task AddAsync(DataExportHistory row) { await using var db = _fixture.CreateContext(); db.DataExportHistory.Add(row); await db.SaveChangesAsync(); }

    private async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    private async Task UpdateAsync(Guid id, Action<DataExportHistory> change)
    {
        await using var db = _fixture.CreateContext();
        var row = await db.DataExportHistory.SingleAsync(r => r.Id == id);
        change(row);
        await db.SaveChangesAsync();
    }

    private static readonly string Hash = new('a', 64);

    [Fact]
    public async Task A_started_row_is_finished_once_with_its_totals_and_the_file_hash()
    {
        var row = Row();
        await AddAsync(row);

        await UpdateAsync(row.Id, r => { r.Result = DataExportResult.Complete; r.FinishedAtUtc = At.AddSeconds(3); r.PatientCount = 1; r.FileCount = 22; r.SizeBytes = 48_000; r.Sha256 = Hash; });

        await using var db = _fixture.CreateContext();
        var saved = await db.DataExportHistory.AsNoTracking().SingleAsync(r => r.Id == row.Id);
        Assert.Equal((DataExportResult.Complete, 1, 22, 48_000L, Hash), (saved.Result, saved.PatientCount, saved.FileCount, saved.SizeBytes, saved.Sha256));
    }

    [Fact]
    public async Task A_row_is_never_deleted_and_a_finished_row_never_changes_again()
    {
        var row = Row();
        await AddAsync(row);
        await using (var db = _fixture.CreateContext())
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [DataExportHistory] WHERE [Id] = {0}", row.Id));
            Assert.Equal(51250, Assert.IsType<SqlException>(error is DbUpdateException { InnerException: not null } d ? d.InnerException : error).Number);
        }

        await UpdateAsync(row.Id, r => { r.Result = DataExportResult.Failed; r.FinishedAtUtc = At.AddSeconds(1); });
        var again = await RefusedAsync(() => UpdateAsync(row.Id, r => r.ProblemCount = 4));
        var back = await RefusedAsync(() => UpdateAsync(row.Id, r => { r.Result = DataExportResult.Complete; }));

        Assert.Equal((51252, 51252), (again.Number, back.Number));
    }

    [Fact]
    public async Task What_a_row_recorded_never_changes_even_while_it_is_started()
    {
        var row = Row();
        await AddAsync(row);

        foreach (var change in new Action<DataExportHistory>[] { r => r.ExportId = Guid.NewGuid(), r => r.IncludesDocumentFiles = true, r => r.RequestedAtUtc = At.AddDays(1), r => r.SubjectRecordId = Guid.NewGuid(), r => r.Kind = DataExportKind.ReadableRecord })
            Assert.Equal(51251, (await RefusedAsync(() => UpdateAsync(row.Id, change))).Number);
    }

    [Fact]
    public async Task The_export_id_is_unique_so_a_file_found_later_traces_to_exactly_one_row()
    {
        var first = Row();
        await AddAsync(first);

        var error = await RefusedAsync(() => AddAsync(Row(r => r.ExportId = first.ExportId)));

        Assert.Equal(2601, error.Number);                                                                         // unique index violation
    }

    public static IEnumerable<object[]> StatesOutsideTheModel()
    {
        static object[] Case(string name, Action<DataExportHistory> change) => [name, change];
        yield return Case("an unknown kind", r => r.Kind = "Archive");
        yield return Case("an unknown scope", r => r.Scope = "Everyone");
        yield return Case("an unknown result", r => r.Result = "Done");
        yield return Case("a one-patient export with no patient", r => r.SubjectRecordId = null);
        yield return Case("an all-patients export that names a patient", r => r.Scope = DataExportScope.AllPatients);
        yield return Case("a readable record for all patients", r => { r.Kind = DataExportKind.ReadableRecord; r.Scope = DataExportScope.AllPatients; r.SubjectRecordId = null; });
        yield return Case("a readable record with document files", r => { r.Kind = DataExportKind.ReadableRecord; r.IncludesDocumentFiles = true; });
        yield return Case("a started row with a finish time", r => r.FinishedAtUtc = At.AddSeconds(1));
        yield return Case("a finished row with no finish time", r => r.Result = DataExportResult.Complete);
        yield return Case("a finish time before the request", r => { r.Result = DataExportResult.Complete; r.FinishedAtUtc = At.AddSeconds(-1); });
        yield return Case("a hash that is not 64 lower-case hex characters", r => r.Sha256 = new string('A', 64));
        yield return Case("a hash that is too short", r => r.Sha256 = "abc");
        yield return Case("a negative size", r => r.SizeBytes = -1);
        yield return Case("a negative patient count", r => r.PatientCount = -1);
        yield return Case("a request time outside the supported range", r => r.RequestedAtUtc = new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [MemberData(nameof(StatesOutsideTheModel))]
    public async Task A_state_outside_the_model_is_refused_by_the_database(string name, Action<DataExportHistory> change)
    {
        var error = await RefusedAsync(() => AddAsync(Row(change)));

        Assert.Equal(547, error.Number);                                                                          // check constraint violation
    }
}
