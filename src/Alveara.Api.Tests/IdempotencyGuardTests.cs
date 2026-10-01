using Alveara.Api.Architecture.Idempotency;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-002-C01: idempotency/correlation primitives for consequential commands - generalizes the
/// pattern BackgroundJob.IdempotencyKey already established, so any consequential command (not
/// only background jobs) can detect and skip a duplicate submission.
/// </summary>
public class IdempotencyGuardTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task A_command_key_is_not_processed_until_MarkProcessed_is_saved()
    {
        const string commandType = "test.command";
        var key = Guid.NewGuid().ToString();

        await using var db = _fixture.CreateContext();
        Assert.False(await IdempotencyGuard.AlreadyProcessedAsync(db, commandType, key));

        IdempotencyGuard.MarkProcessed(db, commandType, key);
        await db.SaveChangesAsync();

        Assert.True(await IdempotencyGuard.AlreadyProcessedAsync(db, commandType, key));
    }

    [Fact]
    public async Task The_same_key_is_scoped_per_command_type_not_globally()
    {
        var key = Guid.NewGuid().ToString();

        await using var db = _fixture.CreateContext();
        IdempotencyGuard.MarkProcessed(db, "command.a", key);
        await db.SaveChangesAsync();

        // Same literal key string, different command type - must not collide.
        Assert.False(await IdempotencyGuard.AlreadyProcessedAsync(db, "command.b", key));
        Assert.True(await IdempotencyGuard.AlreadyProcessedAsync(db, "command.a", key));
    }

    // The application-level AlreadyProcessedAsync check-then-act has an inherent race window
    // between two truly concurrent callers; the real guarantee is the database's own unique
    // index (AlveraDbContext), proven here directly rather than by trying to win a timing race.
    [Fact]
    public async Task A_duplicate_command_key_is_rejected_by_the_database_s_unique_constraint()
    {
        const string commandType = "test.duplicate-command";
        var key = Guid.NewGuid().ToString();

        await using (var first = _fixture.CreateContext())
        {
            IdempotencyGuard.MarkProcessed(first, commandType, key);
            await first.SaveChangesAsync();
        }

        await using var second = _fixture.CreateContext();
        IdempotencyGuard.MarkProcessed(second, commandType, key);
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Inserting_a_new_idempotency_receipt_persists_its_recorded_timestamp()
    {
        const string commandType = "test.timestamp";
        var key = Guid.NewGuid().ToString();
        var before = DateTimeOffset.UtcNow;

        await using var db = _fixture.CreateContext();
        IdempotencyGuard.MarkProcessed(db, commandType, key);
        await db.SaveChangesAsync();

        var stored = await db.IdempotencyReceipts.SingleAsync(r => r.CommandType == commandType && r.IdempotencyKey == key);
        Assert.True(stored.RecordedAtUtc >= before);
    }
}
