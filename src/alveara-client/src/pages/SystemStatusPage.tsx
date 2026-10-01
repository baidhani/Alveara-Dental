import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { useSystemStatus, usePublicInternetSignal } from "../hooks/useSystemStatus";
import type { SystemStatus } from "../hooks/useSystemStatus";
import "./SystemStatusPage.css";

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
            {status.deployment.state === "ok" ? "Match the data" : status.deployment.state === "mismatch" ? "MISMATCH - the application refuses data requests" : "Not yet checked"}
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
            Last poll: {new Date(status.backgroundRunner.lastPollUtc).toLocaleString()}
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
export function SystemStatusPage() {
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
            {new Date(state.lastConfirmedAtUtc).toLocaleString()}. The most recent check failed,
            timed out, or returned an unexpected response — the values below are not current.
          </div>
          <StatusGrid status={state.lastGoodStatus} internetSignal={internetSignal} />
        </>
      )}

      {state.kind === "loaded" && <StatusGrid status={state.status} internetSignal={internetSignal} />}
    </>
  );
}
