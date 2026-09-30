using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
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
    }

    // STORY-002 Trust requirement: audit log entries are immutable. Enforced here, at the single
    // point every write in the process passes through, rather than per-caller — so no future
    // service, script, or admin tool can accidentally (or deliberately) update or delete an
    // existing entry, no matter what permission it holds. A legitimate audit trail can only ever
    // grow by insertion.
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
