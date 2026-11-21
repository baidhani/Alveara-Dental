/**
 * ALV-N004: typed client for the backup & recovery API (Controllers/BackupController). Recovery
 * material (the key file text and passphrase) is only ever passed through function arguments for the
 * duration of a single request - this module stores nothing, and callers must not persist it either.
 * There is intentionally no call that restores over live data: restore is always into an isolated target.
 */
import { request, requestWithCsrf } from "./authApi";

export interface BackupSettings {
  scheduleEnabled: boolean;
  scheduleIntervalHours: number;
  retentionCount: number;
  destinationDirectory: string;
  destinationIsDefault: boolean;
  recoveryKeyConfigured: boolean;
  recoveryKeyFingerprint: string | null;
  recoveryKeyConfiguredAtUtc: string | null;
  requiredSuccessfulVerifications: number;
  verificationCadenceDays: number;
  rowVersion: string;
}

export type VerificationStatus = "NotVerified" | "HashVerified" | "FullyVerified" | "VerificationFailed";

export interface BackupRecord {
  id: string;
  kind: "Manual" | "Scheduled";
  status: "Running" | "Succeeded" | "Failed" | "Purged";
  startedAtUtc: string;
  completedAtUtc: string | null;
  fileName: string | null;
  sizeBytes: number | null;
  sha256: string | null;
  includedAssetClasses: string[];
  schemaMigration: string | null;
  appVersion: string | null;
  failureCode: string | null;
  failureMessage: string | null;
  verificationStatus: VerificationStatus;
  verifiedAtUtc: string | null;
  verificationFailureCode: string | null;
  /** True only after a successful restore drill: "verify only" never sets it and never counts toward trust. */
  restoreProven: boolean;
  /** A defect proven in the archive itself; permanent - no later check clears it. */
  archiveDefectCode: string | null;
}

export interface BackupStatus {
  settings: BackupSettings;
  lastSuccess: BackupRecord | null;
  lastFailure: BackupRecord | null;
  latestAttemptFailed: boolean;
  successfulVerificationCount: number;
  trusted: boolean;
  lastFullVerificationAtUtc: string | null;
  verificationOverdue: boolean;
  requiredAssetClasses: string[];
  lastSuccessCoversAllAssetClasses: boolean;
  missingAssetClasses: string[];
}

export interface ValidationCheck {
  name: string;
  passed: boolean;
  detail: string;
  blocking: boolean;
}

export interface PreflightResult {
  canRestore: boolean;
  checks: ValidationCheck[];
}

export interface ArchiveInfo {
  /** "destination:NAME.abk" or "import:NAME.abk" - the only way an archive is chosen (never a free path). */
  ref: string;
  location: "destination" | "import";
  fileName: string;
  sizeBytes: number;
  modifiedUtc: string;
  readable: boolean;
  recipientKeyId: string | null;
  keyMatchesConfiguredRecoveryKey: boolean;
  inHistory: boolean;
}

export interface RestoreDrill {
  id: string;
  backupRecordId: string | null;
  sourceKind: "History" | "Archive";
  archiveFileName: string | null;
  archiveSha256: string | null;
  startedAtUtc: string;
  completedAtUtc: string | null;
  outcome: string;
  failureCode: string | null;
  failureMessage: string | null;
  targetDatabase: string | null;
  targetDirectory: string | null;
  targetRemoved: boolean;
  checks: ValidationCheck[];
}

export interface BackupNotification {
  id: string;
  backupRecordId: string | null;
  kind: string;
  message: string;
  createdAtUtc: string;
  delivery: "Pending" | "Delivered" | "Failed";
  deliveryFailureCode: string | null;
}

export interface RecoveryMaterial {
  currentPassword: string;
  recoveryKey: string;
  passphrase: string;
}

export interface BackupSettingsInput {
  scheduleEnabled: boolean;
  scheduleIntervalHours: number;
  retentionCount: number;
  destinationDirectory: string | null;
  requiredSuccessfulVerifications: number;
  verificationCadenceDays: number;
  rowVersion: string;
}

export const getBackupStatus = () => request<BackupStatus>("/api/backup/status");
export const getBackupHistory = (take = 50) => request<BackupRecord[]>(`/api/backup/history?take=${take}`);
export const getRestoreDrills = (take = 50) => request<RestoreDrill[]>(`/api/backup/restore-drills?take=${take}`);
export const getBackupNotifications = (take = 20) => request<BackupNotification[]>(`/api/backup/notifications?take=${take}`);

export const saveBackupSettings = (input: BackupSettingsInput) => requestWithCsrf<BackupSettings>("/api/backup/settings", "PUT", input);

/** Returns the private recovery key AND its server-generated passphrase exactly once; the server keeps only the public half. */
export const configureRecoveryKey = (currentPassword: string, replaceExisting: boolean) =>
  requestWithCsrf<{ recoveryKey: string; passphrase: string; fingerprint: string; warning: string }>("/api/backup/recovery-key", "POST", { currentPassword, replaceExisting });

/** Disaster recovery: retained .abk files in the backup folder and the import folder, found with NO backup history needed. */
export const getArchives = () => request<ArchiveInfo[]>("/api/backup/archives");
export const preflightArchive = (archive: string, m: RecoveryMaterial) => requestWithCsrf<PreflightResult>("/api/backup/archives/preflight", "POST", { archive, ...m });
export const runArchiveRestoreDrill = (archive: string, m: RecoveryMaterial) => requestWithCsrf<RestoreDrill>("/api/backup/archives/restore-drill", "POST", { archive, ...m });

export const runManualBackup = () => requestWithCsrf<BackupRecord>("/api/backup/backups", "POST");
export const verifyBackupHash = (id: string) => requestWithCsrf<BackupRecord>(`/api/backup/backups/${id}/verify-hash`, "POST");
export const preflightBackup = (id: string, m: RecoveryMaterial) => requestWithCsrf<PreflightResult>(`/api/backup/backups/${id}/preflight`, "POST", m);
export const verifyBackupFully = (id: string, m: RecoveryMaterial) => requestWithCsrf<BackupRecord>(`/api/backup/backups/${id}/verify`, "POST", m);
export const runRestoreDrill = (id: string, m: RecoveryMaterial) => requestWithCsrf<RestoreDrill>(`/api/backup/backups/${id}/restore-drill`, "POST", m);
export const removeRestoreTarget = (drillId: string, currentPassword: string) =>
  requestWithCsrf<RestoreDrill>(`/api/backup/restore-drills/${drillId}/remove-target`, "POST", { currentPassword });
export const sendTestNotification = () => requestWithCsrf<{ id: string; kind: string; delivery: string; deliveryFailureCode: string | null }>("/api/backup/notifications/test", "POST");

// ---------- ALV-N026 (PD-25, D-06): backup alerts and the administrator's System Status ----------

export type BackupAlertKind = "BackupFailed" | "BackupVerificationFailed" | "RestoreDrillFailed";
export interface BackupAlert { id: string; kind: BackupAlertKind; createdAtUtc: string; delivery: string; deliveryFailureCode: string | null; acknowledged: boolean; acknowledgedAtUtc: string | null }

/** The backup and restore failures no administrator has acknowledged yet (administrators only). */
export function getUnacknowledgedBackupAlerts(signal?: AbortSignal): Promise<BackupAlert[]> {
  return request<BackupAlert[]>("/api/backup/alerts/unacknowledged", { signal });
}

/** Acknowledges one alert (audited; acknowledging again changes nothing). It hides the alert from the banner; System Status stays in warning until the problem is resolved. */
export function acknowledgeBackupAlert(id: string): Promise<BackupAlert> {
  return requestWithCsrf<BackupAlert>(`/api/backup/alerts/${id}/acknowledge`, "POST");
}

export interface BackupHealth {
  lastGoodBackupUtc: string | null; lastRestoreDrillUtc: string | null; lastRestoreDrillOutcome: string | null; recoveryKeyConfigured: boolean; scheduleEnabled: boolean;
  overdue: boolean; unresolvedFailures: number; unacknowledgedAlerts: number; warning: boolean;
}
export interface StorageHealth { documentFolderReachable: boolean; freeBytes: number | null; missingDocuments: number; damagedDocuments: number }
export interface AccountsSummary { enabled: number; disabled: number; locked: number; enabledWithoutMfa: number }
/** A section the server could not read is null: shown as unavailable, never as healthy. */
/** ALV-N026 S4: whether PDFs can be produced now; the reason class only. */
export interface PdfEngineHealth { ready: boolean; code: string | null; errorClass: string | null; reason: string | null; bundleId: string; checkedAtUtc: string }
export interface AdminSystemStatus { backup: BackupHealth | null; storage: StorageHealth | null; accounts: AccountsSummary | null; pdfEngine?: PdfEngineHealth | null }

export function getAdminSystemStatus(signal?: AbortSignal): Promise<AdminSystemStatus> {
  return request<AdminSystemStatus>("/api/systemstatus/admin", { signal });
}

export const BACKUP_ALERT_LABELS: Record<BackupAlertKind, string> = {
  BackupFailed: "A backup failed",
  BackupVerificationFailed: "A backup failed its verification",
  RestoreDrillFailed: "A restore drill failed",
};
