import { useCallback, useEffect, useRef, useState } from "react";
import { Button } from "../components/Button";
import { PageHeader } from "../components/PageHeader";
import { PermissionDenied } from "../components/PermissionDenied";
import { ErrorState, LoadingState } from "../components/StatePatterns";
import { useNotifications } from "../components/Notification";
import { useAuth } from "../contexts/AuthContext";
import { ApiError } from "../services/authApi";
import {
  getBackupHistory, getBackupNotifications, getBackupStatus, getRestoreDrills, runManualBackup, sendTestNotification,
} from "../services/backupApi";
import type { BackupNotification, BackupRecord, BackupStatus, RestoreDrill } from "../services/backupApi";
import { BackupStatusPanel } from "./backup/BackupStatusPanel";
import { RecoveryKeyPanel } from "./backup/RecoveryKeyPanel";
import { BackupSettingsForm } from "./backup/BackupSettingsForm";
import { BackupHistory, NotificationList, RestoreDrillList } from "./backup/BackupLists";
import { RestoreWizard } from "./backup/RestoreWizard";
import { describeBackupError } from "./backup/backupMessages";
import "./BackupRecoveryPage.css";

interface Loaded {
  status: BackupStatus;
  history: BackupRecord[];
  drills: RestoreDrill[];
  notifications: BackupNotification[];
}

type PageState = { kind: "loading" } | { kind: "denied" } | { kind: "error" } | { kind: "loaded"; data: Loaded };

/**
 * ALV-N004's Backup & Recovery admin page. Everyone with ViewBackupStatus sees status, history and
 * failures (so a failed backup cannot go unnoticed); only ManageBackups holders see the actions
 * (recovery key, settings, manual backup, verification, restore wizard). The server enforces both. After the first
 * load, refreshes update in place - they never unmount an open wizard or an on-screen recovery key.
 */
export function BackupRecoveryPage() {
  const { hasPermission } = useAuth();
  const { notify } = useNotifications();
  const canManage = hasPermission("ManageBackups");
  const [state, setState] = useState<PageState>({ kind: "loading" });
  const [wizardFor, setWizardFor] = useState<BackupRecord | null>(null);
  const [running, setRunning] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);
  const generation = useRef(0);

  const load = useCallback(async (initial: boolean) => {
    const mine = ++generation.current;
    if (initial) setState({ kind: "loading" });
    try {
      const [status, history] = await Promise.all([getBackupStatus(), getBackupHistory()]);
      const [drills, notifications] = await Promise.all([
        canManage ? getRestoreDrills() : Promise.resolve<RestoreDrill[]>([]),
        getBackupNotifications().catch(() => [] as BackupNotification[]),
      ]);
      if (mine !== generation.current) return; // a newer refresh superseded this one
      setState({ kind: "loaded", data: { status, history, drills, notifications } });
    } catch (err) {
      if (mine !== generation.current) return;
      if (err instanceof ApiError && err.status === 403) setState({ kind: "denied" });
      else setState((prev) => (prev.kind === "loaded" ? prev : { kind: "error" })); // a failed REFRESH keeps what is already on screen
    }
  }, [canManage]);

  useEffect(() => {
    load(true);
  }, [load]);

  const refresh = useCallback(() => load(false), [load]);

  async function backupNow() {
    setRunning(true);
    setActionError(null);
    try {
      await runManualBackup();
      notify("success", "Backup completed.");
    } catch (err) {
      setActionError(describeBackupError(err));
    } finally {
      setRunning(false);
      refresh();
    }
  }

  async function testNotification() {
    try {
      const result = await sendTestNotification();
      notify(result.delivery === "Delivered" ? "success" : "danger", result.delivery === "Delivered" ? "Test notification delivered." : "Test notification could NOT be delivered - check the notification folder.");
    } catch (err) {
      notify("danger", describeBackupError(err));
    }
    refresh();
  }

  return (
    <>
      <PageHeader
        title="Backup & recovery"
        description="Encrypted full-state backups of the database, documents and encryption keys - with proof they can be restored. Restoring always goes to an isolated copy first."
      />

      {state.kind === "loading" && <LoadingState label="Loading backup status…" />}
      {state.kind === "denied" && <PermissionDenied requiredPermission="ViewBackupStatus" />}
      {state.kind === "error" && <ErrorState title="Could not load backup status" action={<Button onClick={() => load(true)}>Retry</Button>} />}

      {state.kind === "loaded" && (
        <>
          <BackupStatusPanel status={state.data.status} />

          {canManage && (
            <section className="alv-config-panel" aria-label="Backup actions">
              <div className="alv-config-panel__form-actions">
                <Button variant="primary" onClick={backupNow} disabled={running || !state.data.status.settings.recoveryKeyConfigured}>
                  {running ? "Backing up…" : "Run backup now"}
                </Button>
                <Button onClick={testNotification}>Send test notification</Button>
              </div>
              {actionError && (
                <p className="alv-form-field__error" role="alert">
                  Backup failed: {actionError}
                </p>
              )}
            </section>
          )}

          {canManage && <RecoveryKeyPanel settings={state.data.status.settings} onChanged={refresh} />}
          {canManage && <BackupSettingsForm settings={state.data.status.settings} onSaved={refresh} />}

          {wizardFor && <RestoreWizard record={wizardFor} onClose={() => setWizardFor(null)} onChanged={refresh} />}

          <BackupHistory records={state.data.history} canManage={canManage} onChanged={refresh} onRestore={setWizardFor} />
          {canManage && <RestoreDrillList drills={state.data.drills} onChanged={refresh} />}
          <NotificationList notifications={state.data.notifications} />
        </>
      )}
    </>
  );
}
