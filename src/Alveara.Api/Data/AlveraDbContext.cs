using Microsoft.EntityFrameworkCore;
using Alveara.Api.Architecture.BackgroundWork;
using Alveara.Api.Architecture.Backup;
using Alveara.Api.Architecture.Configuration;
using Alveara.Api.Architecture.Forms;
using Alveara.Api.Architecture.Idempotency;
using Alveara.Api.Architecture.Identity;
using Alveara.Api.Architecture.Measurement;
using Alveara.Api.Architecture.Patients;
using Alveara.Api.Architecture.Scheduling;

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

    // ALV-N003: practice/scheduling configuration.
    public DbSet<PracticeSettings> PracticeSettings => Set<PracticeSettings>();
    public DbSet<PracticeLocation> PracticeLocations => Set<PracticeLocation>();
    public DbSet<Operatory> Operatories => Set<Operatory>();
    public DbSet<AppointmentType> AppointmentTypes => Set<AppointmentType>();
    public DbSet<ProviderWeeklyAvailability> ProviderWeeklyAvailabilities => Set<ProviderWeeklyAvailability>();
    public DbSet<ProviderBlockedTime> ProviderBlockedTimes => Set<ProviderBlockedTime>();

    // ALV-N004: backup and recovery.
    public DbSet<BackupSettings> BackupSettings => Set<BackupSettings>();
    public DbSet<BackupRecord> BackupRecords => Set<BackupRecord>();
    public DbSet<RestoreDrillRecord> RestoreDrills => Set<RestoreDrillRecord>();
    public DbSet<BackupNotificationRecord> BackupNotifications => Set<BackupNotificationRecord>();
    public DbSet<DeploymentInvariantRecord> DeploymentInvariants => Set<DeploymentInvariantRecord>();

    // STORY-003: patient registration.
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<PatientHistoryEntry> PatientHistory => Set<PatientHistoryEntry>();
    public DbSet<PatientRegistrationSettings> PatientRegistrationSettings => Set<PatientRegistrationSettings>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentEvent> AppointmentEvents => Set<AppointmentEvent>();
    public DbSet<FormTemplate> FormTemplates => Set<FormTemplate>();
    public DbSet<FormTemplateVersion> FormTemplateVersions => Set<FormTemplateVersion>();
    public DbSet<PatientForm> PatientForms => Set<PatientForm>();
    public DbSet<SignedFormSnapshot> SignedFormSnapshots => Set<SignedFormSnapshot>();
    public DbSet<PatientFormEvent> PatientFormEvents => Set<PatientFormEvent>();

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

            // ALV-N003: a staff profile is inactivated, never deleted. Names are unique (a small
            // practice disambiguates, and it makes create idempotent), and a login account links to
            // at most one staff profile.
            e.Property(s => s.IsActive).HasDefaultValue(true);
            e.Property(s => s.DisplayName).HasMaxLength(120);
            e.Property(s => s.JobTitle).HasMaxLength(80);
            e.HasIndex(s => s.DisplayName).IsUnique();
            e.HasIndex(s => s.UserAccountId).IsUnique().HasFilter("[UserAccountId] IS NOT NULL");
        });

        modelBuilder.Entity<ProviderProfile>(e =>
        {
            e.HasOne(p => p.StaffProfile)
                .WithMany()
                .HasForeignKey(p => p.StaffProfileId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Property(p => p.IsActive).HasDefaultValue(true);
            e.Property(p => p.Specialty).HasMaxLength(80);
            e.Property(p => p.RowVersion).IsRowVersion();
            e.Property(p => p.AvailabilityRevision).IsConcurrencyToken().HasDefaultValue(0);
            e.HasIndex(p => p.StaffProfileId).IsUnique(); // one provider profile per staff member
        });

        // ALV-N003 configuration. Every foreign key into configuration is Restrict: referenced
        // configuration can only be inactivated, never destructively deleted out from under history.
        modelBuilder.Entity<PracticeSettings>(e =>
        {
            e.Property(s => s.Name).HasMaxLength(120);
            e.Property(s => s.Phone).HasMaxLength(40);
            e.Property(s => s.AddressLine).HasMaxLength(200);
            e.Property(s => s.RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<PracticeLocation>(e =>
        {
            e.Property(l => l.Name).HasMaxLength(120);
            e.Property(l => l.RowVersion).IsRowVersion();
            e.HasIndex(l => l.Name).IsUnique();
            // First release supports exactly one ACTIVE location: a unique index over IsActive,
            // filtered to active rows, makes a second active location impossible at the database
            // even if the service's friendly check were bypassed or raced.
            e.HasIndex(l => l.IsActive).IsUnique().HasFilter("[IsActive] = 1").HasDatabaseName("UX_PracticeLocations_SingleActive");
        });

        modelBuilder.Entity<Operatory>(e =>
        {
            e.Property(o => o.Name).HasMaxLength(120);
            e.Property(o => o.RowVersion).IsRowVersion();
            e.HasOne(o => o.Location).WithMany().HasForeignKey(o => o.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(o => new { o.LocationId, o.Name }).IsUnique();
        });

        modelBuilder.Entity<AppointmentType>(e =>
        {
            e.Property(t => t.Name).HasMaxLength(120);
            e.Property(t => t.RowVersion).IsRowVersion();
            e.HasIndex(t => t.Name).IsUnique();
        });

        modelBuilder.Entity<ProviderWeeklyAvailability>(e =>
        {
            e.HasOne(a => a.ProviderProfile).WithMany().HasForeignKey(a => a.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.ProviderProfileId, a.DayOfWeek });
        });

        modelBuilder.Entity<ProviderBlockedTime>(e =>
        {
            e.Property(b => b.Reason).HasMaxLength(200);
            e.HasOne(b => b.ProviderProfile).WithMany().HasForeignKey(b => b.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(b => new { b.ProviderProfileId, b.StartUtc });
        });

        // ALV-N004: backup history is append-mostly and never deleted by the application (a purged
        // backup keeps its row, status Purged). Statuses are stored as strings so history stays readable.
        modelBuilder.Entity<BackupSettings>(e =>
        {
            e.Property(s => s.RowVersion).IsRowVersion();
            e.Property(s => s.DestinationDirectory).HasMaxLength(500);
            e.Property(s => s.RecoveryKeyFingerprint).HasMaxLength(64);
        });

        modelBuilder.Entity<BackupRecord>(e =>
        {
            e.Property(r => r.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(r => r.VerificationStatus).HasConversion<string>().HasMaxLength(30);
            e.Property(r => r.IdempotencyKey).HasMaxLength(200);
            e.Property(r => r.FileName).HasMaxLength(200);
            e.Property(r => r.DestinationDirectory).HasMaxLength(500);
            e.Property(r => r.Sha256).HasMaxLength(64);
            e.Property(r => r.IncludedAssetClasses).HasMaxLength(200);
            e.Property(r => r.SchemaMigration).HasMaxLength(200);
            e.Property(r => r.AppVersion).HasMaxLength(100);
            e.Property(r => r.RecoveryKeyFingerprint).HasMaxLength(64);
            e.Property(r => r.FailureCode).HasMaxLength(60);
            e.Property(r => r.FailureMessage).HasMaxLength(500);
            e.Property(r => r.VerificationFailureCode).HasMaxLength(60);
            e.HasIndex(r => r.IdempotencyKey).IsUnique(); // the same slot/job can never create two backups
            e.HasIndex(r => r.StartedAtUtc);
        });

        modelBuilder.Entity<RestoreDrillRecord>(e =>
        {
            e.Property(d => d.Outcome).HasMaxLength(20);
            e.Property(d => d.FailureCode).HasMaxLength(60);
            e.Property(d => d.FailureMessage).HasMaxLength(500);
            e.Property(d => d.TargetDatabase).HasMaxLength(100);
            e.Property(d => d.TargetDirectory).HasMaxLength(500);
            e.Property(d => d.SourceKind).HasMaxLength(20).HasDefaultValue("History");
            e.Property(d => d.ArchiveFileName).HasMaxLength(200);
            e.Property(d => d.ArchiveSha256).HasMaxLength(64);
            e.HasOne<BackupRecord>().WithMany().HasForeignKey(d => d.BackupRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.StartedAtUtc);
        });

        modelBuilder.Entity<DeploymentInvariantRecord>(e =>
        {
            e.Property(d => d.PracticeTimeZoneId).HasMaxLength(100);
            e.Property(d => d.DataProtectionApplicationName).HasMaxLength(500);
        });

        modelBuilder.Entity<BackupNotificationRecord>(e =>
        {
            e.Property(n => n.Kind).HasMaxLength(60);
            e.Property(n => n.Message).HasMaxLength(1000);
            e.Property(n => n.Delivery).HasConversion<string>().HasMaxLength(20);
            e.Property(n => n.DeliveryFailureCode).HasMaxLength(60);
            e.HasIndex(n => n.CreatedAtUtc);
        });

        // STORY-003: patients are registered, never deleted by the application. The unique DuplicateKey
        // is the database-level guarantee behind "duplicate entry" and concurrent double-registration.
        modelBuilder.Entity<Patient>(e =>
        {
            e.Property(p => p.FirstName).HasMaxLength(80);
            e.Property(p => p.MiddleName).HasMaxLength(80);
            e.Property(p => p.LastName).HasMaxLength(80);
            e.Property(p => p.Sex).HasMaxLength(30);
            e.Property(p => p.Phone).HasMaxLength(40);
            e.Property(p => p.Email).HasMaxLength(200);
            e.Property(p => p.AddressLine1).HasMaxLength(200);
            e.Property(p => p.AddressLine2).HasMaxLength(200);
            e.Property(p => p.City).HasMaxLength(100);
            e.Property(p => p.State).HasMaxLength(50);
            e.Property(p => p.PostalCode).HasMaxLength(20);
            e.Property(p => p.DuplicateKey).HasMaxLength(260);
            e.Property(p => p.RowVersion).IsRowVersion();
            e.Property(p => p.RegistrationKey).HasMaxLength(100);
            e.HasIndex(p => p.DuplicateKey).IsUnique();
            e.HasIndex(p => p.RegistrationKey).IsUnique().HasFilter("[RegistrationKey] IS NOT NULL");
            e.HasIndex(p => p.LastName);
            // ALV-003-C01: relationships are Restrict - a household or guarantor can never be deleted out from under a patient.
            e.Property(p => p.IsActive).HasDefaultValue(true);
            e.Property(p => p.HouseholdRelationship).HasMaxLength(30);
            e.HasOne(p => p.Household).WithMany().HasForeignKey(p => p.HouseholdId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Guarantor).WithMany().HasForeignKey(p => p.GuarantorPatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.HouseholdId);
            e.HasIndex(p => p.GuarantorPatientId);
            e.HasIndex(p => p.DateOfBirth);
        });

        modelBuilder.Entity<PatientRegistrationSettings>(e =>
        {
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.Singleton).IsUnique();
        });

        modelBuilder.Entity<PatientHistoryEntry>(e =>
        {
            e.Property(h => h.ChangeType).HasMaxLength(40);
            e.Property(h => h.FieldName).HasMaxLength(40);
            e.Property(h => h.OldValue).HasMaxLength(400);
            e.Property(h => h.NewValue).HasMaxLength(400);
            e.HasOne<Patient>().WithMany().HasForeignKey(h => h.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(h => new { h.PatientId, h.ChangedAtUtc });
        });

        // STORY-004: appointments. Every relationship is Restrict (a provider, operatory, type or patient with appointments can
        // never be deleted out from under them). The period indexes serve the overlap checks; the unique ScheduleKey makes a
        // retried request unable to book twice; the check constraint is a last line of defence against an inverted period.
        modelBuilder.Entity<Appointment>(e =>
        {
            e.Property(a => a.Status).HasMaxLength(20);
            e.Property(a => a.ScheduleKey).HasMaxLength(100);
            e.Property(a => a.RowVersion).IsRowVersion();
            e.HasOne<Patient>().WithMany().HasForeignKey(a => a.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ProviderProfile>().WithMany().HasForeignKey(a => a.ProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Operatory>().WithMany().HasForeignKey(a => a.OperatoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppointmentType>().WithMany().HasForeignKey(a => a.AppointmentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.ProviderProfileId, a.StartUtc });
            e.HasIndex(a => new { a.OperatoryId, a.StartUtc });
            e.HasIndex(a => new { a.PatientId, a.StartUtc });
            e.HasIndex(a => a.ScheduleKey).IsUnique().HasFilter("[ScheduleKey] IS NOT NULL");
            e.ToTable(t => t.HasCheckConstraint("CK_Appointments_Period", "[EndUtc] > [StartUtc]"));
            // ALV-004-C01
            e.Property(a => a.Notes).HasMaxLength(1000);
            e.Property(a => a.CancelReason).HasMaxLength(400);
            e.HasIndex(a => new { a.Status, a.StartUtc });
            // STORY-011: patient flow. The database refuses an unknown flow state, and refuses any flow beyond Scheduled on an appointment
            // that is Cancelled or NoShow (last line of defence behind the service's rule).
            e.Property(a => a.FlowState).HasMaxLength(20).HasDefaultValue(PatientFlowStates.Scheduled);
            e.ToTable(t =>
            {
                // ALV-011-C01 widened this from STORY-011's four states to the full visit chain (the original four keep their values)
                // Case-sensitive on purpose: the database's default collation ignores case, which would let 'completed' in - a value the application treats as unknown.
                t.HasCheckConstraint("CK_Appointments_FlowState",
                    "[FlowState] COLLATE Latin1_General_CS_AS IN ('Scheduled','Confirmed','CheckedIn','Ready','Seated','InTreatment','CheckedOut','Completed')");
                t.HasCheckConstraint("CK_Appointments_FlowNeedsScheduled", "[FlowState] = 'Scheduled' OR [Status] = 'Scheduled'");
            });
            // ALV-011-C01: the visit-time provider and operatory (Restrict like every relationship), and the index the live board and the occupancy check read.
            e.HasOne<ProviderProfile>().WithMany().HasForeignKey(a => a.VisitProviderProfileId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Operatory>().WithMany().HasForeignKey(a => a.VisitOperatoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.FlowState, a.StartUtc });
        });

        // ALV-004-C01: an appointment's history. Append-only; Restrict like every other relationship.
        modelBuilder.Entity<AppointmentEvent>(e =>
        {
            e.Property(v => v.EventType).HasMaxLength(20);
            e.Property(v => v.Detail).HasMaxLength(400);
            e.HasOne<Appointment>().WithMany().HasForeignKey(v => v.AppointmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.AppointmentId, v.OccurredAtUtc });
        });

        // ALV-N010: versioned forms. Versions and signed snapshots are never updated or deleted (see the SaveChanges guard and the
        // database triggers in the AddVersionedForms migration); every relationship is Restrict. The unique indexes are the
        // database-level guarantees: one version number per template, one signed snapshot per form, one open draft per
        // patient per template.
        modelBuilder.Entity<FormTemplate>(e =>
        {
            e.Property(t => t.Key).HasMaxLength(60);
            e.Property(t => t.Category).HasMaxLength(30);
            e.Property(t => t.RowVersion).IsRowVersion();
            e.HasIndex(t => t.Key).IsUnique();
            e.Property(t => t.RequiredAtCheckIn).HasDefaultValue(false); // ALV-011-C01
        });
        modelBuilder.Entity<FormTemplateVersion>(e =>
        {
            e.Property(v => v.Title).HasMaxLength(200);
            e.Property(v => v.ContentHash).HasMaxLength(64);
            e.Property(v => v.ChangeNote).HasMaxLength(400);
            e.HasOne<FormTemplate>().WithMany().HasForeignKey(v => v.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.TemplateId, v.VersionNumber }).IsUnique();
        });
        modelBuilder.Entity<PatientForm>(e =>
        {
            e.Property(f => f.Status).HasMaxLength(10);
            e.Property(f => f.VoidReason).HasMaxLength(400);
            e.Property(f => f.RowVersion).IsRowVersion();
            e.HasOne<Patient>().WithMany().HasForeignKey(f => f.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FormTemplate>().WithMany().HasForeignKey(f => f.TemplateId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FormTemplateVersion>().WithMany().HasForeignKey(f => f.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(f => new { f.PatientId, f.StartedAtUtc });
            e.HasIndex(f => new { f.PatientId, f.TemplateId }).IsUnique().HasFilter("[Status] = 'Draft'");
        });
        modelBuilder.Entity<SignedFormSnapshot>(e =>
        {
            e.Property(s => s.TemplateKey).HasMaxLength(60);
            e.Property(s => s.Category).HasMaxLength(30);
            e.Property(s => s.Title).HasMaxLength(200);
            e.Property(s => s.SignerName).HasMaxLength(200);
            e.Property(s => s.SignerRelationship).HasMaxLength(30);
            e.Property(s => s.SignerRelationshipNote).HasMaxLength(200);
            e.Property(s => s.SignatureMethod).HasMaxLength(30);
            e.Property(s => s.SignatureText).HasMaxLength(200);
            e.Property(s => s.Attestation).HasMaxLength(1000);
            e.Property(s => s.SnapshotHash).HasMaxLength(64);
            e.HasOne<PatientForm>().WithMany().HasForeignKey(s => s.PatientFormId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Patient>().WithMany().HasForeignKey(s => s.PatientId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => s.PatientFormId).IsUnique();
            e.HasIndex(s => new { s.PatientId, s.SignedAtUtc });
        });
        modelBuilder.Entity<PatientFormEvent>(e =>
        {
            e.Property(v => v.EventType).HasMaxLength(20);
            e.Property(v => v.Detail).HasMaxLength(400);
            e.HasOne<PatientForm>().WithMany().HasForeignKey(v => v.PatientFormId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(v => new { v.PatientFormId, v.OccurredAtUtc });
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

        // ALV-N010: a published template version or a signed snapshot can be added but never changed or removed.
        if (ChangeTracker.Entries().Any(e => (e.Entity is SignedFormSnapshot or FormTemplateVersion) && (e.State is EntityState.Modified or EntityState.Deleted)))
        {
            throw new SignedRecordImmutableException();
        }
    }
}
