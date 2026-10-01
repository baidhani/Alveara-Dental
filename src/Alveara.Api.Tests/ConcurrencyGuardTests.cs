using Alveara.Api.Architecture.Concurrency;
using Alveara.Api.Architecture.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Alveara.Api.Tests;

/// <summary>
/// ALV-002-C01: optimistic-concurrency/stale-edit rejection, demonstrated against StaffProfile -
/// the representative currently-existing mutable record. UserAccount was tried first but rejected
/// (see StaffProfile.RowVersion's own doc comment): it already has its own deliberate concurrency
/// design built on atomic ExecuteUpdateAsync bulk updates for the lockout counter and login-success
/// reset, which bypass the change tracker and would make a generic RowVersion column go stale on
/// any already-tracked entity, spuriously rejecting legitimate sequential (non-concurrent) updates.
/// StaffProfile has no competing bulk-update path, so it demonstrates the primitive cleanly.
/// </summary>
public class ConcurrencyGuardTests : IAsyncLifetime
{
    private readonly TestDatabaseFixture _fixture = new();

    public Task InitializeAsync() => _fixture.InitializeAsync();
    public Task DisposeAsync() => _fixture.DisposeAsync();

    private async Task<Guid> SeedStaffProfileAsync()
    {
        await using var db = _fixture.CreateContext();
        var staff = new StaffProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = "Original Name",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.StaffProfiles.Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    [Fact]
    public async Task Saving_a_stale_read_of_the_same_staff_profile_is_rejected_as_a_concurrency_conflict()
    {
        var staffId = await SeedStaffProfileAsync();

        // Two independent contexts read the same row - simulates two concurrent callers, each
        // holding what they believe is the current version.
        await using var contextA = _fixture.CreateContext();
        await using var contextB = _fixture.CreateContext();
        var staffAsReadByA = await contextA.StaffProfiles.SingleAsync(s => s.Id == staffId);
        var staffAsReadByB = await contextB.StaffProfiles.SingleAsync(s => s.Id == staffId);

        // A saves first and succeeds, genuinely changing the row (and its RowVersion).
        staffAsReadByA.DisplayName = "Changed By A";
        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(contextA, "StaffProfile", staffId);

        // B still holds the pre-A RowVersion. Its own update targets a row that no longer matches
        // what it read, so it must be rejected - not silently overwrite A's change.
        staffAsReadByB.DisplayName = "Changed By B";
        var ex = await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => ConcurrencySaveGuard.SaveOrThrowConflictAsync(contextB, "StaffProfile", staffId));

        Assert.Equal("StaffProfile", ex.EntityType);
        Assert.Equal(staffId, ex.EntityId);

        // A's change is what's actually stored - B's stale write never took effect.
        await using var verify = _fixture.CreateContext();
        var stored = await verify.StaffProfiles.SingleAsync(s => s.Id == staffId);
        Assert.Equal("Changed By A", stored.DisplayName);
    }

    [Fact]
    public void ToProblem_returns_a_stable_conflict_shape_usable_by_an_API_or_UI()
    {
        var ex = new ConcurrencyConflictException("StaffProfile", Guid.Empty);
        var problem = ex.ToProblem();
        Assert.Equal("concurrency_conflict", problem.Error);
        Assert.Equal("StaffProfile", problem.EntityType);
        Assert.Equal(Guid.Empty, problem.EntityId);
    }

    [Fact]
    public async Task Saving_the_current_version_succeeds_normally()
    {
        var staffId = await SeedStaffProfileAsync();

        await using var db = _fixture.CreateContext();
        var staff = await db.StaffProfiles.SingleAsync(s => s.Id == staffId);
        staff.DisplayName = "Updated Name";

        await ConcurrencySaveGuard.SaveOrThrowConflictAsync(db, "StaffProfile", staffId);

        await using var verify = _fixture.CreateContext();
        var stored = await verify.StaffProfiles.SingleAsync(s => s.Id == staffId);
        Assert.Equal("Updated Name", stored.DisplayName);
    }
}
