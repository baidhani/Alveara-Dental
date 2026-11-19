import { useCallback, useContext, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { PageHeader } from "../components/PageHeader";
import { LoadingState, EmptyState, ErrorState } from "../components/StatePatterns";
import { PermissionDenied } from "../components/PermissionDenied";
import { Button } from "../components/Button";
import { ApiError, AUDIT_LOG_WINDOW, getAuditEventTypes, getAuditLog } from "../services/authApi";
import type { AuditLogEntry, AuditLogFilter } from "../services/authApi";
import { AuthContext } from "../contexts/authContextObject";
import "./AuditLogPage.css";
import { showInstant } from "../time/practiceTime";

type PageState =
  | { kind: "loading" }
  | { kind: "permission-denied" }
  | { kind: "error" }
  | { kind: "loaded"; entries: AuditLogEntry[] };

/** A day typed in the filter (yyyy-mm-dd, the browser's own date field) as the start or end of that day in UTC; empty means no bound. */
const dayBound = (day: string, end: boolean) => (day ? new Date(`${day}T${end ? "23:59:59.999" : "00:00:00.000"}`).toISOString() : undefined);

/**
 * ALV-002-C01 (review finding ALV-002-C01-R01-01): the basic permission-aware audit/history viewer - reads GET /api/auth/audit-log. Gated at the route level by
 * RequirePermission(ViewAuditLog) (see App.tsx) and again here: a 403 from the server itself renders PermissionDenied.
 *
 * ALV-N025 (PKG-10 group A): people are named (the actor-name rule; the ids stay in a tooltip, F-149); the heading says what the log holds (F-150, F-174); it filters by
 * event type and date range; a patient edit names its fields and, for a viewer who may see patients, links to that patient's History, where the old and new values live
 * (F-041). A viewer without ViewPatientRecords gets no link - and the History route and API refuse them anyway (BR-02).
 */
export function AuditLogPage() {
  // read without requiring a provider: no auth context means no patient access, so no History link (the safe default)
  const canSeePatients = useContext(AuthContext)?.hasPermission("ViewPatientRecords") ?? false;
  const [state, setState] = useState<PageState>({ kind: "loading" });
  const [types, setTypes] = useState<string[]>([]);
  const [eventType, setEventType] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");

  const load = useCallback(async (filter: AuditLogFilter = {}) => {
    setState({ kind: "loading" });
    try {
      const entries = await getAuditLog(filter);
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
    getAuditEventTypes().then((t) => setTypes(Array.isArray(t) ? t.filter((x): x is string => typeof x === "string") : [])).catch(() => setTypes([]));
  }, [load]);

  const apply = () => load({ eventType: eventType || undefined, fromUtc: dayBound(from, false), toUtc: dayBound(to, true) });

  return (
    <>
      <PageHeader
        title="Audit log"
        description={`Who did what, and when - the most recent ${AUDIT_LOG_WINDOW} recorded actions of every kind (sign-ins, accounts and permissions, patients, clinical records, billing, documents, reports and more). Entries hold no patient details and cannot be edited or deleted once written.`}
      />

      <form className="alv-audit-log__filters" aria-label="Filter the audit log" onSubmit={(e) => { e.preventDefault(); apply(); }}>
        <label htmlFor="audit-type">Event
          <select id="audit-type" value={eventType} onChange={(e) => setEventType(e.target.value)}>
            <option value="">All events</option>
            {types.map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>
        <label htmlFor="audit-from">From
          <input id="audit-from" type="date" value={from} onChange={(e) => setFrom(e.target.value)} />
        </label>
        <label htmlFor="audit-to">To
          <input id="audit-to" type="date" value={to} onChange={(e) => setTo(e.target.value)} />
        </label>
        <Button type="submit">Show</Button>
      </form>

      {state.kind === "loading" && <LoadingState label="Loading audit log…" />}
      {state.kind === "permission-denied" && <PermissionDenied requiredPermission="ViewAuditLog" />}
      {state.kind === "error" && <ErrorState title="Could not load the audit log" action={<Button onClick={() => load()}>Retry</Button>} />}

      {state.kind === "loaded" && state.entries.length === 0 && (
        <EmptyState title="No audit events yet" description="No recorded action matches. Change the filter, or check back as things happen." />
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
                <th>About</th>
                <th>Performed by</th>
                <th>Details</th>
              </tr>
            </thead>
            <tbody>
              {state.entries.map((entry) => (
                <tr key={entry.id}>
                  <td>{showInstant(entry.timestampUtc)}</td>
                  <td>{entry.eventType}</td>
                  <td>
                    {entry.entityType ?? "Account"}{" "}
                    {entry.targetName
                      ? <span title={entry.targetUserAccountId}>{entry.targetName}</span>
                      : <span className="alv-audit-log__id">{entry.targetUserAccountId}</span>}
                  </td>
                  <td>
                    {entry.performedByUserAccountId === null
                      ? <span className="alv-audit-log__self">self</span>
                      : <span title={entry.performedByUserAccountId}>{entry.performedByName ?? entry.performedByUserAccountId}</span>}
                  </td>
                  <td>
                    {entry.details}
                    {entry.historyEntryId && (canSeePatients
                      ? <> <Link to={`/patients/${entry.targetUserAccountId}/history`}>View in patient history</Link></>
                      : <span className="alv-audit-log__note"> History is available to staff who may see this patient.</span>)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
