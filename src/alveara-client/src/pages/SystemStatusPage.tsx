import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { useSystemStatus, usePublicInternetSignal } from "../hooks/useSystemStatus";
import type { SystemStatus } from "../hooks/useSystemStatus";
import "./SystemStatusPage.css";
import { showInstant } from "../time/practiceTime";
import { useEffect, useState } from "react";
import { useAuth } from "../contexts/AuthContext";
import { getAdminSystemStatus } from "../services/backupApi";
import type { AdminSystemStatus } from "../services/backupApi";

function StatusDot({ tone }: { tone: "success" | "warning" | "danger" | "unknown" }) {
  return <span className={`status-dot status-dot--${tone}`} aria-hidden="true" />;
}

function StatusGrid({ status, internetSignal }: { status: SystemStatus; internetSignal: boolean }) {
  return (
    <div className="status-grid">
      <div className="status-card">
        <h3>Local server</h3>
        <p>
          <StatusDot tone={status.localServerReachable ? "success" : "danger"} />
          {status.localServerReachable ? "Reachable" : "Unreachable"}
        </p>
      </div>

      <div className="status-card">
        <h3>Public internet</h3>
        <p>
          <StatusDot tone={internetSignal ? "success" : "warning"} />
          {internetSignal ? "Adapter reports connected" : "Adapter reports disconnected"}
        </p>
        <p className="status-caveat">
          This only reflects the browser's own network adapter state, not an actual internet
          reachability probe — it can be wrong. It is independent of local-server reachability: a
          LAN-only connection with no internet still reports connected here.
        </p>
      </div>

      {status.deployment && (
        <div className="status-card">
          <h3>Deployment settings</h3>
          <p>
            <StatusDot tone={status.deployment.state === "ok" ? "success" : status.deployment.state === "mismatch" ? "danger" : "unknown"} />
            {status.deployment.state === "ok" ? "Match the data" : status.deployment.state === "mismatch" ? "MISMATCH - the application refuses data requests" : "Not yet verified - the application refuses data requests until it is"}
          </p>
          {status.deployment.mismatches.length > 0 && (
            <ul>
              {status.deployment.mismatches.map((m) => (
                <li key={m.setting}>
                  Set <code>{m.setting}</code> to <code>{m.recorded}</code> (currently <code>{m.current}</code>), then restart.
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

      <div className="status-card">
        <h3>Database</h3>
        <p>
          <StatusDot tone={status.database.reachable ? "success" : "danger"} />
          {status.database.reachable ? "Reachable" : "Unreachable"}
        </p>
      </div>

      <div className="status-card">
        <h3>Background job runner</h3>
        <p>
          <StatusDot
            tone={
              status.backgroundRunner.status === "healthy"
                ? "success"
                : status.backgroundRunner.status === "stale"
                  ? "warning"
                  : "unknown"
            }
          />
          {status.backgroundRunner.status === "not_yet_polled"
            ? "Not yet polled"
            : status.backgroundRunner.status === "healthy"
              ? "Healthy"
              : "Stale — last poll was too long ago"}
        </p>
        {status.backgroundRunner.lastPollUtc && (
          <p className="status-caveat">
            Last poll: {showInstant(status.backgroundRunner.lastPollUtc)}
          </p>
        )}
      </div>

      <div className="status-card">
        <h3>Application version</h3>
        <p>{status.appVersion}</p>
      </div>
    </div>
  );
}

/**
 * Truthful admin System Status page (ALV-N002). Every value shown here comes from a real check
 * performed at request time — nothing here is an assumed-healthy default — and it explicitly
 * distinguishes "local server unavailable" from "public internet unavailable," which are
 * different failure modes with different causes and different fixes. Per N002-R01-03, it also
 * never silently keeps showing old "healthy" data once a poll starts failing — the "stale" state
 * says so explicitly, with the last time it was actually confirmed.
 */
const GIB = 1024 * 1024 * 1024;
const freeText = (bytes: number | null) => (bytes === null ? "Not known" : `${(bytes / GIB).toFixed(1)} GB free`);
const when = (iso: string | null) => (iso ? showInstant(iso) : "Never");

/**
 * ALV-N026 (D-06, PD-25): the administrator's sections - backup and recovery, storage, staff accounts - from the administrator-only status endpoint. Counts and states only (no paths, keys,
 * usernames or document names). A section the server could not read says so, never "healthy". The backup warning holds until a later success resolves the problem, whatever was acknowledged.
 */
export function AdminStatusSections() {
  const [data, setData] = useState<AdminSystemStatus | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    getAdminSystemStatus(controller.signal).then(setData).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, []);
  if (failed) return <ErrorState title="Backup, storage and account status could not be read" description="Try again in a moment. This does not mean they are healthy." />;
  if (!data) return <LoadingState label="Checking backup, storage and accounts…" />;
  const { backup, storage, accounts, pdfEngine } = data;
  const unavailable = <p><StatusDot tone="unknown" />Could not be read right now; this does not mean it is healthy.</p>;
  return (
    <div className="status-grid" aria-label="Administrator status">
      <div className="status-card">
        <h3>Backup and recovery</h3>
        {backup ? (
          <>
            <p><StatusDot tone={backup.warning ? "danger" : "success"} />{backup.warning ? "Needs attention" : "No problem found"}</p>
            <ul>
              <li>Last good backup: {when(backup.lastGoodBackupUtc)}</li>
              <li>Last restore drill: {when(backup.lastRestoreDrillUtc)}{backup.lastRestoreDrillOutcome ? ` (${backup.lastRestoreDrillOutcome.toLowerCase()})` : ""}</li>
              <li>Recovery key configured: {backup.recoveryKeyConfigured ? "Yes" : "No"}</li>
              <li>Scheduled backups: {backup.scheduleEnabled ? (backup.overdue ? "Overdue" : "On time") : "Off"}</li>
              <li>Unresolved failures: {backup.unresolvedFailures} (not yet acknowledged: {backup.unacknowledgedAlerts})</li>
            </ul>
            {backup.unresolvedFailures > 0 && <p className="status-caveat">A failure stays here until a later backup, verification or restore drill succeeds, even if it was acknowledged.</p>}
          </>
        ) : unavailable}
      </div>
      <div className="status-card">
        <h3>Storage</h3>
        {storage ? (
          <>
            <p><StatusDot tone={!storage.documentFolderReachable || storage.missingDocuments + storage.damagedDocuments > 0 ? "danger" : "success"} />
              {storage.documentFolderReachable ? "Document folder reachable" : "Document folder NOT reachable"}</p>
            <ul>
              <li>Free space: {freeText(storage.freeBytes)}</li>
              <li>Missing documents: {storage.missingDocuments}</li>
              <li>Damaged documents: {storage.damagedDocuments}</li>
            </ul>
          </>
        ) : unavailable}
      </div>
      <div className="status-card">
        <h3>PDF documents</h3>
        {pdfEngine ? (
          <>
            <p><StatusDot tone={pdfEngine.ready ? "success" : "danger"} />{pdfEngine.ready ? "Ready" : "Unavailable"}</p>
            {!pdfEngine.ready && <p className="status-caveat">{pdfEngine.reason} Other work is unaffected; documents cannot be issued as PDFs until this is fixed.</p>}
            <p className="status-caveat">Engine bundle: {pdfEngine.bundleId}</p>
          </>
        ) : unavailable}
      </div>
      <div className="status-card">
        <h3>Staff accounts</h3>
        {accounts ? (
          <ul>
            <li>Enabled: {accounts.enabled}</li>
            <li>Disabled: {accounts.disabled}</li>
            <li>Locked: {accounts.locked}</li>
            <li>Enabled without MFA: {accounts.enabledWithoutMfa}</li>
          </ul>
        ) : unavailable}
      </div>
    </div>
  );
}

export function SystemStatusPage() {
  const { state: auth } = useAuth();
  if (auth.kind !== "signed-in" || auth.role !== "Admin")
    return <ErrorState title="System Status is for administrators" description="Ask a practice administrator if you need to know the state of the system." />;
  return <SystemStatusContent />;
}

function SystemStatusContent() {
  const state = useSystemStatus();
  const internetSignal = usePublicInternetSignal();

  return (
    <>
      <PageHeader
        title="System Status"
        description="Truthful application, server, database, and background-job status. No secrets or patient data appear on this page."
      />

      {state.kind === "loading" && <LoadingState label="Checking system status…" />}

      {state.kind === "error" && (
        <ErrorState
          title="Local server unavailable"
          description="The application shell cannot reach the local server at all right now, so no status could be retrieved. This is different from a public-internet outage — check that the local Windows server is running and reachable on the LAN."
        />
      )}

      {state.kind === "stale" && (
        <>
          <div className="status-stale-banner" role="alert">
            <strong>Status is stale.</strong> The last successful check was at{" "}
            {showInstant(state.lastConfirmedAtUtc)}. The most recent check failed,
            timed out, or returned an unexpected response — the values below are not current.
          </div>
          <StatusGrid status={state.lastGoodStatus} internetSignal={internetSignal} />
        </>
      )}

      {state.kind === "loaded" && <StatusGrid status={state.status} internetSignal={internetSignal} />}

      <h2 className="alv-workspace__section-title">Backup, storage and staff accounts</h2>
      <AdminStatusSections />
    </>
  );
}
