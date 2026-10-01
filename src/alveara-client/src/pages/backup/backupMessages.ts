import { ApiError } from "../../services/authApi";

/**
 * Plain-language text for the stable failure codes the backup API returns. The server never sends
 * exception text, paths or key material, so these are the ONLY explanations a user sees.
 */
const MESSAGES: Record<string, string> = {
  recovery_key_required: "Set up the recovery key first - backups cannot be encrypted without it.",
  recovery_key_exists: "A recovery key already exists. Replacing it means older backups still need the OLD key.",
  weak_passphrase: "The recovery passphrase must be at least 12 characters.",
  step_up_failed: "Your current password was not accepted.",
  account_locked: "Too many wrong passwords - this account is temporarily locked.",
  destination_unavailable: "The backup destination is not available or not writable.",
  destination_full: "The backup destination is out of space.",
  io_error: "A file operation failed while handling the backup.",
  snapshot_failed: "The database could not produce a consistent snapshot.",
  interrupted: "The backup was interrupted before it finished.",
  unexpected_error: "The operation failed unexpectedly. Check the server diagnostics.",
  wrong_recovery_key: "The recovery key or its passphrase is wrong, or this key does not belong to this backup.",
  recovery_material_required: "The recovery key file and its passphrase are required.",
  recovery_material_invalid: "The recovery material is not valid.",
  hash_mismatch: "The backup file does not match the hash recorded when it was written - it is corrupt or has been altered.",
  backup_file_missing: "The backup file is missing from its destination.",
  backup_file_unknown: "This backup has no recorded file.",
  backup_not_available: "Only a successful, retained backup can be used.",
  corrupt_or_tampered: "The backup failed authentication: it is corrupt or has been altered.",
  not_a_backup: "This file is not an Alveara backup.",
  unsupported_version: "This backup was written in a format this version cannot read.",
  incompatible_backup: "This backup is from an incompatible schema or format and cannot be restored by this version.",
  documents_complete: "Documents listed in the backup are missing from the backup set.",
  components_present: "Parts of the backup set are missing.",
  component_hashes_match: "Parts of the backup do not match the hashes recorded in its manifest.",
  database_backup_damaged: "SQL Server reports the database backup inside the set as damaged.",
  restore_validation_failed: "The database restored, but validation of the restored data failed.",
  hash_verified: "The file hash matches.",
  invalid_destination: "The backup destination must be an absolute folder path outside the staging and restore areas.",
  invalid_interval: "The backup interval must be between 1 and 168 hours.",
  invalid_retention: "Retention must keep between 1 and 365 backups.",
  invalid_probation: "The required verification count must be between 0 and 10.",
  invalid_cadence: "The verification cadence must be between 1 and 365 days.",
  row_version_required: "These settings changed since you opened them. Reload and try again.",
};

export function describeBackupCode(code: string | null | undefined, fallback?: string): string {
  if (!code) return fallback ?? "The operation failed.";
  return MESSAGES[code] ?? fallback ?? `The operation failed (${code}).`;
}

/** The message for a caught error: the mapped text for a known code, the server's own safe message otherwise. */
export function describeBackupError(err: unknown, fallback = "The operation failed. Check your connection and try again."): string {
  if (err instanceof ApiError) return describeBackupCode(err.code, err.message);
  return fallback;
}

export const ASSET_CLASS_LABELS: Record<string, string> = {
  database: "Database",
  documents: "Documents",
  dataProtectionKeys: "Encryption keys",
};

export const assetLabel = (cls: string) => ASSET_CLASS_LABELS[cls] ?? cls;

export function formatBytes(bytes: number | null): string {
  if (bytes === null) return "-";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
