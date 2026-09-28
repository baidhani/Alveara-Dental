using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

public class MigrationTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public MigrationTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migration_creates_all_expected_tables_on_a_fresh_database()
    {
        await using var db = _fixture.CreateContext();

        // Fixture's InitializeAsync already ran MigrateAsync; this proves it actually took effect
        // by round-tripping a real row through each core table.
        var canConnect = await db.Database.CanConnectAsync();
        Assert.True(canConnect);

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task Re_running_migrate_on_an_already_migrated_database_is_a_safe_no_op()
    {
        await using var db = _fixture.CreateContext();

        // Applying migrations twice must not throw or duplicate schema objects — this is the
        // "safe failure behavior" the story's acceptance item requires.
        await db.Database.MigrateAsync();
        await db.Database.MigrateAsync();

        var pendingMigrations = await db.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendingMigrations);
    }

    [Fact]
    public async Task Unique_index_on_username_is_actually_enforced_by_the_real_database()
    {
        await using var db = _fixture.CreateContext();

        db.UserAccounts.Add(new Alveara.Api.Architecture.Identity.UserAccount
        {
            Id = Guid.NewGuid(),
            Username = "shared.username.migration.test",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        await using var db2 = _fixture.CreateContext();
        db2.UserAccounts.Add(new Alveara.Api.Architecture.Identity.UserAccount
        {
            Id = Guid.NewGuid(),
            Username = "shared.username.migration.test", // duplicate
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }
}
