using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Idempotency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;

namespace Alveara.Api.Data;

/// <summary>
/// The single EF Core DbContext for Alveara. Per ALV-N002's server-owned-database-topology rule,
/// this type is only ever constructed inside the API process — LAN/browser clients have no
/// connection string and no direct database access; they only ever call the HTTP API.
/// </summary>
public class AlveraDbContext(DbContextOptions<AlveraDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<StaffProfile> StaffProfiles => Set<StaffProfile>();
    public DbSet<ProviderProfile> ProviderProfiles => Set<ProviderProfile>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<MfaRecoveryCode> MfaRecoveryCodes => Set<MfaRecoveryCode>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<BootstrapState> BootstrapStates => Set<BootstrapState>();
    public DbSet<MfaChallenge> MfaChallenges => Set<MfaChallenge>();

    public DbSet<BackgroundJob> BackgroundJobs => Set<BackgroundJob>();
    public DbSet<BackgroundJobEffectReceipt> BackgroundJobEffectReceipts => Set<BackgroundJobEffectReceipt>();
    public DbSet<MeasurementEvent> MeasurementEvents => Set<MeasurementEvent>();
    public DbSet<IdempotencyReceipt> IdempotencyReceipts => Set<IdempotencyReceipt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserAccount>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
        });

        modelBuilder.Entity<StaffProfile>(e =>
        {
            e.HasOne(s => s.UserAccount)
                .WithMany()
                .HasForeignKey(s => s.UserAccountId)
                .OnDelete(DeleteBehavior.SetNull);
            // ALV-002-C01: SQL Server rowversion - the database, not application code, maintains
            // this on every UPDATE, and EF Core includes it in the WHERE clause of any UPDATE it
            // generates, so a save against a stale-read row affects zero rows and EF raises
            // DbUpdateConcurrencyException (translated by ConcurrencySaveGuard). See
            // StaffProfile.RowVersion's own doc comment for why StaffProfile, not UserAccount, is
            // this story's representative record.
            e.Property(s => s.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<ProviderProfile>(e =>
        {
            e.HasOne(p => p.StaffProfile)
                .WithMany()
                .HasForeignKey(p => p.StaffProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BackgroundJob>(e =>
        {
            e.HasIndex(j => j.IdempotencyKey).IsUnique();
        });

        modelBuilder.Entity<BackgroundJobEffectReceipt>(e =>
        {
            e.HasKey(r => r.JobId);
        });

        modelBuilder.Entity<MfaRecoveryCode>(e =>
        {
            e.HasIndex(r => r.UserAccountId);
        });

        modelBuilder.Entity<PasswordResetToken>(e =>
        {
            e.HasIndex(t => t.UserAccountId);
        });

        modelBuilder.Entity<MfaChallenge>(e =>
        {
            e.HasIndex(c => c.UserAccountId);
        });

        modelBuilder.Entity<IdempotencyReceipt>(e =>
        {
            // ALV-002-C01: the real duplicate-command guarantee - a database-enforced unique
            // constraint, not an application-level check-then-act race (see IdempotencyGuard).
            e.HasIndex(r => new { r.CommandType, r.IdempotencyKey }).IsUnique();
        });
    }

    // STORY-002 Trust requirement (generalized by ALV-002-C01's shared AuditService, which writes
    // through this same table): audit log entries are immutable. Enforced here against every
    // CHANGE-TRACKED SaveChanges/SaveChangesAsync call - which is how every ordinary caller writes
    // (AuditService.Record, AccountService, and every browser-facing API path never have direct
    // database access per ALV-N002's server-owned-database-topology rule) - so no future service,
    // script, or admin tool using the normal EF Core change tracker can accidentally (or
    // deliberately) update or delete an existing entry, no matter what permission it holds. This
    // does NOT cover EF Core bulk operations (ExecuteUpdateAsync/ExecuteDeleteAsync) or raw SQL
    // issued directly against this table, which bypass the change tracker entirely - no code path
    // in this codebase does either against AuditLogEntries today, and adding one would need its own
    // explicit safeguard, not an assumption that this guard already covers it.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ThrowIfAuditLogEntryMutationAttempted();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ThrowIfAuditLogEntryMutationAttempted();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ThrowIfAuditLogEntryMutationAttempted()
    {
        var attemptedMutation = ChangeTracker.Entries<AuditLogEntry>()
            .Any(e => e.State is EntityState.Modified or EntityState.Deleted);
        if (attemptedMutation)
        {
            throw new AuditLogImmutableException();
        }
    }
}
