import { useEffect, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { useNotifications } from "../../components/Notification";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../services/authApi";
import { preflightArchive, preflightBackup, runArchiveRestoreDrill, runRestoreDrill, verifyBackupFully } from "../../services/backupApi";
import type { ArchiveInfo, BackupRecord, PreflightResult, RecoveryMaterial, RestoreDrill, ValidationCheck } from "../../services/backupApi";
import { describeBackupCode, describeBackupError } from "./backupMessages";

type Stage =
  | { kind: "material" }
  | { kind: "checking" }
  | { kind: "preflight"; result: PreflightResult }
  | { kind: "running"; action: "verify" | "drill" }
  | { kind: "done"; title: string; ok: boolean; checks: ValidationCheck[]; detail?: string; drill?: RestoreDrill };

function CheckList({ checks }: { checks: ValidationCheck[] }) {
  return (
    <ul className="alv-backup-checks" aria-label="Checks">
      {checks.map((c) => (
        <li key={c.name} className={c.passed ? "alv-backup-check--pass" : c.blocking ? "alv-backup-check--fail" : "alv-backup-check--warn"}>
          <strong>{c.passed ? "Passed" : c.blocking ? "FAILED" : "Warning"}:</strong> {c.detail}
        </li>
      ))}
    </ul>
  );
}

/**
 * Restore wizard: explicit target, compatibility and recovery-material checks. It can only
 * (a) verify a backup or (b) restore into a NEW isolated database - it never offers to overwrite live
 * data, and says so. The recovery key, passphrase and password live only in this component's state for
 * the length of the wizard, are never persisted, and are cleared when it closes. Every response is bound
 * to a wizard SESSION: closing (or restarting) the wizard abandons in-flight requests, whose late results
 * are discarded rather than shown against a different session.
 */
export function RestoreWizard({ record, archive, onClose, onChanged }: { record?: BackupRecord; archive?: ArchiveInfo; onClose: () => void; onChanged: () => void }) {
  // Exactly one source: a recorded backup (history) or a retained archive file found on disk (disaster recovery, no history needed).
  const { notify } = useNotifications();
  const [stage, setStage] = useState<Stage>({ kind: "material" });
  const [keyText, setKeyText] = useState("");
  const [passphrase, setPassphrase] = useState("");
  const [currentPassword, setCurrentPassword] = useState("");
  const [understood, setUnderstood] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const session = useRef(0);

  const typed = keyText !== "" || passphrase !== "" || currentPassword !== "";
  useUnsavedChangesWarning(typed && stage.kind !== "done");

  useEffect(() => {
    const mine = session;
    return () => {
      mine.current += 1; // unmounted: abandon anything in flight
    };
  }, []);

  const material = (): RecoveryMaterial => ({ currentPassword, recoveryKey: keyText, passphrase });

  async function readKeyFile(file: File | undefined) {
    if (!file) return;
    try {
      setKeyText(await file.text());
      setError(null);
    } catch {
      setError("That file could not be read.");
    }
  }

  async function check() {
    setError(null);
    if (!keyText.trim() || !passphrase) return setError("Choose the recovery key file (or paste its contents) and enter its passphrase.");
    if (!currentPassword) return setError("Enter your current password to confirm this action.");
    const mine = ++session.current;
    setStage({ kind: "checking" });
    try {
      const result = archive ? await preflightArchive(archive.ref, material()) : await preflightBackup(record!.id, material());
      if (mine !== session.current) return;
      setStage({ kind: "preflight", result });
    } catch (err) {
      if (mine !== session.current) return;
      setStage({ kind: "material" });
      setError(describeBackupError(err));
    }
  }

  async function run(action: "verify" | "drill") {
    const mine = ++session.current;
    setStage({ kind: "running", action });
    try {
      if (action === "verify") {
        const verified = await verifyBackupFully(record!.id, material());
        if (mine !== session.current) return;
        const ok = verified.verificationStatus === "FullyVerified";
        setStage({
          kind: "done", ok, title: ok ? "Backup fully verified" : "Verification failed", checks: [],
          detail: ok ? "The backup decrypted with your recovery key, every component matched its recorded hash, and SQL Server accepted the database backup. Nothing was restored." : describeBackupCode(verified.verificationFailureCode),
        });
        notify(ok ? "success" : "danger", ok ? "Backup fully verified." : "Backup verification failed.");
      } else {
        const drill = archive ? await runArchiveRestoreDrill(archive.ref, material()) : await runRestoreDrill(record!.id, material());
        if (mine !== session.current) return;
        setStage({ kind: "done", ok: true, title: "Restore drill succeeded", checks: drill.checks, drill });
        notify("success", "Restore drill succeeded.");
      }
      onChanged();
    } catch (err) {
      if (mine !== session.current) return;
      const failedDrill = err instanceof ApiError && err.status === 422 && Array.isArray((err.body as { checks?: unknown }).checks) ? (err.body as unknown as RestoreDrill) : null;
      if (failedDrill) {
        setStage({ kind: "done", ok: false, title: "Restore drill failed", checks: failedDrill.checks, drill: failedDrill, detail: describeBackupCode(failedDrill.failureCode, failedDrill.failureMessage ?? undefined) });
        onChanged();
      } else {
        setStage({ kind: "preflight", result: stage.kind === "preflight" ? stage.result : { canRestore: true, checks: [] } });
        setError(describeBackupError(err));
      }
    }
  }

  function close() {
    session.current += 1;
    setKeyText("");
    setPassphrase("");
    setCurrentPassword("");
    onClose();
  }

  return (
    <section className="alv-config-panel__form alv-backup-wizard" role="dialog" aria-label="Restore wizard" aria-modal="false">
      <h2 className="alv-config-panel__form-title">
        Restore wizard - {archive ? `retained backup file ${archive.fileName}` : `backup of ${new Date(record!.startedAtUtc).toLocaleString()}`}
      </h2>
      <p className="alv-backup-alert alv-backup-alert--info">
        This never overwrites your live data. A restore creates a NEW, isolated copy (its own database and folder) so you can check it before anything else is done. Putting a restored copy
        into production is a separate, deliberate offline procedure (see the recovery runbook).
      </p>

      {stage.kind === "material" && (
        <>
          <h3>Step 1 - recovery material</h3>
          <FormField label="Recovery key file" type="file" accept=".pem,.key,.txt" onChange={(e) => readKeyFile(e.target.files?.[0])} hint="The key file you stored offline when the recovery key was created." />
          <div className="alv-form-field">
            <label className="alv-form-field__label" htmlFor="wizard-key-text">
              Or paste the key file contents
            </label>
            <textarea id="wizard-key-text" className="alv-form-field__input alv-backup-key" rows={4} value={keyText} onChange={(e) => setKeyText(e.target.value)} />
          </div>
          <FormField label="Recovery passphrase" type="password" autoComplete="off" value={passphrase} onChange={(e) => setPassphrase(e.target.value)} />
          <FormField label="Your current password" type="password" autoComplete="current-password" value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} />
          {error && (
            <p className="alv-form-field__error" role="alert">
              {error}
            </p>
          )}
          <div className="alv-config-panel__form-actions">
            <Button variant="primary" onClick={check}>
              Check compatibility and recovery material
            </Button>
            <Button onClick={close}>Cancel</Button>
          </div>
        </>
      )}

      {stage.kind === "checking" && <p role="status">Checking the backup file and your recovery material…</p>}

      {stage.kind === "preflight" && (
        <>
          <h3>Step 2 - compatibility and recovery-material checks</h3>
          <CheckList checks={stage.result.checks} />
          {!stage.result.canRestore && (
            <p className="alv-form-field__error" role="alert">
              This backup cannot be restored yet. Fix the failed checks above (for example, supply the correct recovery key) and try again.
            </p>
          )}
          {error && (
            <p className="alv-form-field__error" role="alert">
              {error}
            </p>
          )}
          <label className="alv-config-panel__toggle">
            <input type="checkbox" checked={understood} onChange={(e) => setUnderstood(e.target.checked)} />I understand a restore drill creates a new isolated database and does not change live data
          </label>
          <div className="alv-config-panel__form-actions">
            {!archive && (
              <Button disabled={!stage.result.canRestore} onClick={() => run("verify")}>
                Verify only (nothing is restored)
              </Button>
            )}
            <Button variant="primary" disabled={!stage.result.canRestore || !understood} onClick={() => run("drill")}>
              Restore into an isolated target
            </Button>
            <Button onClick={() => setStage({ kind: "material" })}>Back</Button>
            <Button onClick={close}>Cancel</Button>
          </div>
        </>
      )}

      {stage.kind === "running" && <p role="status">{stage.action === "drill" ? "Restoring into an isolated target and validating it - this can take a while…" : "Verifying the backup…"}</p>}

      {stage.kind === "done" && (
        <>
          <h3 className={stage.ok ? "alv-backup-result--ok" : "alv-backup-result--fail"} role="status">
            {stage.title}
          </h3>
          {stage.detail && <p>{stage.detail}</p>}
          {stage.drill?.outcome === "Succeeded" && (
            <p>
              The recovered data is in the isolated database <code>{stage.drill.targetDatabase}</code>. Your live data was not touched. Remove the target from the drills list below when you are
              done inspecting it.
            </p>
          )}
          {stage.checks.length > 0 && <CheckList checks={stage.checks} />}
          <div className="alv-config-panel__form-actions">
            <Button variant="primary" onClick={close}>
              Close
            </Button>
          </div>
        </>
      )}
    </section>
  );
}
