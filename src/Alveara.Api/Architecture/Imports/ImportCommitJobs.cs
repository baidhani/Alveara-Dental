using System.Text.Json;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Data;
using Microsoft.Extensions.Options;
using Alveara.Api.Architecture.Time;

namespace Alveara.Api.Architecture.Imports;

/// <summary>One unit of commit work on the durable job queue. <paramref name="Step"/> is <c>rows</c> (register the valid rows of rows <paramref name="FromRow"/> to <paramref name="ToRow"/>) or <c>finish</c> (settle the batch once every <c>rows</c> job is over).</summary>
public sealed record ImportCommitJob(Guid BatchId, int Round, string Step, int FromRow, int ToRow, int Wait);

/// <summary>The names and keys of the commit jobs. A key is unique for a batch, a commit round and a piece of work, so putting the same job on the queue twice (a repeated commit request, a crash between the batch update and the queue) returns the job that is already there.</summary>
public static class ImportCommitJobs
{
    public const string JobType = "import.commit";
    public const string RowsStep = "rows";
    public const string FinishStep = "finish";

    public static string RowsKey(Guid batchId, int round, int fromRow) => $"import-commit:{batchId:N}:{round}:{fromRow}";
    public static string RowsKeyPrefix(Guid batchId, int round) => $"import-commit:{batchId:N}:{round}:";
    public static string FinishKey(Guid batchId, int round, int wait) => $"import-commit-finish:{batchId:N}:{round}:{wait}";
    public static string Payload(ImportCommitJob job) => JsonSerializer.Serialize(job);
}

/// <summary>
/// ALV-018-C02 slice 3: runs one commit job on the durable background queue. The runner owns the transaction: whatever the engine stages on the context (patients, their audit entries, row and batch changes) commits together
/// with the job's receipt, or not at all, and a job that fails is retried a bounded number of times and then marked Failed (see <see cref="BackgroundJobRunner"/>); the finish job then reports the batch as Interrupted.
/// </summary>
public sealed class ImportCommitJobHandler(ImportFileStorage files, IOptions<ImportOptions> options, IPracticeClock clock) : IBackgroundJobHandler
{
    public string JobType => ImportCommitJobs.JobType;

    public async Task ExecuteAsync(BackgroundJob job, AlveraDbContext transactionalDb, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<ImportCommitJob>(job.PayloadJson ?? throw new InvalidOperationException("An import commit job carries its work."))
            ?? throw new InvalidOperationException("An import commit job carries its work.");
        await new ImportCommitEngine(transactionalDb, files, options, clock).ExecuteAsync(payload, cancellationToken);
    }
}
