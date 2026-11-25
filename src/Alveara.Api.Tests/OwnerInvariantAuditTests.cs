using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Data;
using Alveara.Api.Migrations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-N030-C01 S30 (brief 18.7, R08-3; review R10): an active Practice Owner who breaks the owner invariant is recorded - never repaired - with one audit row whose
/// id is the documented hash of the state; the same state twice adds nothing; a changed state adds one new row and leaves the first byte-identical; a valid owner
/// writes nothing; the migration runs the same statement; <c>practiceOwnerStatus</c> reports none, degraded, then valid.
/// </summary>
public class OwnerInvariantAuditTests : IAsyncLifetime
{
    private const string Password = "Seeded-Password-1";
    private readonly TestDatabaseFixture _fixture = new();
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Alveara", _fixture.ConnectionString);
            b.UseSetting("AuthAttemptRateLimit:PermitLimit", "500");
        });
    }

    public async Task DisposeAsync() { await _factory.DisposeAsync(); await _fixture.DisposeAsync(); }

    private async Task<T> WithDbAsync<T>(Func<AlveraDbContext, Task<T>> work)
    {
        using var scope = _factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AlveraDbContext>());
    }

    /// <summary>An owner seeded directly (a pre-C01 state no route can create any more): (designation, staff profile, account).</summary>
    private Task<(Guid Designation, Guid Staff, Guid Account)> SeedOwnerAsync(Role role) => WithDbAsync(async db =>
    {
        var account = new UserAccount { Id = Guid.NewGuid(), Username = "old.owner", PasswordHash = Pbkdf2PasswordHasher.Hash(Password), Role = role, CreatedAtUtc = DateTimeOffset.UtcNow };
        var staff = new StaffProfile { Id = Guid.NewGuid(), DisplayName = "Dr Old Owner", UserAccountId = account.Id, IsActive = true, CreatedAtUtc = DateTimeOffset.UtcNow };
        var designation = new PracticeOwnerDesignation
        {
            Id = Guid.NewGuid(), StaffProfileId = staff.Id, DesignatedAtUtc = DateTimeOffset.UtcNow, DesignatedByAccountId = account.Id, Method = PracticeOwnerDesignationMethod.UpgradeTransition,
        };
        db.UserAccounts.Add(account);
        db.StaffProfiles.Add(staff);
        await db.SaveChangesAsync();
        db.PracticeOwnerDesignations.Add(designation);
        await db.SaveChangesAsync();
        return (designation.Id, staff.Id, account.Id);
    });

    /// <summary>The documented formula, recomputed independently of the SQL.</summary>
    private static Guid ExpectedId(Guid designation, Guid staff, bool staffActive, Guid account, bool disabled, Role role, AccountKind kind)
    {
        var text = $"{OwnerInvariantAudit.FingerprintPrefix}{designation:N}:{staff:N}:{(staffActive ? 1 : 0)}:{account:N}:{(disabled ? 1 : 0)}:{role}:{kind}";
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(text)).AsSpan(0, 16));
    }

    private Task<int> RunAsync() => WithDbAsync(db => OwnerInvariantAudit.RecordIfNewAsync(db, CancellationToken.None));

    private Task<List<AuditLogEntry>> ViolationsAsync() => WithDbAsync(db =>
        db.AuditLogEntries.AsNoTracking().Where(a => a.EventType == AuditEventTypes.PracticeOwnerInvariantViolation).OrderBy(a => a.TimestampUtc).ToListAsync());

    private async Task<string?> StatusAsync()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { username = "status.reader", password = Password })).StatusCode);
        return (await (await client.GetAsync("/api/auth/permissions")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("practiceOwnerStatus").GetString();
    }

    [Fact]
    public async Task S30_a_non_administrator_owner_is_recorded_once_per_state_and_never_promoted()
    {
        await WithDbAsync(async db =>
        {
            db.UserAccounts.Add(new UserAccount { Id = Guid.NewGuid(), Username = "status.reader", PasswordHash = Pbkdf2PasswordHasher.Hash(Password), Role = Role.Admin, CreatedAtUtc = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync();
        });
        Assert.Equal("none", await StatusAsync());

        var (designation, staff, account) = await SeedOwnerAsync(Role.Dentist);
        Assert.Equal("degraded", await StatusAsync());

        Assert.Equal(1, await RunAsync());
        var first = Assert.Single(await ViolationsAsync());
        Assert.Equal(ExpectedId(designation, staff, true, account, false, Role.Dentist, AccountKind.Staff), first.Id);
        Assert.Equal($"staffProfileId={staff}; failing=notAdministrator; actor=system (upgrade)", first.Details);
        Assert.Equal(staff, first.TargetUserAccountId);
        Assert.Null(first.PerformedByUserAccountId);
        Assert.DoesNotContain("Old Owner", first.Details);
        Assert.Equal(Role.Dentist, await WithDbAsync(db => db.UserAccounts.Where(u => u.Id == account).Select(u => u.Role).SingleAsync()));   // never promoted

        // the same state again: nothing new
        Assert.Equal(0, await RunAsync());
        Assert.Single(await ViolationsAsync());

        // a different violation state: exactly one new row; the first is unchanged
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UserAccounts] SET [Role] = {(int)Role.Billing} WHERE [Id] = {account}"));
        Assert.Equal(1, await RunAsync());
        var both = await ViolationsAsync();
        Assert.Equal(2, both.Count);
        var again = both.Single(r => r.Id == first.Id);
        Assert.Equal((first.Details, first.TimestampUtc, first.TargetUserAccountId, first.EntityType), (again.Details, again.TimestampUtc, again.TargetUserAccountId, again.EntityType));
        Assert.Equal(ExpectedId(designation, staff, true, account, false, Role.Billing, AccountKind.Staff), both.Single(r => r.Id != first.Id).Id);

        // fixed by an Administrator (role changed to Administrator): valid, and the step writes nothing more
        await WithDbAsync(db => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE [UserAccounts] SET [Role] = {(int)Role.Admin} WHERE [Id] = {account}"));
        Assert.Equal("valid", await StatusAsync());
        Assert.Equal(0, await RunAsync());
        Assert.Equal(2, (await ViolationsAsync()).Count);
    }

    [Fact]
    public async Task S30_a_valid_owner_writes_nothing_and_the_migration_runs_the_same_single_statement()
    {
        await SeedOwnerAsync(Role.Admin);
        Assert.Equal(0, await RunAsync());
        Assert.Empty(await ViolationsAsync());

        var builder = new MigrationBuilder("Microsoft.EntityFrameworkCore.SqlServer");
        typeof(RecordPracticeOwnerInvariantViolations).GetMethod("Up", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(new RecordPracticeOwnerInvariantViolations(), [builder]);
        var operation = Assert.Single(builder.Operations);
        Assert.Equal(OwnerInvariantAudit.Sql, Assert.IsType<SqlOperation>(operation).Sql);
    }
}
