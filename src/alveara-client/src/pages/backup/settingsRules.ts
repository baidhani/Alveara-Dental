import type { BackupSettings } from "../../services/backupApi";

/** Client-side mirror of the server's backup-settings rules; the server remains the authority. */
export interface FormState {
  scheduleEnabled: boolean;
  interval: string;
  retention: string;
  destination: string;
  required: string;
  cadence: string;
}

export const toForm = (s: BackupSettings): FormState => ({
  scheduleEnabled: s.scheduleEnabled,
  interval: String(s.scheduleIntervalHours),
  retention: String(s.retentionCount),
  destination: s.destinationIsDefault ? "" : s.destinationDirectory,
  required: String(s.requiredSuccessfulVerifications),
  cadence: String(s.verificationCadenceDays),
});

const intBetween = (value: string, min: number, max: number) => /^\d+$/.test(value.trim()) && Number(value) >= min && Number(value) <= max;

export function validateSettings(f: FormState, recoveryKeyConfigured: boolean): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!intBetween(f.interval, 1, 168)) errors.interval = "Enter a whole number of hours between 1 and 168.";
  if (!intBetween(f.retention, 1, 365)) errors.retention = "Keep between 1 and 365 backups.";
  if (!intBetween(f.required, 0, 10)) errors.required = "Enter a whole number between 0 and 10.";
  if (!intBetween(f.cadence, 1, 365)) errors.cadence = "Enter a whole number of days between 1 and 365.";
  if (f.destination.trim() !== "" && !/^([a-zA-Z]:[\\/]|\\\\|\/)/.test(f.destination.trim())) errors.destination = "Enter an absolute folder path, or leave blank for the default.";
  if (f.scheduleEnabled && !recoveryKeyConfigured) errors.scheduleEnabled = "Set up the recovery key before enabling scheduled backups.";
  return errors;
}
