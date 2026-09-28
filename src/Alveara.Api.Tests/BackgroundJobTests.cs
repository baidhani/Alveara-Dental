using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Data;
using Microsoft.EntityFrameworkCore;
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

    public Task ExecuteAsync(BackgroundJob job, AlveraDbContext transactionalDb, CancellationToken cancellationToken)
    {
        ExecutionCount++;
        ExecutedJobIds.Add(job.Id);
        return Task.CompletedTask;
    }
}

public sealed class AlwaysFailsJobHandler : IBackgroundJobHandler
{
    public string JobType { get; } = $"test.always-fails.{Guid.NewGuid():N}";
    public Task ExecuteAsync(BackgroundJob job, AlveraDbContext transactionalDb, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated persistent failure.");
}

/// <summary>
/// A handler whose effect is a real database row written through the SAME transactional context
/// the runner gives it — this is the pattern N002-R01-01 requires: any local-DB effect commits
/// atomically with the job's completion receipt, so a crash between "effect committed" and
/// "job marked Succeeded" cannot cause the effect to be silently reapplied on retry.
/// </summary>
public sealed class DatabaseEffectJobHandler : IBackgroundJobHandler
{
    public string JobType { get; } = $"test.db-effect-job.{Guid.NewGuid():N}";
    public string TargetUsernamePrefix { get; } = $"effect-{Guid.NewGuid():N}";

    public Task ExecuteAsync(BackgroundJob job, AlveraDbContext transactionalDb, CancellationToken cancellationToken)
    {
        // A representative "local-database effect": inserting a row through the *same* context
        // the runner will commit together with the effect receipt and Succeeded status.
        transactionalDb.UserAccounts.Add(new Alveara.Api.Architecture.Identity.UserAccount
        {
            Id = Guid.NewGuid(),
            Username = $"{TargetUsernamePrefix}-{job.Id:N}",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        return Task.CompletedTask;
    }
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

    // --- N002-R01-01 regression: the runner previously committed "InProgress" and "Succeeded" as
    // two separate SaveChanges calls, with the handler's own effect potentially committed
    // independently in between — a crash in that window let a recovered retry re-apply the same
    // effect. The runner now writes the handler's effect (via the shared transactionalDb), a
    // completion receipt, and the Succeeded status in ONE transaction. ---

    [Fact]
    public async Task A_handler_s_effect_and_the_job_s_completion_commit_atomically_in_one_transaction()
    {
        var handler = new DatabaseEffectJobHandler();
        Guid jobId;

        await using (var db = _fixture.CreateContext())
        {
            var queue = new BackgroundJobQueue(db);
            var job = await queue.EnqueueAsync($"effect:{Guid.NewGuid()}", handler.JobType, null);
            jobId = job.Id;

            var runner = new BackgroundJobRunner(db, [handler]);
            await runner.ProcessOnceAsync();
        }

        await using var verifyDb = _fixture.CreateContext();
        var job2 = await verifyDb.BackgroundJobs.FindAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Succeeded, job2!.Status);

        var effectRows = verifyDb.UserAccounts.Count(u => u.Username.StartsWith(handler.TargetUsernamePrefix));
        Assert.Equal(1, effectRows); // exactly one effect row — not zero (lost) and not duplicated

        var receiptExists = verifyDb.BackgroundJobEffectReceipts.Any(r => r.JobId == jobId);
        Assert.True(receiptExists);
    }

    [Fact]
    public async Task Recovery_does_not_re_invoke_the_handler_when_an_effect_receipt_already_exists_for_the_job()
    {
        // Simulates a handler whose effect is NOT a simple row in the shared transactionalDb (an
        // "outbox-style" handler that manages its own durable commit for a non-local effect) —
        // the effect + receipt were already committed by a prior attempt, then the process
        // crashed before the job's own status could be updated in that same prior attempt. This
        // is the defensive path the effect-receipt check exists for.
        var handler = new CountingJobHandler();
        Guid jobId;

        await using (var db = _fixture.CreateContext())
        {
            var job = new Alveara.Api.Architecture.BackgroundWork.BackgroundJob
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"already-applied:{Guid.NewGuid()}",
                JobType = handler.JobType,
                Status = BackgroundJobStatus.InProgress,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            };
            db.BackgroundJobs.Add(job);
            db.BackgroundJobEffectReceipts.Add(new Alveara.Api.Architecture.BackgroundWork.BackgroundJobEffectReceipt
            {
                JobId = job.Id,
                RecordedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            });
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await using (var db = _fixture.CreateContext())
        {
            var runner = new BackgroundJobRunner(db, [handler]);
            await runner.RecoverStuckJobsAsync();
            await runner.ProcessOnceAsync();
        }

        Assert.Equal(0, handler.ExecutionCount); // handler must NOT be re-invoked — the effect already happened

        await using var verifyDb = _fixture.CreateContext();
        var job2 = await verifyDb.BackgroundJobs.FindAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Succeeded, job2!.Status);
    }

    [Fact]
    public async Task Two_concurrent_attempts_to_claim_the_same_job_result_in_exactly_one_execution()
    {
        // N002-R01-01 also flagged that claiming was read-then-write, not atomic, so two readers
        // could both believe they owned the same Pending job. TryClaimAsync uses a single
        // conditional UPDATE instead; this proves only one of two concurrent runner instances
        // actually executes the handler.
        var handler = new CountingJobHandler();
        Guid jobId;

        await using (var seedDb = _fixture.CreateContext())
        {
            var queue = new BackgroundJobQueue(seedDb);
            var job = await queue.EnqueueAsync($"race:{Guid.NewGuid()}", handler.JobType, null);
            jobId = job.Id;
        }

        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var runnerA = new BackgroundJobRunner(dbA, [handler]);
        var runnerB = new BackgroundJobRunner(dbB, [handler]);

        await Task.WhenAll(runnerA.ProcessOnceAsync(), runnerB.ProcessOnceAsync());

        Assert.Equal(1, handler.ExecutionCount);
        Assert.Equal(jobId, Assert.Single(handler.ExecutedJobIds));
    }

    [Fact]
    public async Task Recovery_marks_a_job_that_already_exhausted_MaxAttempts_as_Failed_instead_of_looping_forever()
    {
        var handler = new AlwaysFailsJobHandler();
        Guid jobId;

        await using (var db = _fixture.CreateContext())
        {
            var job = new Alveara.Api.Architecture.BackgroundWork.BackgroundJob
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"exhausted:{Guid.NewGuid()}",
                JobType = handler.JobType,
                Status = BackgroundJobStatus.InProgress,
                AttemptCount = 3,
                MaxAttempts = 3,
                StartedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await using (var db = _fixture.CreateContext())
        {
            var runner = new BackgroundJobRunner(db, [handler]);
            var recovered = await runner.RecoverStuckJobsAsync();
            Assert.Equal(1, recovered);
        }

        await using var verifyDb = _fixture.CreateContext();
        var job2 = await verifyDb.BackgroundJobs.FindAsync(jobId);
        Assert.Equal(BackgroundJobStatus.Failed, job2!.Status); // not reset to Pending yet again
    }

    [Fact]
    public async Task Hosted_service_style_recovery_runs_on_every_poll_not_only_once_at_startup()
    {
        // N002-R01-02: a process that restarts within the 5-minute stuck threshold would
        // otherwise never re-check a job it claimed just before crashing, because the old hosted
        // service only called RecoverStuckJobsAsync once, before the loop started. This proves
        // the runner's recovery step is safe to call repeatedly and picks up a job that only
        // *becomes* stale between two calls (simulating "quick restart, then enough time passes").
        var handler = new CountingJobHandler();
        Guid jobId;

        await using (var db = _fixture.CreateContext())
        {
            var job = new Alveara.Api.Architecture.BackgroundWork.BackgroundJob
            {
                Id = Guid.NewGuid(),
                IdempotencyKey = $"quick-restart:{Guid.NewGuid()}",
                JobType = handler.JobType,
                Status = BackgroundJobStatus.InProgress,
                StartedAtUtc = DateTimeOffset.UtcNow, // NOT yet stale — a "quick restart" scenario
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            db.BackgroundJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        await using var db2 = _fixture.CreateContext();
        var runner = new BackgroundJobRunner(db2, [handler]);

        var firstAttemptRecovered = await runner.RecoverStuckJobsAsync();
        Assert.Equal(0, firstAttemptRecovered); // too soon — correctly not recovered yet

        // Time passes; the job is still InProgress from the caller's point of view. A real
        // deployment calls RecoverStuckJobsAsync on every poll (not only at startup), so the next
        // call — once the threshold has elapsed — must recover it.
        await db2.BackgroundJobs
            .Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.StartedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-10)));

        var secondAttemptRecovered = await runner.RecoverStuckJobsAsync();
        Assert.Equal(1, secondAttemptRecovered);
    }
}
