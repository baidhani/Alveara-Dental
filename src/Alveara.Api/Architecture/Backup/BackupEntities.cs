namespace Alveara.Api.Architecture.Backup;

public enum BackupKind
{
    Manual,
    Scheduled,
}

public enum BackupStatus
{
    Running,
    Succeeded,
    Failed,
    /// <summary>The backup file was removed by the retention policy; the history row is kept.</summary>
    Purged,
}

/// <summary>
/// What has actually been proven about a backup file, in increasing strength. Success of the
/// BACKUP itself is a separate fact (<see cref="BackupStatus"/>): a backup can have succeeded and
/// never have been verified.
/// </summary>
public enum BackupVerificationStatus
{
    /// <summary>Written and hash-recorded; nothing re-checked since.</summary>
    NotVerified,
    /// <summary>The file's SHA-256 matches the hash recorded at creation (detects corruption/truncation; cannot prove restorability).</summary>
    HashVerified,
    /// <summary>Decrypted with the recovery key, every component hash matched, and SQL Server's own RESTORE VERIFYONLY passed.</summary>
    FullyVerified,
    /// <summary>The most recent verification attempt failed (hash mismatch, wrong key, corrupt, ...).</summary>
    VerificationFailed,
}

public enum BackupNotificationDelivery
{
    Pending,
    Delivered,
    Failed,
}

/// <summary>
/// ALV-N004: the singleton backup/recovery configuration. Holds the recovery key's PUBLIC half only
/// - the server can therefore encrypt every unattended backup but can never decrypt one. The private
/// half is shown to the administrator exactly once at setup and is never stored here, in logs, or
/// inside any backup.
/// </summary>
/// <summary>
/// Failure codes that are properties of the ARCHIVE itself (not of the recovering server, the supplied key or the
/// destination). Once seen they are permanent, because an archive is immutable.
/// </summary>
public static class ArchiveDefects
{
    public static readonly IReadOnlySet<string> Codes = new HashSet<string>(StringComparer.Ordinal)
    {
        "restore_validation_failed", "deployment_asset_matches_manifest", "deployment_settings_recorded", "documents_complete", "components_present",
        "component_hashes_match", "database_backup_damaged", "manifest_missing", "manifest_invalid", "corrupt_or_tampered",
    };

    public static bool IsDefect(string? code) => code is not null && Codes.Contains(code);
}

public class BackupSettings
{
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-00000000b4c4");

    public Guid Id { get; set; } = SingletonId;

    public bool ScheduleEnabled { get; set; }

    /// <summary>Hours between scheduled backups (1..168).</summary>
    public int ScheduleIntervalHours { get; set; } = 24;

    /// <summary>How many most-recent successful backups to keep (never fewer than 1; the newest fully-verified one is never purged).</summary>
    public int RetentionCount { get; set; } = 7;

    /// <summary>Optional override of the backup destination folder; null = the deployment default.</summary>
    public string? DestinationDirectory { get; set; }

    /// <summary>PEM (SubjectPublicKeyInfo) of the recovery key used to wrap every backup's data key.</summary>
    public string? RecoveryPublicKeyPem { get; set; }

    /// <summary>SHA-256 (hex) of the public key, so a backup/key mismatch is recognisable without decrypting.</summary>
    public string? RecoveryKeyFingerprint { get; set; }
    public DateTimeOffset? RecoveryKeyConfiguredAtUtc { get; set; }

    /// <summary>
    /// Confidence/probation workflow: unattended scheduled backups are not "trusted" until this many
    /// full verifications or restore drills have succeeded. Scheduled backups still run during
    /// probation, but the UI says plainly they are not yet trusted.
    /// </summary>
    public int RequiredSuccessfulVerifications { get; set; } = 2;

    /// <summary>A full verification/restore drill older than this many days is reported as overdue.</summary>
    public int VerificationCadenceDays { get; set; } = 30;

    public DateTimeOffset UpdatedAtUtc { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

/// <summary>One backup attempt and everything truthfully known about it.</summary>
public class BackupRecord
{
    public Guid Id { get; set; }
    public BackupKind Kind { get; set; }
    public BackupStatus Status { get; set; }

    /// <summary>Stable key making a scheduled slot / retried job idempotent: the same key never yields two backups.</summary>
    public required string IdempotencyKey { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid? InitiatedByUserAccountId { get; set; }

    /// <summary>The destination folder in force when this backup was written (the setting can change later).</summary>
    public string? DestinationDirectory { get; set; }

    /// <summary>File name (not path) inside <see cref="DestinationDirectory"/>; null until written.</summary>
    public string? FileName { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }

    /// <summary>Comma-separated asset classes the set contains (see <see cref="ManagedAssetClasses"/>).</summary>
    public string? IncludedAssetClasses { get; set; }
    public string? SchemaMigration { get; set; }
    public string? AppVersion { get; set; }
    public string? RecoveryKeyFingerprint { get; set; }

    /// <summary>Stable machine-readable failure reason (never exception text).</summary>
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public BackupVerificationStatus VerificationStatus { get; set; } = BackupVerificationStatus.NotVerified;
    public DateTimeOffset? VerifiedAtUtc { get; set; }
    public string? VerificationFailureCode { get; set; }

    /// <summary>
    /// When a restore drill proved this backup restores into a working, internally consistent application state. ONLY a
    /// successful drill sets it. "Verify only" proves the file decrypts, matches its manifest and that SQL Server accepts the
    /// database media - not that the captured database agrees with the rest of the set - so it never sets this and never
    /// counts toward trust/probation. A drill that finds the archive itself defective clears it for good.
    /// </summary>
    public DateTimeOffset? RestoreProvenAtUtc { get; set; }

    /// <summary>
    /// A defect proven in the ARCHIVE ITSELF (see <see cref="ArchiveDefects"/>). Stored apart from the mutable "latest check"
    /// fields on purpose: an archive is immutable, so once this is set it is permanent, no later check (hash, verify-only,
    /// wrong key, destination error, drill) can clear or replace it, and it keeps the backup failed and without restore proof.
    /// </summary>
    public string? ArchiveDefectCode { get; set; }
    public DateTimeOffset? ArchiveDefectAtUtc { get; set; }

    public bool HasArchiveDefect => ArchiveDefectCode is not null;

    /// <summary>
    /// The ONE way a failed check is written: a known archive defect is never replaced by a different (weaker or environmental)
    /// failure, and an archive-intrinsic failure becomes the permanent defect.
    /// </summary>
    public void MarkVerificationFailed(string code, DateTimeOffset now)
    {
        if (!HasArchiveDefect && ArchiveDefects.IsDefect(code))
        {
            ArchiveDefectCode = code;
            ArchiveDefectAtUtc = now;
        }
        if (HasArchiveDefect)
        {
            VerificationStatus = BackupVerificationStatus.VerificationFailed;
            VerificationFailureCode = ArchiveDefectCode;
            RestoreProvenAtUtc = null;
            VerifiedAtUtc = ArchiveDefectAtUtc;
            return;
        }
        VerificationStatus = BackupVerificationStatus.VerificationFailed;
        VerificationFailureCode = code;
        VerifiedAtUtc = now;
    }
}

/// <summary>A restore into an isolated target (a drill): where it went and what was validated.</summary>
public class RestoreDrillRecord
{
    public Guid Id { get; set; }
    /// <summary>The history row restored from; NULL when the drill restored a retained archive file whose history row does not exist (disaster recovery).</summary>
    public Guid? BackupRecordId { get; set; }

    /// <summary>"History" (a recorded backup) or "Archive" (a retained .abk file, independent of any database row).</summary>
    public string SourceKind { get; set; } = "History";

    /// <summary>For an archive drill: which retained file (name only) and its SHA-256 at the time.</summary>
    public string? ArchiveFileName { get; set; }
    public string? ArchiveSha256 { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public Guid? InitiatedByUserAccountId { get; set; }

    /// <summary>"Succeeded" or "Failed".</summary>
    public string Outcome { get; set; } = "Running";
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }

    public string? TargetDatabase { get; set; }
    public string? TargetDirectory { get; set; }

    /// <summary>JSON array of { name, passed, detail } post-restore validation checks.</summary>
    public string? ValidationJson { get; set; }

    /// <summary>True once the isolated target database/files have been removed.</summary>
    public bool TargetRemoved { get; set; }
}

/// <summary>A notification the system tried to deliver and what happened - delivery never changes a backup's own status.</summary>
public class BackupNotificationRecord
{
    public Guid Id { get; set; }
    public Guid? BackupRecordId { get; set; }
    public required string Kind { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public BackupNotificationDelivery Delivery { get; set; } = BackupNotificationDelivery.Pending;
    public string? DeliveryFailureCode { get; set; }
}

/// <summary>
/// The persistent asset classes a complete backup must cover. A later story that adds a new
/// persistent asset class (e.g. a new on-disk store) registers an <see cref="IBackupAssetSource"/>
/// AND adds its name here, so a backup that predates it is visibly reported as incomplete.
/// </summary>
public static class ManagedAssetClasses
{
    public const string Database = "database";
    public const string Documents = "documents";
    public const string DataProtectionKeys = "dataProtectionKeys";

    /// <summary>The non-secret deployment settings (practice time zone, Data Protection application name) without which restored data is misinterpreted or unreadable.</summary>
    public const string DeploymentConfiguration = "deploymentConfiguration";

    public static readonly IReadOnlyList<string> All = [Database, Documents, DataProtectionKeys, DeploymentConfiguration];
}
