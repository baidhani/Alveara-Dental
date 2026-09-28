import { PageHeader } from "../components/PageHeader";
import { LoadingState, ErrorState } from "../components/StatePatterns";
import { useSystemStatus, usePublicInternetSignal } from "../hooks/useSystemStatus";
import "./SystemStatusPage.css";

function StatusDot({ tone }: { tone: "success" | "warning" | "danger" | "unknown" }) {
  return <span className={`status-dot status-dot--${tone}`} aria-hidden="true" />;
}

/**
 * Truthful admin System Status page (ALV-N002). Every value shown here comes from a real check
 * performed at request time — nothing here is an assumed-healthy default — and it explicitly
 * distinguishes "local server unavailable" from "public internet unavailable," which are
 * different failure modes with different causes and different fixes.
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

      {state.kind === "loaded" && (
        <div className="status-grid">
          <div className="status-card">
            <h3>Local server</h3>
            <p>
              <StatusDot tone={state.status.localServerReachable ? "success" : "danger"} />
              {state.status.localServerReachable ? "Reachable" : "Unreachable"}
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
              reachability probe — it can be wrong. It is independent of local-server reachability:
              a LAN-only connection with no internet still reports connected here.
            </p>
          </div>

          <div className="status-card">
            <h3>Database</h3>
            <p>
              <StatusDot tone={state.status.database.reachable ? "success" : "danger"} />
              {state.status.database.reachable ? "Reachable" : "Unreachable"}
            </p>
          </div>

          <div className="status-card">
            <h3>Background job runner</h3>
            <p>
              <StatusDot
                tone={
                  state.status.backgroundRunner.status === "healthy"
                    ? "success"
                    : state.status.backgroundRunner.status === "stale"
                      ? "warning"
                      : "unknown"
                }
              />
              {state.status.backgroundRunner.status === "not_yet_polled"
                ? "Not yet polled"
                : state.status.backgroundRunner.status === "healthy"
                  ? "Healthy"
                  : "Stale — last poll was too long ago"}
            </p>
            {state.status.backgroundRunner.lastPollUtc && (
              <p className="status-caveat">
                Last poll: {new Date(state.status.backgroundRunner.lastPollUtc).toLocaleString()}
              </p>
            )}
          </div>

          <div className="status-card">
            <h3>Application version</h3>
            <p>{state.status.appVersion}</p>
          </div>
        </div>
      )}
    </>
  );
}
