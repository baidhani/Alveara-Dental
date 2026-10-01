import { useCallback, useEffect, useState } from "react";
import { PageHeader } from "../components/PageHeader";
import { LoadingState, EmptyState, ErrorState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
import { Button } from "../components/Button";
import { ApiError, AUDIT_LOG_WINDOW, getAuditLog } from "../services/authApi";
import type { AuditLogEntry } from "../services/authApi";
import "./AuditLogPage.css";

type PageState =
  | { kind: "loading" }
  | { kind: "permission-denied" }
  | { kind: "error" }
  | { kind: "loaded"; entries: AuditLogEntry[] };

/**
 * ALV-002-C01 (review finding ALV-002-C01-R01-01): the basic permission-aware audit/history viewer
 * for records/actions that already exist - reads GET /api/auth/audit-log (built in STORY-002,
 * extended with entity/reason/correlation metadata by this story's shared AuditService). Gated at
 * the route level by RequirePermission(ViewAuditLog) (see App.tsx) and again here: a 403 from the
 * server itself (not just a hidden nav link) renders PermissionDenied, matching every other
 * admin screen's own-independent-enforcement pattern (PermissionMatrixPage, AdminUsersPage).
 */
export function AuditLogPage() {
  const [state, setState] = useState<PageState>({ kind: "loading" });

  const load = useCallback(async () => {
    setState({ kind: "loading" });
    try {
      const entries = await getAuditLog();
      setState({ kind: "loaded", entries });
    } catch (err) {
      if (err instanceof ApiError && err.status === 403) {
        setState({ kind: "permission-denied" });
      } else {
        setState({ kind: "error" });
      }
    }
  }, []);

  useEffect(() => {
    load();
  }, [load]);

  return (
    <>
      <PageHeader
        title="Audit log"
        description={`Who did what, and when - the most recent ${AUDIT_LOG_WINDOW} account, role, and session events. Entries cannot be edited or deleted once written.`}
      />

      {state.kind === "loading" && <LoadingState label="Loading audit log…" />}
      {state.kind === "permission-denied" && <PermissionDenied requiredPermission="ViewAuditLog" />}
      {state.kind === "error" && <ErrorState title="Could not load the audit log" action={<Button onClick={load}>Retry</Button>} />}

      {state.kind === "loaded" && state.entries.length === 0 && (
        <EmptyState title="No audit events yet" description="Account, role, and session changes will appear here as they happen." />
      )}

      {state.kind === "loaded" && state.entries.length > 0 && (
        <div className="alv-audit-log-scroll" role="region" aria-label="Audit log entries (scrollable table)" tabIndex={0}>
          {/* Gate A review GATE-A-02 (ALV-002-C01 R04): the table scrolls horizontally, so this region must be reachable and
              scrollable from the keyboard (axe scrollable-region-focusable) and announced as a named region. */}
          <table className="alv-audit-log">
            <thead>
              <tr>
                <th>Time</th>
                <th>Event</th>
                <th>Entity</th>
                <th>Performed by</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {state.entries.map((entry) => (
                <tr key={entry.id}>
                  <td>{new Date(entry.timestampUtc).toLocaleString()}</td>
                  <td>{entry.eventType}</td>
                  <td>
                    {entry.entityType ?? "—"} <span className="alv-audit-log__id">{entry.targetUserAccountId}</span>
                  </td>
                  <td>{entry.performedByUserAccountId ?? <span className="alv-audit-log__self">self</span>}</td>
                  <td>{entry.details}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
