using Alveara.Api.Architecture.Identity;
using Xunit;

namespace Alveara.Api.Tests;

public class TransactionRollbackTests : IClassFixture<TestDatabaseFixture>
{
    private readonly TestDatabaseFixture _fixture;

    public TransactionRollbackTests(TestDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task A_failed_multi_row_transaction_leaves_no_partial_writes()
    {
        await using var db = _fixture.CreateContext();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var staff = new StaffProfile
        {
            Id = Guid.NewGuid(),
            DisplayName = "Rollback Test Staff",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
        db.StaffProfiles.Add(staff);
        await db.SaveChangesAsync();

        // Intentionally reference a non-existent StaffProfileId to force a foreign-key violation
        // partway through the transaction.
        db.ProviderProfiles.Add(new ProviderProfile
        {
            Id = Guid.NewGuid(),
            StaffProfileId = Guid.NewGuid(), // does not exist
            Specialty = "General",
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });

        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await db.SaveChangesAsync();
        });

        await transaction.RollbackAsync();

        await using var verifyDb = _fixture.CreateContext();
        var staffExists = await verifyDb.StaffProfiles.FindAsync(staff.Id);
        Assert.Null(staffExists); // the "successful" first insert was rolled back too
    }

    [Fact]
    public async Task A_committed_transaction_persists_all_its_writes()
    {
        var staffId = Guid.NewGuid();

        await using (var db = _fixture.CreateContext())
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.StaffProfiles.Add(new StaffProfile
            {
                Id = staffId,
                DisplayName = "Committed Staff",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var verifyDb = _fixture.CreateContext();
        var staff = await verifyDb.StaffProfiles.FindAsync(staffId);
        Assert.NotNull(staff);
    }
}
