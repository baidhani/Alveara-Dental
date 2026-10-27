using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 1, the import batch table against a real SQL Server, independently of any application code: a batch moves from Uploaded to DryRunComplete with its counters; it is never deleted (51260); what it
/// recorded about its file and its maker never changes (51261); a finished batch (Committed, Failed, Cancelled) never changes (51262); only one open batch may exist for the same file of the same kind (a filtered
/// unique index) while a finished one does not block a new one; and every state outside the model is refused (an unknown kind, status or policy, a bad hash, an empty label, no size, rows or columns, negative or
/// impossible counters, impossible times). The rollback guard (51263) is not exercised.
/// </summary>
public class ImportBatchSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private Guid _actor;
    private Guid _other;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _actor = Guid.NewGuid();
        _other = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        foreach (var id in new[] { _actor, _other }) db.UserAccounts.Add(new UserAccount { Id = id, Username = $"u-{Guid.NewGuid():N}", Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private static string NewHash() => Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    private ImportBatch Row(Action<ImportBatch>? change = null)
    {
        var row = new ImportBatch
        {
            Id = Guid.NewGuid(), SourceKind = ImportSourceKind.Patients, SourceLabel = "Source", Status = ImportBatchStatus.Uploaded, Policy = ImportBatchPolicy.AllOrNothing, FileBlobId = Guid.NewGuid(),
            FileSha256 = NewHash(), FileSizeBytes = 100, RowCount = 5, ColumnCount = 3, ColumnNamesJson = "[\"A\",\"B\",\"C\"]", CreatedByUserId = _actor, CreatedAtUtc = At,
        };
        change?.Invoke(row);
        return row;
    }

    private async Task AddAsync(ImportBatch row) { await using var db = _fixture.CreateContext(); db.ImportBatches.Add(row); await db.SaveChangesAsync(); }

    private async Task UpdateAsync(Guid id, Action<ImportBatch> change)
    {
        await using var db = _fixture.CreateContext();
        var row = await db.ImportBatches.SingleAsync(r => r.Id == id);
        change(row);
        await db.SaveChangesAsync();
    }

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    [Fact]
    public async Task A_batch_moves_from_uploaded_to_dry_run_complete_with_its_counters()
    {
        var row = Row();
        await AddAsync(row);

        await UpdateAsync(row.Id, r => { r.Status = ImportBatchStatus.DryRunComplete; r.AttemptedRows = 5; r.AcceptedRows = 3; r.RejectedRows = 1; r.UnresolvedRows = 1; r.UpdatedAtUtc = At.AddMinutes(1); });

        await using var db = _fixture.CreateContext();
        var saved = await db.ImportBatches.AsNoTracking().SingleAsync(r => r.Id == row.Id);
        Assert.Equal((ImportBatchStatus.DryRunComplete, 5, 3, 1, 0, 1), (saved.Status, saved.AttemptedRows, saved.AcceptedRows, saved.RejectedRows, saved.SkippedRows, saved.UnresolvedRows));
    }

    [Fact]
    public async Task A_batch_is_never_deleted()
    {
        var row = Row();
        await AddAsync(row);
        await using var db = _fixture.CreateContext();

        var error = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [ImportBatches] WHERE [Id] = {0}", row.Id));

        Assert.Equal(51260, Assert.IsType<SqlException>(error is DbUpdateException { InnerException: not null } d ? d.InnerException : error).Number);
        Assert.Equal(1, await db.ImportBatches.CountAsync());
    }

    [Fact]
    public async Task What_a_batch_recorded_about_its_file_and_its_maker_never_changes()
    {
        var row = Row();
        await AddAsync(row);

        var changes = new Action<ImportBatch>[]
        {
            r => r.SourceKind = ImportSourceKind.Appointments, r => r.SourceLabel = "Another", r => r.FileBlobId = Guid.NewGuid(), r => r.FileSha256 = NewHash(), r => r.FileSizeBytes = 101,
            r => r.RowCount = 6, r => r.ColumnCount = 4, r => r.ColumnNamesJson = "[\"X\"]", r => r.CreatedByUserId = _other, r => r.CreatedAtUtc = At.AddDays(1),
        };
        foreach (var change in changes) Assert.Equal(51261, (await RefusedAsync(() => UpdateAsync(row.Id, change))).Number);
    }

    [Theory]
    [InlineData(ImportBatchStatus.Committed)]
    [InlineData(ImportBatchStatus.Failed)]
    [InlineData(ImportBatchStatus.Cancelled)]
    public async Task A_finished_batch_never_changes_again(string finished)
    {
        var row = Row();
        await AddAsync(row);
        await UpdateAsync(row.Id, r => r.Status = finished);

        var counters = await RefusedAsync(() => UpdateAsync(row.Id, r => { r.AttemptedRows = 1; r.AcceptedRows = 1; }));
        var reopened = await RefusedAsync(() => UpdateAsync(row.Id, r => r.Status = ImportBatchStatus.Uploaded));
        var other = await RefusedAsync(() => UpdateAsync(row.Id, r => r.Status = finished == ImportBatchStatus.Failed ? ImportBatchStatus.Committed : ImportBatchStatus.Failed));

        Assert.Equal((51262, 51262, 51262), (counters.Number, reopened.Number, other.Number));
    }

    [Fact]
    public async Task Only_one_open_batch_may_exist_for_the_same_file_of_the_same_kind()
    {
        var first = Row();
        await AddAsync(first);
        Task Again(Action<ImportBatch>? change = null) => AddAsync(Row(r => { r.FileSha256 = first.FileSha256; change?.Invoke(r); }));

        Assert.Equal(2601, (await RefusedAsync(() => Again())).Number);                              // the same file, the same kind, still Uploaded
        await UpdateAsync(first.Id, r => r.Status = ImportBatchStatus.DryRunComplete);
        Assert.Equal(2601, (await RefusedAsync(() => Again())).Number);                              // a batch that has been dry-run is still open
        await UpdateAsync(first.Id, r => r.Status = ImportBatchStatus.Committing);
        Assert.Equal(2601, (await RefusedAsync(() => Again())).Number);                              // and so is one being committed

        await Again(r => r.SourceKind = ImportSourceKind.OpeningBalances);                           // the same file as another kind is its own batch
        await UpdateAsync(first.Id, r => r.Status = ImportBatchStatus.Committed);
        await Again();                                                                               // a finished batch does not block a new one
    }

    [Fact]
    public async Task The_states_outside_the_model_are_refused()
    {
        var bad = new (string Name, Action<ImportBatch> Change)[]
        {
            ("kind", r => r.SourceKind = "Everything"), ("status", r => r.Status = "Weird"), ("policy", r => r.Policy = "Both"),
            ("short hash", r => r.FileSha256 = new string('a', 63)), ("upper-case hash", r => r.FileSha256 = new string('A', 64)), ("non-hex hash", r => r.FileSha256 = new string('g', 64)),
            ("blank label", r => r.SourceLabel = "   "), ("no size", r => r.FileSizeBytes = 0), ("no rows", r => r.RowCount = 0), ("no columns", r => r.ColumnCount = 0),
            ("negative counter", r => r.RejectedRows = -1), ("more outcomes than attempts", r => { r.AttemptedRows = 2; r.AcceptedRows = 2; r.RejectedRows = 1; }),
            ("more attempts than rows", r => r.AttemptedRows = 6), ("created before 2000", r => r.CreatedAtUtc = new DateTimeOffset(1999, 12, 31, 0, 0, 0, TimeSpan.Zero)),
            ("updated before created", r => r.UpdatedAtUtc = At.AddSeconds(-1)),
        };

        foreach (var (name, change) in bad)
            Assert.True(547 == (await RefusedAsync(() => AddAsync(Row(change)))).Number, name);

        await using var db = _fixture.CreateContext();
        Assert.Equal(0, await db.ImportBatches.CountAsync());
    }
}
