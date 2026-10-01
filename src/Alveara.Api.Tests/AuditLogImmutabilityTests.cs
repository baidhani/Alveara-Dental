using Alveara.Api.Architecture.Auditing;
using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// STORY-002 Trust requirement: "Audit logs are immutable and protected from unauthorized
/// modification." Proves the guarantee at its actual enforcement point - AlveraDbContext.SaveChanges
/// - rather than only at whatever API surface happens to exist today, since that surface could
/// change without this guarantee being retested.
/// </summary>
public class AuditLogImmutabilityTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<AuditLogEntry> SeedAuditEntryAsync()
    {
        await using var db = _fixture.CreateContext();
        var service = IdentityTestHelpers.CreateAccountService(db);
        var account = await service.RegisterAsync($"user-{Guid.NewGuid():N}", "password");

        return await db.AuditLogEntries
            .Where(a => a.TargetUserAccountId == account.Id && a.EventType == AuditEventTypes.AccountRegistered)
            .OrderByDescending(a => a.TimestampUtc)
            .FirstAsync();
    }

    [Fact]
    public async Task Updating_an_existing_audit_log_entry_is_rejected()
    {
        var seeded = await SeedAuditEntryAsync();

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(a => a.Id == seeded.Id);
        entry.Details = "tampered";

        await Assert.ThrowsAsync<AuditLogImmutableException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Deleting_an_existing_audit_log_entry_is_rejected()
    {
        var seeded = await SeedAuditEntryAsync();

        await using var db = _fixture.CreateContext();
        var entry = await db.AuditLogEntries.SingleAsync(a => a.Id == seeded.Id);
        db.AuditLogEntries.Remove(entry);

        await Assert.ThrowsAsync<AuditLogImmutableException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task A_rejected_mutation_leaves_the_stored_entry_completely_unchanged()
    {
        var seeded = await SeedAuditEntryAsync();

        await using (var db = _fixture.CreateContext())
        {
            var entry = await db.AuditLogEntries.SingleAsync(a => a.Id == seeded.Id);
            entry.Details = "tampered";
            await Assert.ThrowsAsync<AuditLogImmutableException>(() => db.SaveChangesAsync());
        }

        // A fresh context/query - not the same tracked instance - confirms nothing actually
        // persisted, not just that an exception happened to be thrown.
        await using var verify = _fixture.CreateContext();
        var stored = await verify.AuditLogEntries.SingleAsync(a => a.Id == seeded.Id);
        Assert.Equal(seeded.Details, stored.Details);
    }

    [Fact]
    public async Task Inserting_a_new_audit_log_entry_still_succeeds()
    {
        // The guard must reject Modified/Deleted only - an ordinary Added entry (how every
        // legitimate audit write happens) must be unaffected.
        await using var db = _fixture.CreateContext();
        db.AuditLogEntries.Add(new AuditLogEntry
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventTypes.AccountRegistered,
            TargetUserAccountId = Guid.NewGuid(),
            Details = "test insert",
            TimestampUtc = DateTimeOffset.UtcNow,
        });

        var affected = await db.SaveChangesAsync();

        Assert.Equal(1, affected);
    }

    // ALV-002-C01 (preflight-rejection coverage, retained as-is per the R02 review): proves the
    // *immutability guard itself* participates in the same atomic SaveChanges call as a business
    // change. This does NOT exercise a genuine audit-table INSERT failure (the guard throws before
    // base.SaveChangesAsync ever runs) - see the next test for that.
    [Fact]
    public async Task A_business_change_cannot_commit_if_its_coupled_audit_write_is_invalid()
    {
        var seededAudit = await SeedAuditEntryAsync();
        var accountId = seededAudit.TargetUserAccountId;

        await using var db = _fixture.CreateContext();
        var account = await db.UserAccounts.SingleAsync(a => a.Id == accountId);
        var originalTimeout = account.SessionTimeoutMinutes;
        account.SessionTimeoutMinutes = originalTimeout + 100; // the business change

        var existingEntry = await db.AuditLogEntries.SingleAsync(a => a.Id == seededAudit.Id);
        existingEntry.Details = "tampered"; // stands in for "the required audit write is invalid"

        await Assert.ThrowsAsync<AuditLogImmutableException>(() => db.SaveChangesAsync());

        await using var verify = _fixture.CreateContext();
        var reloadedAccount = await verify.UserAccounts.SingleAsync(a => a.Id == accountId);
        Assert.Equal(originalTimeout, reloadedAccount.SessionTimeoutMinutes);
    }

    // ALV-002-C01 R02 (review finding ALV-002-C01-R01-03): the test above only proves the preflight
    // guard rejects a save before any INSERT happens. This test forces a GENUINE database-level
    // failure on the audit INSERT itself - a real SQL Server primary-key violation, not the
    // in-process guard - and proves the policy still holds: "a consequential action must not
    // silently succeed without its required audit trail" means a business change and its *new*
    // required audit event (staged through the actual shared AuditService.Record path) live or die
    // together even when the failure is a genuine storage-layer rejection, not an application check.
    [Fact]
    public async Task A_business_change_and_its_new_required_audit_event_are_both_rolled_back_when_the_audit_INSERT_genuinely_fails()
    {
        var seededAudit = await SeedAuditEntryAsync();
        var accountId = seededAudit.TargetUserAccountId;

        await using var db = _fixture.CreateContext();
        var account = await db.UserAccounts.SingleAsync(a => a.Id == accountId);
        var originalTimeout = account.SessionTimeoutMinutes;
        account.SessionTimeoutMinutes = originalTimeout + 50; // the business change

        // The required audit event, staged through the real shared path - not a hand-built entity.
        AuditService.Record(db, AuditEventTypes.SessionTimeoutChanged, "UserAccount", accountId, accountId, "test");

        // Force a genuine SQL Server primary-key violation on exactly that INSERT by colliding its
        // Id with an already-committed row. This entry is in the Added state (not Modified/Deleted),
        // so the immutability guard above does NOT intercept it - the failure that follows comes
        // from the real database constraint, not the in-process preflight check.
        var newAuditEntry = db.ChangeTracker.Entries<AuditLogEntry>().Single(e => e.State == EntityState.Added).Entity;
        newAuditEntry.Id = seededAudit.Id;

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        await using var verify = _fixture.CreateContext();
        var reloadedAccount = await verify.UserAccounts.SingleAsync(a => a.Id == accountId);
        Assert.Equal(originalTimeout, reloadedAccount.SessionTimeoutMinutes);

        var newEventCount = await verify.AuditLogEntries
            .CountAsync(a => a.TargetUserAccountId == accountId && a.EventType == AuditEventTypes.SessionTimeoutChanged);
        Assert.Equal(0, newEventCount); // the required audit event never persisted either
    }
}
