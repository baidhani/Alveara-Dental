import type { BackupStatus } from "../../services/backupApi";
import { assetLabel, describeBackupCode, formatBytes } from "./backupMessages";

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "never");

/**
 * The truthful state of backups at a glance. A failure is shown prominently and is NOT hidden behind
 * an older success: if the most recent attempt failed, that is the headline. "Backed up" and
 * "verified" are separate facts, and unattended scheduling is described as untrusted until enough
 * full verifications/restore drills have passed. Encryption material is never shown here.
 */
export function BackupStatusPanel({ status }: { status: BackupStatus }) {
  const { settings, lastSuccess, lastFailure } = status;

  return (
    <section className="alv-backup-status" aria-label="Backup status">
      {status.latestAttemptFailed && lastFailure && (
        <div className="alv-backup-alert alv-backup-alert--danger" role="alert">
          <p className="alv-backup-alert__title">The most recent backup FAILED</p>
          <p>
            {describeBackupCode(lastFailure.failureCode, lastFailure.failureMessage ?? undefined)} ({when(lastFailure.completedAtUtc ?? lastFailure.startedAtUtc)})
          </p>
          <p>{lastSuccess ? `The last successful backup was ${when(lastSuccess.completedAtUtc)}.` : "There is no successful backup yet."}</p>
        </div>
      )}

      {!settings.recoveryKeyConfigured && (
        <div className="alv-backup-alert alv-backup-alert--warning" role="status">
          <p className="alv-backup-alert__title">No recovery key is set up</p>
          <p>Backups cannot be created or scheduled until a recovery key exists.</p>
        </div>
      )}

      <dl className="alv-backup-facts">
        <div>
          <dt>Last successful backup</dt>
          <dd>{lastSuccess ? `${when(lastSuccess.completedAtUtc)} (${formatBytes(lastSuccess.sizeBytes)})` : "none yet"}</dd>
        </div>
        <div>
          <dt>Last failed backup</dt>
          <dd>{lastFailure ? `${when(lastFailure.completedAtUtc ?? lastFailure.startedAtUtc)} - ${describeBackupCode(lastFailure.failureCode)}` : "none"}</dd>
        </div>
        <div>
          <dt>Schedule</dt>
          <dd>{settings.scheduleEnabled ? `Every ${settings.scheduleIntervalHours} hour(s), keeping the latest ${settings.retentionCount}` : "Scheduled backups are off"}</dd>
        </div>
        <div>
          <dt>Storage target</dt>
          <dd>
            {settings.destinationDirectory}
            {settings.destinationIsDefault ? " (default)" : ""}
          </dd>
        </div>
        <div>
          <dt>Verification</dt>
          <dd>
            {status.successfulVerificationCount} of {settings.requiredSuccessfulVerifications} required full verification(s) or restore drill(s) passed. Last: {when(status.lastFullVerificationAtUtc)}
          </dd>
        </div>
      </dl>

      {settings.scheduleEnabled && !status.trusted && (
        <div className="alv-backup-alert alv-backup-alert--warning" role="status">
          <p className="alv-backup-alert__title">Scheduled backups are on probation</p>
          <p>
            Unattended backups are not yet trusted: only {status.successfulVerificationCount} of {settings.requiredSuccessfulVerifications} required full verification(s) or
            restore drill(s) have succeeded. Verify a backup with the recovery key to build confidence.
          </p>
        </div>
      )}

      {lastSuccess && status.verificationOverdue && (
        <div className="alv-backup-alert alv-backup-alert--warning" role="status">
          <p className="alv-backup-alert__title">A full verification is overdue</p>
          <p>
            No full verification or restore drill has passed in the last {settings.verificationCadenceDays} day(s). A backup that has only been hash-checked is not proven restorable.
          </p>
        </div>
      )}

      <p className="alv-backup-coverage" aria-label="Asset coverage">
        {lastSuccess === null
          ? "Coverage: no backup yet."
          : status.lastSuccessCoversAllAssetClasses
            ? `The last backup includes every managed asset class: ${status.requiredAssetClasses.map(assetLabel).join(", ")}.`
            : `The last backup does NOT include every managed asset class. Missing: ${status.missingAssetClasses.map(assetLabel).join(", ")}.`}
      </p>
    </section>
  );
}
