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
    }
}
