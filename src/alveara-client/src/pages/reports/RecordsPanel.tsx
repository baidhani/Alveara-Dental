import { useState } from "react";
import type { ReactNode } from "react";
import { Button } from "../../components/Button";
import { SafeLink } from "../../components/SafeLink";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { getAppointments, getFollowUpTasks, reportErrorMessage } from "../../services/reportsApi";
import type { AppointmentCursor, AppointmentDrill, AppointmentRecord, FollowUpCursor, FollowUpDrill, FollowUpRecord, ReportQuery } from "../../services/reportsApi";
import { formatMoment } from "../exports/exportFormat";
import { words } from "./reportFormat";
import { useReport } from "./useReport";

interface Column<R> { header: string; cell: (row: R) => ReactNode }

interface PanelProps<R, C> {
  title: string;
  load: (after: C | null, signal: AbortSignal) => Promise<{ total: number; rows: R[]; next: C | null }>;
  columns: Column<R>[];
  rowKey: (row: R) => string;
  onClose: () => void;
}

/**
 * The records behind a number. The first page arrives with the total (the number that was pressed), and "Show more" adds the next page after the last record shown, so a record is never repeated or skipped. The list
 * shows ids, times and statuses, never a name or a note; a patient is a link to the record for a person who may open patient records. The caller gives this a new key for each number, so a new question starts fresh.
 */
function RecordsPanel<R, C>({ title, load, columns, rowKey, onClose }: PanelProps<R, C>) {
  const { outcome, again } = useReport((signal) => load(null, signal), title);
  const [more, setMore] = useState<{ rows: R[]; next: C | null } | null>(null);
  const [moreError, setMoreError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const first = outcome?.ok ? outcome.data : null;
  const rows = [...(first?.rows ?? []), ...(more?.rows ?? [])];
  const next = more ? more.next : first?.next ?? null;

  const showMore = async () => {
    setBusy(true); setMoreError(null);
    try {
      const page = await load(next, new AbortController().signal);
      setMore({ rows: [...(more?.rows ?? []), ...page.rows], next: page.next });
    } catch (e) {
      setMoreError(reportErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  return (
    <section className="alv-rep__records" aria-label={`Records: ${title}`}>
      <div className="alv-rep__records-head">
        <h3>{title}</h3>
        <Button onClick={onClose}>Close the list</Button>
      </div>
      {outcome === null && <LoadingState label="Loading the records…" />}
      {outcome && !outcome.ok && <ErrorState title="The records could not be shown" description={outcome.message} action={<Button onClick={again}>Try again</Button>} />}
      {first && (
        <>
          <p>{first.total.toLocaleString()} {first.total === 1 ? "record" : "records"}; showing {rows.length.toLocaleString()}.</p>
          <table className="alv-rep__table">
            <caption className="alv-rep__sr">{title}</caption>
            <thead><tr>{columns.map((c) => <th key={c.header} scope="col">{c.header}</th>)}</tr></thead>
            <tbody>{rows.map((r) => <tr key={rowKey(r)}>{columns.map((c) => <td key={c.header}>{c.cell(r)}</td>)}</tr>)}</tbody>
          </table>
          {moreError && <p role="alert" className="alv-rep__problem">{moreError}</p>}
          {next && <Button onClick={showMore} disabled={busy}>{busy ? "Loading…" : "Show more"}</Button>}
        </>
      )}
    </section>
  );
}

function PatientLink({ id }: { id: string }) {
  const { hasPermission } = useAuth();
  return hasPermission("ViewPatientRecords") ? <SafeLink to={`/patients/${id}`}>Open patient record</SafeLink> : <span>Patient record (you cannot open it)</span>;
}

export function AppointmentRecords({ query, drill, title, timeZone, onClose }: { query: ReportQuery; drill: AppointmentDrill; title: string; timeZone: string; onClose: () => void }) {
  return (
    <RecordsPanel<AppointmentRecord, AppointmentCursor>
      title={title} onClose={onClose} rowKey={(r) => r.id}
      load={(after, signal) => getAppointments(query, { ...drill, after }, signal)}
      columns={[
        { header: "Start", cell: (r) => formatMoment(r.startUtc, timeZone) },
        { header: "Length", cell: (r) => `${r.durationMinutes} min` },
        { header: "Booking", cell: (r) => words(r.status) },
        { header: "Visit", cell: (r) => (r.status === "Scheduled" ? words(r.flowState) : "Not a visit") },
        { header: "Patient", cell: (r) => <PatientLink id={r.patientId} /> },
      ]}
    />
  );
}

export function FollowUpRecords({ query, drill, title, timeZone, onClose }: { query: ReportQuery; drill: FollowUpDrill; title: string; timeZone: string; onClose: () => void }) {
  return (
    <RecordsPanel<FollowUpRecord, FollowUpCursor>
      title={title} onClose={onClose} rowKey={(r) => r.id}
      load={(after, signal) => getFollowUpTasks(query, { ...drill, after }, signal)}
      columns={[
        { header: "Due", cell: (r) => formatMoment(r.dueAtUtc, timeZone) },
        { header: "Kind", cell: (r) => words(r.kind) },
        { header: "State", cell: (r) => words(r.status) },
        { header: "Priority", cell: (r) => r.priority },
        { header: "Closed", cell: (r) => (r.closedAtUtc ? `${formatMoment(r.closedAtUtc, timeZone)}${r.closedBySystem ? " (by the system)" : ""}` : "Open") },
        { header: "Owner", cell: (r) => (r.assigneeUserId ? "Assigned" : "Unassigned") },
        { header: "Patient", cell: (r) => <PatientLink id={r.patientId} /> },
      ]}
    />
  );
}
