using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-001-C01: since self-registration can no longer grant any role (STORY-001's original
/// tests registered directly into a chosen role, which is exactly the insecure behavior this
/// story removes), tests that need a privileged account go through the same one-time bootstrap
/// path a real deployment would use.
/// </summary>
public static class IdentityTestHelpers
{
    public const string TestBootstrapSecret = "test-bootstrap-secret-for-unit-tests-only";

    /// <summary>An ephemeral (non-persisted) Data Protection provider — fine for a test process's lifetime.</summary>
    public static AccountService CreateAccountService(AlveraDbContext db) =>
        new(db, new EphemeralDataProtectionProvider());

    /// <summary>Same as <see cref="CreateAccountService(AlveraDbContext)"/> but against a caller-supplied
    /// provider - needed whenever two AccountService instances (different DbContexts, e.g. a
    /// concurrency test) must decrypt data protected by each other, since two independently
    /// created EphemeralDataProtectionProvider instances do not share key material (matching
    /// production, where the provider is a singleton shared across all scoped DbContexts).</summary>
    public static AccountService CreateAccountService(AlveraDbContext db, IDataProtectionProvider provider) =>
        new(db, provider);

    public static Task<UserAccount> BootstrapAdminAsync(AccountService service, string username = "", string password = "bootstrap-admin-password") =>
        service.BootstrapFirstAdminAsync(
            string.IsNullOrEmpty(username) ? $"admin-{Guid.NewGuid():N}" : username,
            password,
            TestBootstrapSecret,
            TestBootstrapSecret);

    /// <summary>Bootstraps a fresh admin for this database (each test owns its own LocalDB
    /// database, so bootstrap - which is one-time per database - is always safe to call here).</summary>
    public static Task<UserAccount> GetOrBootstrapAdminAsync(AccountService service, AlveraDbContext db) =>
        BootstrapAdminAsync(service);

    /// <summary>Registers a self-service account, then uses (or bootstraps) an admin to enable it
    /// and assign a working role - the only path by which a login-capable account now exists.</summary>
    public static async Task<UserAccount> RegisterEnabledAsync(AccountService service, AlveraDbContext db, string username, string password, Role role, UserAccount? admin = null)
    {
        admin ??= await GetOrBootstrapAdminAsync(service, db);
        var account = await service.RegisterAsync(username, password);
        await service.ChangeRoleAsync(account.Id, role, admin.Id);
        return await service.SetAccountEnabledAsync(account.Id, enabled: true, admin.Id);
    }
}
