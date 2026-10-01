import { useRef, useState } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { EmptyState } from "../../components/StatePatterns";
import { useNotifications } from "../../components/Notification";
import { removeRestoreTarget, verifyBackupHash } from "../../services/backupApi";
import type { BackupNotification, BackupRecord, RestoreDrill, VerificationStatus } from "../../services/backupApi";
import { assetLabel, describeBackupCode, describeBackupError, formatBytes } from "./backupMessages";

const when = (iso: string | null) => (iso ? new Date(iso).toLocaleString() : "-");

const VERIFICATION_LABEL: Record<VerificationStatus, string> = {
  NotVerified: "Not verified",
  HashVerified: "File hash verified only",
  FullyVerified: "Fully verified (restorable)",
  VerificationFailed: "Verification FAILED",
};

/** Backup history: success, failure and verification are three separate columns of truth. */
export function BackupHistory({ records, canManage, onChanged, onRestore }: { records: BackupRecord[]; canManage: boolean; onChanged: () => void; onRestore: (r: BackupRecord) => void }) {
  const { notify } = useNotifications();
  const [busyId, setBusyId] = useState<string | null>(null);
  const busy = useRef<string | null>(null);

  async function hashCheck(record: BackupRecord) {
    if (busy.current) return;
    busy.current = record.id;
    setBusyId(record.id);
    try {
      const result = await verifyBackupHash(record.id);
      notify(result.verificationStatus === "VerificationFailed" ? "danger" : "success", result.verificationStatus === "VerificationFailed" ? describeBackupCode(result.verificationFailureCode) : "The backup file hash matches.");
    } catch (err) {
      notify("danger", describeBackupError(err));
    } finally {
      busy.current = null;
      setBusyId(null);
      onChanged();
    }
  }

  if (records.length === 0) return <EmptyState title="No backups yet" description="Create the recovery key, then run the first backup." />;

  return (
    <section className="alv-config-panel" aria-label="Backup history">
      <h2 className="alv-config-panel__form-title">Backup history</h2>
      <table className="alv-config-panel__table">
        <thead>
          <tr>
            <th>Started</th>
            <th>Type</th>
            <th>Result</th>
            <th>Size</th>
            <th>Contains</th>
            <th>Verification</th>
            {canManage && <th>Actions</th>}
          </tr>
        </thead>
        <tbody>
          {records.map((r) => (
            <tr key={r.id}>
              <td>{when(r.startedAtUtc)}</td>
              <td>{r.kind}</td>
              <td>
                <span className={`alv-status-badge alv-status-badge--${r.status === "Succeeded" ? "enabled" : r.status === "Failed" ? "disabled" : "neutral"}`}>{r.status}</span>
                {r.status === "Failed" && <div>{describeBackupCode(r.failureCode, r.failureMessage ?? undefined)}</div>}
              </td>
              <td>{formatBytes(r.sizeBytes)}</td>
              <td>{r.includedAssetClasses.length === 0 ? "-" : r.includedAssetClasses.map(assetLabel).join(", ")}</td>
              <td>
                <span className={`alv-status-badge alv-status-badge--${r.verificationStatus === "FullyVerified" ? "enabled" : r.verificationStatus === "VerificationFailed" ? "disabled" : "neutral"}`}>
                  {r.status === "Succeeded" ? VERIFICATION_LABEL[r.verificationStatus] : "-"}
                </span>
                {r.verificationStatus === "VerificationFailed" && <div>{describeBackupCode(r.verificationFailureCode)}</div>}
              </td>
              {canManage && (
                <td className="alv-config-panel__actions">
                  {r.status === "Succeeded" && (
                    <>
                      <Button disabled={busyId !== null} onClick={() => hashCheck(r)} aria-label={`Check file hash for backup of ${when(r.startedAtUtc)}`}>
                        Check file hash
                      </Button>
                      <Button onClick={() => onRestore(r)} aria-label={`Verify or restore backup of ${when(r.startedAtUtc)}`}>
                        Verify / restore…
                      </Button>
                    </>
                  )}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

/** Restore drills and their isolated targets. Removing a target needs the current password (it drops a database). */
export function RestoreDrillList({ drills, onChanged }: { drills: RestoreDrill[]; onChanged: () => void }) {
  const { notify } = useNotifications();
  const [removing, setRemoving] = useState<string | null>(null);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function remove(drill: RestoreDrill) {
    setError(null);
    try {
      await removeRestoreTarget(drill.id, password);
      notify("success", "The isolated restore target was removed.");
      setRemoving(null);
      setPassword("");
      onChanged();
    } catch (err) {
      setError(describeBackupError(err));
    }
  }

  if (drills.length === 0) return null;
  return (
    <section className="alv-config-panel" aria-label="Restore drills">
      <h2 className="alv-config-panel__form-title">Restore drills</h2>
      <table className="alv-config-panel__table">
        <thead>
          <tr>
            <th>Started</th>
            <th>Outcome</th>
            <th>Isolated target</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          {drills.map((d) => (
            <tr key={d.id}>
              <td>{when(d.startedAtUtc)}</td>
              <td>
                <span className={`alv-status-badge alv-status-badge--${d.outcome === "Succeeded" ? "enabled" : "disabled"}`}>{d.outcome}</span>
                {d.outcome === "Failed" && <div>{describeBackupCode(d.failureCode, d.failureMessage ?? undefined)}</div>}
              </td>
              <td>{d.targetRemoved ? "removed" : d.outcome === "Succeeded" ? d.targetDatabase : "-"}</td>
              <td>
                {d.outcome === "Succeeded" && !d.targetRemoved && removing !== d.id && <Button onClick={() => setRemoving(d.id)}>Remove target…</Button>}
                {removing === d.id && (
                  <div className="alv-config-panel__form" role="group" aria-label="Confirm removal">
                    <FormField label="Your current password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
                    {error && (
                      <p className="alv-form-field__error" role="alert">
                        {error}
                      </p>
                    )}
                    <div className="alv-config-panel__form-actions">
                      <Button variant="danger" onClick={() => remove(d)}>
                        Remove the restored copy
                      </Button>
                      <Button onClick={() => { setRemoving(null); setPassword(""); setError(null); }}>Cancel</Button>
                    </div>
                  </div>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

export function NotificationList({ notifications }: { notifications: BackupNotification[] }) {
  if (notifications.length === 0) return null;
  return (
    <section className="alv-config-panel" aria-label="Notifications">
      <h2 className="alv-config-panel__form-title">Recent notifications</h2>
      <ul>
        {notifications.map((n) => (
          <li key={n.id}>
            {when(n.createdAtUtc)} - {n.kind}: {n.message}{" "}
            <span className={`alv-status-badge alv-status-badge--${n.delivery === "Delivered" ? "enabled" : n.delivery === "Failed" ? "disabled" : "neutral"}`}>
              {n.delivery === "Failed" ? "Delivery failed" : n.delivery}
            </span>
          </li>
        ))}
      </ul>
    </section>
  );
}
