import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ApiError } from "../services/authApi";
import { BACKUP_ALERT_LABELS, acknowledgeBackupAlert, getUnacknowledgedBackupAlerts } from "../services/backupApi";
import type { BackupAlert } from "../services/backupApi";
import { formatDateTime } from "../pages/documents/documentText";

/** How often an open screen asks again (a failure found by a scheduled backup appears without a reload). */
export const BACKUP_ALERT_POLL_MS = 5 * 60 * 1000;

/**
 * ALV-N026 (PD-25): the administrators' persistent banner for backup and restore failures nobody has acknowledged yet. Shown only to an administrator (the server answers only them). Each
 * alert can be acknowledged (audited); that hides it here, but System Status keeps warning until a later success resolves the problem, and the banner says so. A failed load shows nothing
 * rather than a false "all clear": System Status remains the place that reports.
 */
export function BackupAlertBanner() {
  const [alerts, setAlerts] = useState<BackupAlert[]>([]);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    try {
      const result = await getUnacknowledgedBackupAlerts(signal);
      setAlerts(Array.isArray(result) ? result : []);                                   // anything but a list is treated as nothing to show
    } catch (err) {
      // a failed load keeps what is shown (no false all-clear); System Status is the place that reports
      if (!signal?.aborted) console.warn("backup_alerts_load_failed", err instanceof Error ? err.name : "unknown");
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    const timer = window.setInterval(() => void load(), BACKUP_ALERT_POLL_MS);
    return () => { controller.abort(); window.clearInterval(timer); };
  }, [load]);

  async function acknowledge(id: string) {
    setBusy(id); setError(null);
    try {
      await acknowledgeBackupAlert(id);
      setAlerts((a) => a.filter((x) => x.id !== id));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the server, so the alert was not acknowledged. Try again.");
    } finally { setBusy(null); }
  }

  if (alerts.length === 0) return null;
  return (
    <section className="alv-shell__session-warning alv-shell__backup-alert" role="alert" aria-label="Backup alerts">
      <p>
        <strong>{alerts.length === 1 ? "A backup or restore problem needs attention." : `${alerts.length} backup or restore problems need attention.`}</strong>{" "}
        Acknowledging hides an alert here; <Link to="/system-status">System Status</Link> keeps warning until a later backup or restore check succeeds.
      </p>
      <ul className="alv-shell__backup-alert-list">
        {alerts.map((a) => (
          <li key={a.id}>
            {BACKUP_ALERT_LABELS[a.kind] ?? "A backup problem"}, {formatDateTime(a.createdAtUtc)}{a.delivery === "Failed" ? " (not every notification could be delivered)" : ""}.{" "}
            <button type="button" className="alv-shell__link-button" disabled={busy === a.id} onClick={() => void acknowledge(a.id)} aria-label={`Acknowledge: ${BACKUP_ALERT_LABELS[a.kind] ?? "backup problem"}, ${formatDateTime(a.createdAtUtc)}`}>
              {busy === a.id ? "Acknowledging…" : "Acknowledge"}
            </button>
          </li>
        ))}
      </ul>
      {error && <p role="status">{error}</p>}
    </section>
  );
}
