using Microsoft.AspNetCore.DataProtection;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// Account fixtures for service-level tests. ALV-N030 (brief 17.4): sign-up and the static-secret bootstrap are gone from the product, so the first administrator
/// and "registered" accounts are written directly by <see cref="TestAccounts"/>; enabling an account and giving it a working role still go through the real
/// <see cref="AccountService"/> operations, exactly as before.
/// </summary>
public static class IdentityTestHelpers
{
    /// <summary>An ephemeral (non-persisted) Data Protection provider — fine for a test process's lifetime.</summary>
    public static AccountService CreateAccountService(AlveraDbContext db) =>
        TestAccounts.Track(new(db, new EphemeralDataProtectionProvider(), PolicyTestSupport.Defaults()), db);

    /// <summary>Same as <see cref="CreateAccountService(AlveraDbContext)"/> but against a caller-supplied
    /// provider - needed whenever two AccountService instances (different DbContexts, e.g. a
    /// concurrency test) must decrypt data protected by each other, since two independently
    /// created EphemeralDataProtectionProvider instances do not share key material (matching
    /// production, where the provider is a singleton shared across all scoped DbContexts).</summary>
    public static AccountService CreateAccountService(AlveraDbContext db, IDataProtectionProvider provider) =>
        TestAccounts.Track(new(db, provider, PolicyTestSupport.Defaults()), db);

    /// <summary>An enabled Administrator for this test's database (the first one also writes BootstrapState and stages the presentation administrator).</summary>
    public static Task<UserAccount> BootstrapAdminAsync(AccountService service, string username = "", string password = "bootstrap-admin-password") =>
        TestAccounts.SeedFirstAdminAsync(TestAccounts.ContextOf(service), string.IsNullOrEmpty(username) ? $"admin-{Guid.NewGuid():N}" : username, password);

    /// <summary>A fresh administrator for this database (each test owns its own database).</summary>
    public static Task<UserAccount> GetOrBootstrapAdminAsync(AccountService service, AlveraDbContext db) =>
        TestAccounts.SeedFirstAdminAsync(db, $"admin-{Guid.NewGuid():N}", "bootstrap-admin-password");

    /// <summary>Writes a "registered" (Unassigned, disabled) account, then uses (or makes) an admin to give it a working role and enable it through the real operations.</summary>
    public static async Task<UserAccount> RegisterEnabledAsync(AccountService service, AlveraDbContext db, string username, string password, Role role, UserAccount? admin = null)
    {
        admin ??= await GetOrBootstrapAdminAsync(service, db);
        var account = await TestAccounts.SeedRegisteredAsync(db, username, password);
        await service.ChangeRoleAsync(account.Id, role, admin.Id);
        return await service.SetAccountEnabledAsync(account.Id, enabled: true, admin.Id);
    }
}
