using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 2, the row results of a batch against a real SQL Server, independently of any application code: a row may be updated in place (a dry-run run again) while its batch is open; it is never
/// deleted (51264); it keeps its batch and row number (51265); the rows of a finished batch never change (51266); a row number exists once for a batch (a unique index); and the states outside the model are
/// refused (an unknown status, a row number below 2, a repeat of a row that is not earlier). The rollback guard (51267) is not exercised.
/// </summary>
public class ImportBatchRowSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private Guid _actor;
    private Guid _batch;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _actor = Guid.NewGuid();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _actor, Username = $"u-{Guid.NewGuid():N}", Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        _batch = await NewBatchAsync();
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> NewBatchAsync()
    {
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(), SourceKind = ImportSourceKind.Patients, SourceLabel = "Source", Status = ImportBatchStatus.Uploaded, Policy = ImportBatchPolicy.AllOrNothing, FileBlobId = Guid.NewGuid(),
            FileSha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), FileSizeBytes = 100, RowCount = 10, ColumnCount = 3, ColumnNamesJson = "[\"A\",\"B\",\"C\"]", CreatedByUserId = _actor, CreatedAtUtc = At,
        };
        await using var db = _fixture.CreateContext();
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();
        return batch.Id;
    }

    private ImportBatchRow Row(Action<ImportBatchRow>? change = null, Guid? batch = null, int number = 2)
    {
        var row = new ImportBatchRow { Id = Guid.NewGuid(), BatchId = batch ?? _batch, RowNumber = number, Status = ImportRowStatus.Valid, UpdatedAtUtc = At };
        change?.Invoke(row);
        return row;
    }

    private async Task AddAsync(ImportBatchRow row) { await using var db = _fixture.CreateContext(); db.ImportBatchRows.Add(row); await db.SaveChangesAsync(); }

    private async Task UpdateAsync(Guid id, Action<ImportBatchRow> change)
    {
        await using var db = _fixture.CreateContext();
        var row = await db.ImportBatchRows.SingleAsync(r => r.Id == id);
        change(row);
        await db.SaveChangesAsync();
    }

    private async Task FinishBatchAsync(Guid id, string status)
    {
        await using var db = _fixture.CreateContext();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == id);
        batch.Status = status;
        await db.SaveChangesAsync();
    }

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    [Fact]
    public async Task A_row_is_updated_in_place_while_its_batch_is_open()
    {
        var row = Row();
        await AddAsync(row);

        await UpdateAsync(row.Id, r => { r.Status = ImportRowStatus.Unresolved; r.Reason = ImportRowReason.PossibleDuplicate; r.MatchedPatientIdsJson = "[\"" + Guid.NewGuid() + "\"]"; r.UpdatedAtUtc = At.AddMinutes(1); });

        await using var db = _fixture.CreateContext();
        var saved = await db.ImportBatchRows.AsNoTracking().SingleAsync(r => r.Id == row.Id);
        Assert.Equal((ImportRowStatus.Unresolved, ImportRowReason.PossibleDuplicate), (saved.Status, saved.Reason));
    }

    [Fact]
    public async Task A_row_is_never_deleted_and_keeps_its_batch_and_its_row_number()
    {
        var row = Row();
        await AddAsync(row);
        var other = await NewBatchAsync();
        await using (var db = _fixture.CreateContext())
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM [ImportBatchRows] WHERE [Id] = {0}", row.Id));
            Assert.Equal(51264, Assert.IsType<SqlException>(error is DbUpdateException { InnerException: not null } d ? d.InnerException : error).Number);
        }

        Assert.Equal(51265, (await RefusedAsync(() => UpdateAsync(row.Id, r => r.BatchId = other))).Number);
        Assert.Equal(51265, (await RefusedAsync(() => UpdateAsync(row.Id, r => r.RowNumber = 3))).Number);
    }

    [Theory]
    [InlineData(ImportBatchStatus.Committed)]
    [InlineData(ImportBatchStatus.Failed)]
    [InlineData(ImportBatchStatus.Cancelled)]
    public async Task The_rows_of_a_finished_batch_never_change(string finished)
    {
        var row = Row();
        await AddAsync(row);
        await FinishBatchAsync(_batch, finished);

        var error = await RefusedAsync(() => UpdateAsync(row.Id, r => r.Status = ImportRowStatus.Rejected));

        Assert.Equal(51266, error.Number);
    }

    [Fact]
    public async Task A_row_number_exists_once_for_a_batch_but_another_batch_may_use_it()
    {
        await AddAsync(Row());

        Assert.Equal(2601, (await RefusedAsync(() => AddAsync(Row()))).Number);
        await AddAsync(Row(batch: await NewBatchAsync()));
    }

    [Fact]
    public async Task The_states_outside_the_model_are_refused()
    {
        var bad = new (string Name, Action<ImportBatchRow> Change, int Number)[]
        {
            ("status", r => r.Status = "Weird", 2), ("header row", _ => { }, 1), ("row zero", _ => { }, 0), ("repeat of itself", r => r.DuplicateOfRow = 4, 4),
            ("repeat of a later row", r => r.DuplicateOfRow = 9, 4), ("repeat of the header", r => r.DuplicateOfRow = 1, 4),
        };

        foreach (var (name, change, number) in bad)
            Assert.True(547 == (await RefusedAsync(() => AddAsync(Row(change, number: number)))).Number, name);

        await AddAsync(Row(r => r.DuplicateOfRow = 2, number: 4));                                  // a repeat of an earlier row is fine
        await using var db = _fixture.CreateContext();
        Assert.Equal(1, await db.ImportBatchRows.CountAsync());
    }
}
