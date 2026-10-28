using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Imports;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-018-C02 slice 3, the commit columns against a real SQL Server, independently of any application code: an Accepted row must name the record it produced (a patient or an appointment, so there is no foreign key); a decision is a person and
/// a time together or neither; a batch's commit outcome is one of the four known ones and its round is never negative; and the guards of the earlier slices still hold for a batch that has been through a commit (the
/// rows of a finished batch never change).
/// </summary>
public class ImportCommitSchemaTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();
    private Guid _actor;
    private Guid _patient;
    private Guid _batch;
    private static readonly DateTimeOffset At = new(2030, 1, 14, 15, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        var s = new SchedulingTestSupport(_fixture);
        await s.ArrangeAsync();
        _actor = s.Actor;
        _patient = await s.PatientAsync();
        await using var db = _fixture.CreateContext();
        db.UserAccounts.Add(new UserAccount { Id = _actor, Username = $"u-{Guid.NewGuid():N}", Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow });
        var batch = new ImportBatch
        {
            Id = Guid.NewGuid(), SourceKind = ImportSourceKind.Patients, SourceLabel = "Source", Status = ImportBatchStatus.Uploaded, Policy = ImportBatchPolicy.AllOrNothing, FileBlobId = Guid.NewGuid(),
            FileSha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"), FileSizeBytes = 100, RowCount = 10, ColumnCount = 3, ColumnNamesJson = "[\"A\"]", CreatedByUserId = _actor, CreatedAtUtc = At,
        };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync();
        _batch = batch.Id;
    }

    public Task DisposeAsync() => _fixture.DisposeAsync();

    private ImportBatchRow Row(Action<ImportBatchRow>? change = null, int number = 2)
    {
        var row = new ImportBatchRow { Id = Guid.NewGuid(), BatchId = _batch, RowNumber = number, Status = ImportRowStatus.Valid, UpdatedAtUtc = At };
        change?.Invoke(row);
        return row;
    }

    private async Task AddAsync(ImportBatchRow row) { await using var db = _fixture.CreateContext(); db.ImportBatchRows.Add(row); await db.SaveChangesAsync(); }

    private async Task UpdateBatchAsync(Action<ImportBatch> change)
    {
        await using var db = _fixture.CreateContext();
        var batch = await db.ImportBatches.SingleAsync(b => b.Id == _batch);
        change(batch);
        await db.SaveChangesAsync();
    }

    private static async Task<SqlException> RefusedAsync(Func<Task> write)
    {
        var ex = await Assert.ThrowsAsync<DbUpdateException>(write);
        return Assert.IsType<SqlException>(ex.InnerException);
    }

    [Fact]
    public async Task An_accepted_row_names_the_record_it_produced_a_patient_or_an_appointment()
    {
        Assert.Equal(547, (await RefusedAsync(() => AddAsync(Row(r => r.Status = ImportRowStatus.Accepted)))).Number);                          // no record named

        await AddAsync(Row(r => { r.Status = ImportRowStatus.Accepted; r.ResultRecordId = _patient; }, 4));
        await AddAsync(Row(r => { r.Status = ImportRowStatus.Accepted; r.ResultRecordId = Guid.NewGuid(); }, 5));                              // the record may be an appointment: no foreign key to patients
        await using var db = _fixture.CreateContext();
        Assert.Equal(2, await db.ImportBatchRows.CountAsync());
    }

    [Fact]
    public async Task A_decision_is_a_person_and_a_time_together_or_neither()
    {
        Assert.Equal(547, (await RefusedAsync(() => AddAsync(Row(r => r.DecisionByUserId = _actor)))).Number);
        Assert.Equal(547, (await RefusedAsync(() => AddAsync(Row(r => r.DecidedAtUtc = At, 3)))).Number);
        Assert.Equal(547, (await RefusedAsync(() => AddAsync(Row(r => { r.DecisionByUserId = Guid.NewGuid(); r.DecidedAtUtc = At; }, 4)))).Number);   // no such person

        await AddAsync(Row(r => { r.DecisionByUserId = _actor; r.DecidedAtUtc = At; }, 5));
    }

    [Fact]
    public async Task A_batchs_commit_outcome_is_one_of_the_known_ones_and_its_round_is_never_negative()
    {
        foreach (var outcome in ImportCommitOutcome.All) await UpdateBatchAsync(b => b.CommitOutcome = outcome);
        await UpdateBatchAsync(b => b.CommitOutcome = null);

        Assert.Equal(547, (await RefusedAsync(() => UpdateBatchAsync(b => b.CommitOutcome = "Mostly"))).Number);
        Assert.Equal(547, (await RefusedAsync(() => UpdateBatchAsync(b => b.CommitRound = -1))).Number);
        Assert.Equal(547, (await RefusedAsync(() => UpdateBatchAsync(b => b.CommitConfirmedByUserId = Guid.NewGuid()))).Number);                // no such person
        await UpdateBatchAsync(b => { b.CommitRound = 1; b.CommitConfirmedByUserId = _actor; b.CommitStartedAtUtc = At; b.Status = ImportBatchStatus.Committing; });
    }

    [Fact]
    public async Task The_rows_of_a_batch_that_has_been_committed_never_change()
    {
        var row = Row(r => { r.Status = ImportRowStatus.Accepted; r.ResultRecordId = _patient; });
        await AddAsync(row);
        await UpdateBatchAsync(b => { b.Status = ImportBatchStatus.Committing; b.CommitRound = 1; b.CommitConfirmedByUserId = _actor; });
        await UpdateBatchAsync(b => { b.Status = ImportBatchStatus.Committed; b.CommitOutcome = ImportCommitOutcome.Completed; b.CommittedAtUtc = At.AddMinutes(1); });

        await using var db = _fixture.CreateContext();
        var tracked = await db.ImportBatchRows.SingleAsync(r => r.Id == row.Id);
        tracked.Reason = "changed";

        var error = await RefusedAsync(() => db.SaveChangesAsync());

        Assert.Equal(51266, error.Number);
    }
}
