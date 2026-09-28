using Alveara.Api.Architecture.BackgroundWork;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// Test handler that counts how many times it actually executed, to prove idempotency.
/// JobType is unique per instance (a fresh Guid) rather than a fixed string: the test database is
/// shared across every [Fact] in a test class (IClassFixture lives for the whole class), and a
/// job left Pending by one test (e.g. "enqueue but never process") would otherwise be picked up
/// by a *different* test's ProcessOnceAsync call merely because both used the same JobType string.
/// </summary>
public sealed class CountingJobHandler : IBackgroundJobHandler
{
    public string JobType { get; } = $"test.counting-job.{Guid.NewGuid():N}";
    public int ExecutionCount { get; private set; }
    public List<Guid> ExecutedJobIds { get; } = [];

    public Task ExecuteAsync(BackgroundJob job, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        ExecutedJobIds.Add(job.Id);
        return Task.CompletedTask;
    }
}

public sealed class AlwaysFailsJobHandler : IBackgroundJobHandler
{
    public string JobType { get; } = $"test.always-fails.{Guid.NewGuid():N}";
    public Task ExecuteAsync(BackgroundJob job, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated persistent failure.");
}

public class BackgroundJobTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public BackgroundJobTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Enqueuing_the_same_idempotency_key_twice_creates_only_one_job()
    {
        await using var db = _fixture.CreateContext();
        var queue = new BackgroundJobQueue(db);

        var jobType = $"test.recall.{Guid.NewGuid():N}";
        var first = await queue.EnqueueAsync("recall:patient-123:2026-06-01", jobType, null);
        var second = await queue.EnqueueAsync("recall:patient-123:2026-06-01", jobType, null);

        Assert.Equal(first.Id, second.Id);

        var count = db.BackgroundJobs.Count(j => j.IdempotencyKey == "recall:patient-123:2026-06-01");
        Assert.Equal(1, count);

        // Clean up: this row is deliberately left Pending forever (this test never processes
        // it), and the test database is shared across every [Fact] in this class — left in
        // place, it would be picked up (and correctly rejected as "no handler registered," but
        // still counted) by another test's ProcessOnceAsync call, inflating that test's expected
        // per-call processed count for a reason unrelated to what that test is checking.
        db.BackgroundJobs.Remove(first);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_pending_job_is_processed_exactly_once()
    {
        var handler = new CountingJobHandler();
        Guid jobId;

        await using (var db = _fixture.CreateContext())
        {
            var queue = new BackgroundJobQueue(db);
            var job = await queue.EnqueueAsync($"once:{Guid.NewGuid()}", handler.JobType, null);
            jobId = job.Id;

            var runner = new BackgroundJobRunner(db, [handler]);
            var processed = await runner.ProcessOnceAsync();
            Assert.Equal(1, processed);
        }

        Assert.Equal(1, handler.ExecutionCount);

        await using var verifyDb = _fixture.CreateContext();
        var completedJob = await verifyDb.BackgroundJobs.FindAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Succeeded, completedJob!.Status);
        Assert.NotNull(completedJob.CompletedAtUtc);
    }

    [Fact]
    public async Task Restart_recovery_resumes_a_job_stuck_InProgress_from_a_crashed_process_without_duplicate_side_effects()
    {
        var handler = new CountingJobHandler();
        Guid jobId;

        // Simulate: a previous process instance claimed a job (set it InProgress) and then
        // crashed before completing it — StartedAtUtc is old enough to count as "stuck."
        await using (var db = _fixture.CreateContext())
        {
            var job = new Alveara.Api.Architecture.BackgroundWork.BackgroundJob
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"stuck:{Guid.NewGuid()}",
                JobType = handler.JobType,
                Status = BackgroundJobStatus.InProgress,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10), // older than the 5-minute stuck threshold
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        // A brand-new runner instance (standing in for the restarted process) recovers and
        // completes it.
        await using (var db = _fixture.CreateContext())
        {
            var runner = new BackgroundJobRunner(db, [handler]);
            var recovered = await runner.RecoverStuckJobsAsync();
            Assert.Equal(1, recovered);

            var processed = await runner.ProcessOnceAsync();
            Assert.Equal(1, processed);
        }

        Assert.Equal(1, handler.ExecutionCount); // exactly once, not duplicated by the recovery
        Assert.Equal(jobId, Assert.Single(handler.ExecutedJobIds));

        await using var verifyDb = _fixture.CreateContext();
        var job2 = await verifyDb.BackgroundJobs.FindAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Succeeded, job2!.Status);
    }

    [Fact]
    public async Task A_job_that_keeps_failing_is_retried_up_to_its_max_attempts_then_marked_Failed_not_lost()
    {
        var handler = new AlwaysFailsJobHandler();
        Guid jobId;

        await using (var db = _fixture.CreateContext())
        {
            var job = new Alveara.Api.Architecture.BackgroundWork.BackgroundJob
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"fails:{Guid.NewGuid()}",
                JobType = handler.JobType,
                MaxAttempts = 2,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;

            var runner = new BackgroundJobRunner(db, [handler]);
            await runner.ProcessOnceAsync(); // attempt 1: fails, goes back to Pending
            await runner.ProcessOnceAsync(); // attempt 2: fails, hits MaxAttempts -> Failed
        }

        await using var verifyDb = _fixture.CreateContext();
        var job2 = await verifyDb.BackgroundJobs.FindAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Failed, job2!.Status);
        Assert.Equal(2, job2.AttemptCount);
        Assert.Contains("Simulated persistent failure", job2.LastError);
    }
}
