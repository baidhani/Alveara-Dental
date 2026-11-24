using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030 slice 5b (brief 17.4): backend tests create accounts DIRECTLY in their test database. The product no longer has sign-up or the static-secret
/// first-admin bootstrap, so these fixtures write the same rows those routes used to write, and nothing else:
/// <list type="bullet">
/// <item><see cref="SeedFirstAdminAsync"/>: an enabled Administrator (a legacy, profile-less account of kind Staff). The first one on a database also writes the
/// <see cref="BootstrapState"/> singleton and stages the sign-in presentation administrator, as the old bootstrap did (ALV-N020-C01 tests rely on that).</item>
/// <item><see cref="SeedRegisteredAsync"/>: a disabled <see cref="Role.Unassigned"/> account, as old sign-up made it, for tests whose subject is the administrator's
/// enable and role-change flow that follows.</item>
/// </list>
/// The <c>...ResponseAsync</c> forms return an HTTP-shaped answer (201 with <c>{ id, username, role }</c>, or 409 for a taken username), so a test that read the old
/// route's reply keeps reading it the same way. This is fixture setup, never a product path.
/// </summary>
public static class TestAccounts
{
    public static async Task<UserAccount> SeedFirstAdminAsync(AlveraDbContext db, string username, string password, CancellationToken ct = default)
    {
        username = AccountService.NormalizeUsername(username);
        if (username.Length == 0) throw new ArgumentException("A fixture account needs a username.", nameof(username));
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(password), Role = Role.Admin, IsDisabled = false, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        // ALV-N030 slice 7: every test database already carries the setup-completed mark (TestDatabaseFixture), so "first" means: no sign-in presentation
        // administrator has been staged yet, as the old bootstrap staged one for the first administrator only.
        var first = !await db.AccountPermissionGrants.AnyAsync(g => g.Permission == nameof(AccountPermission.ManageLoginPresentation), ct);
        if (!await db.BootstrapStates.AnyAsync(ct)) db.BootstrapStates.Add(new BootstrapState { ConsumedAtUtc = DateTimeOffset.UtcNow, ConsumedForUsername = username });
        db.UserAccounts.Add(account);
        AuditService.Record(db, AuditEventTypes.AccountRegistered, "UserAccount", account.Id, null, $"Test fixture: administrator '{username}' created.");
        if (first) AccountPermissionService.StageBootstrapHolder(db, account.Id, username);
        await SaveAsync(db, username, ct);
        return account;
    }

    public static async Task<UserAccount> SeedRegisteredAsync(AlveraDbContext db, string username, string password, CancellationToken ct = default)
    {
        username = AccountService.NormalizeUsername(username);
        if (username.Length == 0) throw new ArgumentException("A fixture account needs a username.", nameof(username));
        var account = new UserAccount
        {
            Id = Guid.NewGuid(), Username = username, PasswordHash = Pbkdf2PasswordHasher.Hash(password), Role = Role.Unassigned, IsDisabled = true, CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.UserAccounts.Add(account);
        AuditService.Record(db, AuditEventTypes.AccountRegistered, "UserAccount", account.Id, null, $"Test fixture: account '{username}' created (Unassigned, disabled).");
        await SaveAsync(db, username, ct);
        return account;
    }

    /// <summary>The old bootstrap route's reply: the first administrator of a database is also told it is the sign-in presentation administrator.</summary>
    public const string PresentationAdministratorNotice =
        "This administrator account is also the sign-in presentation administrator (ManageLoginPresentation): it can change the sign-in screen background and footer notice.";

    public static Task<HttpResponseMessage> SeedFirstAdminResponseAsync(WebApplicationFactory<Program> factory, string username, string password) =>
        RespondAsync(factory, db => SeedFirstAdminAsync(db, username, password), async (db, account) =>
        {
            var presentation = await db.AccountPermissionGrants.AnyAsync(g => g.UserAccountId == account.Id && g.Permission == nameof(AccountPermission.ManageLoginPresentation));
            return presentation
                ? new { id = account.Id, username = account.Username, role = account.Role.ToString(), presentationAdministrator = true, notice = PresentationAdministratorNotice }
                : (object)new { id = account.Id, username = account.Username, role = account.Role.ToString(), presentationAdministrator = false };
        });

    public static Task<HttpResponseMessage> SeedRegisteredResponseAsync(WebApplicationFactory<Program> factory, string username, string password) =>
        RespondAsync(factory, db => SeedRegisteredAsync(db, username, password),
            (_, account) => Task.FromResult<object>(new { id = account.Id, username = account.Username, role = account.Role.ToString() }));

    private static async Task<HttpResponseMessage> RespondAsync(WebApplicationFactory<Program> factory, Func<AlveraDbContext, Task<UserAccount>> seed,
        Func<AlveraDbContext, UserAccount, Task<object>> body)
    {
        using var scope = factory.Services.CreateScope();
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<AlveraDbContext>();
            var account = await seed(db);
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(await body(db, account)) };
        }
        catch (FixtureUsernameTakenException)
        {
            return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { error = "username_taken" }) };
        }
    }

    private static async Task SaveAsync(AlveraDbContext db, string username, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2627 or 2601 })
        {
            db.ChangeTracker.Clear();
            throw new FixtureUsernameTakenException(username);
        }
    }

    // ---------- service-level tests: the DbContext an AccountService was made with ----------

    private static readonly ConditionalWeakTable<AccountService, AlveraDbContext> Contexts = new();

    /// <summary>Remembers the context a test's AccountService works on, so the fixtures below write to the same database and transaction scope.</summary>
    public static AccountService Track(AccountService service, AlveraDbContext db)
    {
        Contexts.AddOrUpdate(service, db);
        return service;
    }

    internal static AlveraDbContext ContextOf(AccountService service) =>
        Contexts.TryGetValue(service, out var db) ? db
            : throw new InvalidOperationException("Create the AccountService with IdentityTestHelpers.CreateAccountService (or TestAccounts.Track) so fixtures know its database.");

    /// <summary>The old sign-up's result (Unassigned, disabled), written directly. Replaces the removed <c>AccountService.RegisterAsync</c> in fixtures.</summary>
    public static Task<UserAccount> RegisterAsync(this AccountService service, string username, string password, CancellationToken ct = default) =>
        SeedRegisteredAsync(ContextOf(service), username, password, ct);
}

/// <summary>A fixture asked for a username that the test database already holds.</summary>
public sealed class FixtureUsernameTakenException(string username) : Exception($"The test database already has an account named '{username}'.");
