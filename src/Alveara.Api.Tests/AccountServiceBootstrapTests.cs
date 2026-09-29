using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// Bootstrap is one-time per database, so - unlike every other test class in this project - these
/// tests must NOT share a single LocalDB database across [Fact]s (an IClassFixture instance would
/// be shared class-wide and the second test to bootstrap would spuriously see
/// BootstrapAlreadyConsumedException). Each test method gets its own fresh database instead.
/// </summary>
public class AccountServiceBootstrapTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    [Fact]
    public async Task Bootstrap_with_the_correct_secret_creates_an_enabled_admin_account()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"admin-{Guid.NewGuid():N}";

        var account = await service.BootstrapFirstAdminAsync(username, "password", IdentityTestHelpers.TestBootstrapSecret, IdentityTestHelpers.TestBootstrapSecret);

        Assert.Equal(Role.Admin, account.Role);
        Assert.False(account.IsDisabled);
    }

    [Fact]
    public async Task Bootstrap_with_an_incorrect_secret_is_rejected_and_creates_no_account()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var username = $"admin-{Guid.NewGuid():N}";

        await Assert.ThrowsAsync<InvalidBootstrapSecretException>(
            () => service.BootstrapFirstAdminAsync(username, "password", "wrong-secret", IdentityTestHelpers.TestBootstrapSecret));

        Assert.Equal(0, db.UserAccounts.Count(u => u.Username == username));
    }

    [Fact]
    public async Task Bootstrap_cannot_be_used_a_second_time()
    {
        // Each attempt uses its own DbContext/AccountService, mirroring the real per-request DI
        // scoping (a fresh scoped DbContext per HTTP request) - the one-time-use guarantee comes
        // from the database's own PK constraint, not from in-memory change-tracker state.
        await using var firstDb = _fixture.CreateContext();
        await IdentityTestHelpers.BootstrapAdminAsync(IdentityTestHelpers.CreateAccountService(firstDb));

        await using var secondDb = _fixture.CreateContext();
        var secondService = IdentityTestHelpers.CreateAccountService(secondDb);
        await Assert.ThrowsAsync<BootstrapAlreadyConsumedException>(
            () => secondService.BootstrapFirstAdminAsync($"second-admin-{Guid.NewGuid():N}", "password", IdentityTestHelpers.TestBootstrapSecret, IdentityTestHelpers.TestBootstrapSecret));
    }

    [Fact]
    public async Task Two_concurrent_bootstrap_attempts_result_in_exactly_one_admin_account()
    {
        await using var dbA = _fixture.CreateContext();
        await using var dbB = _fixture.CreateContext();
        var serviceA = IdentityTestHelpers.CreateAccountService(dbA);
        var serviceB = IdentityTestHelpers.CreateAccountService(dbB);

        var usernameA = $"race-admin-a-{Guid.NewGuid():N}";
        var usernameB = $"race-admin-b-{Guid.NewGuid():N}";

        var taskA = serviceA.BootstrapFirstAdminAsync(usernameA, "password", IdentityTestHelpers.TestBootstrapSecret, IdentityTestHelpers.TestBootstrapSecret);
        var taskB = serviceB.BootstrapFirstAdminAsync(usernameB, "password", IdentityTestHelpers.TestBootstrapSecret, IdentityTestHelpers.TestBootstrapSecret);

        var results = await Task.WhenAll(
            taskA.ContinueWith(t => t.IsCompletedSuccessfully),
            taskB.ContinueWith(t => t.IsCompletedSuccessfully));

        Assert.Single(results, r => r); // exactly one bootstrap attempt won the race

        await using var verifyDb = _fixture.CreateContext();
        var adminCount = verifyDb.UserAccounts.Count(u => (u.Username == usernameA || u.Username == usernameB) && u.Role == Role.Admin);
        Assert.Equal(1, adminCount);
    }
}
