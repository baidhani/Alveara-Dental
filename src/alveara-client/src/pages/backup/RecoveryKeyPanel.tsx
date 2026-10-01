import { useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { useNotifications } from "../../components/Notification";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { configureRecoveryKey } from "../../services/backupApi";
import type { BackupSettings } from "../../services/backupApi";
import { describeBackupError } from "./backupMessages";

type Step =
  | { kind: "idle" }
  | { kind: "form"; replace: boolean }
  | { kind: "show"; recoveryKey: string; fingerprint: string };

/**
 * Recovery key setup. The server keeps only the PUBLIC half; the encrypted private key is returned
 * once and lives here only until the administrator confirms they stored it offline - it is held in
 * component state alone (never storage, never a URL, never logged), is cleared the moment it is
 * acknowledged, and while it is on screen the page warns that leaving would lose it forever.
 */
export function RecoveryKeyPanel({ settings, onChanged }: { settings: BackupSettings; onChanged: () => void }) {
  const { notify } = useNotifications();
  const [step, setStep] = useState<Step>({ kind: "idle" });
  const [currentPassword, setCurrentPassword] = useState("");
  const [passphrase, setPassphrase] = useState("");
  const [confirmPassphrase, setConfirmPassphrase] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [acknowledged, setAcknowledged] = useState(false);
  const session = useRef(0);

  // Leaving while the key is on screen would lose it forever: arm the browser's leave prompt and the in-app guard.
  useUnsavedChangesWarning(step.kind === "show");

  function reset() {
    session.current += 1;
    setStep({ kind: "idle" });
    setCurrentPassword("");
    setPassphrase("");
    setConfirmPassphrase("");
    setError(null);
    setAcknowledged(false);
    setBusy(false);
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (step.kind !== "form") return;
    setError(null);
    if (passphrase.length < 12) return setError("The passphrase must be at least 12 characters.");
    if (passphrase !== confirmPassphrase) return setError("The two passphrases do not match.");
    if (!currentPassword) return setError("Enter your current password to confirm this action.");

    const mySession = ++session.current;
    setBusy(true);
    try {
      const result = await configureRecoveryKey(currentPassword, passphrase, step.replace);
      if (mySession !== session.current) return; // the panel was reset meanwhile
      setCurrentPassword("");
      setPassphrase("");
      setConfirmPassphrase("");
      setStep({ kind: "show", recoveryKey: result.recoveryKey, fingerprint: result.fingerprint });
      onChanged();
    } catch (err) {
      if (mySession === session.current) setError(describeBackupError(err));
    } finally {
      if (mySession === session.current) setBusy(false);
    }
  }

  function download(recoveryKey: string, fingerprint: string) {
    try {
      const url = URL.createObjectURL(new Blob([recoveryKey], { type: "application/x-pem-file" }));
      const link = document.createElement("a");
      link.href = url;
      link.download = `alveara-recovery-key-${fingerprint.slice(0, 8)}.pem`;
      link.click();
      URL.revokeObjectURL(url);
    } catch {
      notify("warning", "Your browser could not start the download. Copy the key text below instead.");
    }
  }

  return (
    <section className="alv-config-panel" aria-label="Recovery key">
      <h2 className="alv-config-panel__form-title">Recovery key</h2>

      {step.kind === "idle" && (
        <>
          {settings.recoveryKeyConfigured ? (
            <p>
              A recovery key is set up (fingerprint <code>{settings.recoveryKeyFingerprint?.slice(0, 16)}</code>
              {settings.recoveryKeyConfiguredAtUtc ? `, ${new Date(settings.recoveryKeyConfiguredAtUtc).toLocaleDateString()}` : ""}). The server holds only its public half, so it can create
              backups but can never read them back - restoring needs the key file and passphrase you stored offline.
            </p>
          ) : (
            <p>Create a recovery key before the first backup. You will be shown it once; store it and its passphrase OFFLINE, away from this server.</p>
          )}
          <div className="alv-config-panel__form-actions">
            <Button variant={settings.recoveryKeyConfigured ? "secondary" : "primary"} onClick={() => setStep({ kind: "form", replace: settings.recoveryKeyConfigured })}>
              {settings.recoveryKeyConfigured ? "Replace recovery key" : "Set up recovery key"}
            </Button>
          </div>
        </>
      )}

      {step.kind === "form" && (
        <form className="alv-config-panel__form" onSubmit={submit} noValidate aria-label="Set up recovery key">
          {step.replace && (
            <p className="alv-backup-alert alv-backup-alert--warning" role="alert">
              Replacing the key only affects FUTURE backups. Backups made with the old key can still only be restored with the OLD key and passphrase - keep them.
            </p>
          )}
          <fieldset className="alv-config-panel__fields" disabled={busy}>
            <FormField label="Recovery passphrase" type="password" autoComplete="new-password" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} hint="At least 12 characters. You will need it to restore." />
            <FormField label="Confirm passphrase" type="password" autoComplete="new-password" value={confirmPassphrase} onChange={(e) => setConfirmPassphrase(e.target.value)} />
            <FormField label="Your current password" type="password" autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
          </fieldset>
          {error && (
            <p className="alv-form-field__error" role="alert">
              {error}
            </p>
          )}
          <div className="alv-config-panel__form-actions">
            <Button type="submit" variant="primary" disabled={busy}>
              {busy ? "Creating…" : "Create recovery key"}
            </Button>
            <Button type="button" onClick={reset} disabled={busy}>
              Cancel
            </Button>
          </div>
        </form>
      )}

      {step.kind === "show" && (
        <div className="alv-config-panel__form" role="alertdialog" aria-label="Your recovery key" aria-describedby="recovery-key-warning">
          <p id="recovery-key-warning" className="alv-backup-alert alv-backup-alert--danger" role="alert">
            This is the only time the recovery key is shown. Without this file AND its passphrase, backups can never be restored - not even by us. Store both OFFLINE (for example, a safe).
          </p>
          <textarea readOnly aria-label="Recovery key file contents" className="alv-backup-key" rows={8} value={step.recoveryKey} />
          <div className="alv-config-panel__form-actions">
            <Button onClick={() => download(step.recoveryKey, step.fingerprint)}>Download recovery key file</Button>
          </div>
          <label className="alv-config-panel__toggle">
            <input type="checkbox" checked={acknowledged} onChange={(e) => setAcknowledged(e.target.checked)} />I have stored the recovery key file and its passphrase offline
          </label>
          <div className="alv-config-panel__form-actions">
            <Button
              variant="primary"
              disabled={!acknowledged}
              onClick={() => {
                notify("success", "Recovery key saved. It has been cleared from this screen.");
                reset();
                onChanged();
              }}
            >
              Done - clear the key from this screen
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}
