import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { listCases, routeOf } from "../../services/externalCasesApi";
import type { CaseKind, CasePage } from "../../services/externalCasesApi";
import { formatDay, kindLabel, statusLabel } from "./externalCaseFormat";
import "../followups/followups.css";

type View = "open" | "overdue" | "closed" | "all";
const PAGE_SIZE = 25;

/**
 * ALV-009-C02: the referral and lab case list. The chips say how many cases each view holds (open, overdue, finished, all); a kind and a status narrow it; each row names the patient (a link to their referral and lab page),
 * the outside party, the status and the due date in words, with OVERDUE said as text beside the date and never by colour alone. A failed load says it could not load rather than showing an empty list.
 */
export function ExternalCasesPage() {
  const [view, setView] = useState<View>("open");
  const [kind, setKind] = useState<CaseKind | "">("");
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<CasePage | null>(null);
  const [failed, setFailed] = useState(false);

  const load = useCallback((signal?: AbortSignal) => {
    const statusFilter = status || (view === "closed" ? "closed" : view === "all" ? "all" : "open");
    listCases({ kind, status: statusFilter, overdue: view === "overdue", page, pageSize: PAGE_SIZE }, signal)
      .then((d) => { setData(d); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [view, kind, status, page]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  if (failed && data === null) return <ErrorState title="Could not load the cases" description="Do not assume nothing is outstanding. Check your connection and reload the page." />;
  if (data === null) return <LoadingState label="Loading referrals and lab cases…" />;

  const choose = (next: View) => { setView(next); setStatus(""); setPage(1); };
  const first = (data.page - 1) * data.pageSize + 1;
  const last = Math.min(data.page * data.pageSize, data.total);
  const chips: { id: View; label: string; count: number }[] = [
    { id: "open", label: "Open", count: data.counts.open }, { id: "overdue", label: "Overdue", count: data.counts.overdue },
    { id: "closed", label: "Finished", count: data.counts.closed }, { id: "all", label: "All", count: data.counts.open + data.counts.closed },
  ];

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-ec-title">
      <h1 id="alv-ec-title" className="alv-workspace__section-title">Referrals and lab cases</h1>
      <p className="alv-clinical__meta">Work sent to outside providers and dental laboratories. A case that has gone out and passed its due date is overdue and also appears in the work queue. Start a new case from the patient&apos;s own page.</p>
      {failed && <p role="alert" className="alv-fu-error">Could not refresh the list. What is shown may be out of date.</p>}

      <div role="group" aria-label="Case view" className="alv-fu-chips">
        {chips.map((c) => (
          <Button key={c.id} type="button" variant={view === c.id ? "primary" : "secondary"} aria-pressed={view === c.id} onClick={() => choose(c.id)}>{c.label} ({c.count})</Button>
        ))}
      </div>
      <div className="alv-fu-narrow">
        <label>Kind <select value={kind} onChange={(e) => { setKind(e.target.value as CaseKind | ""); setPage(1); }}>
          <option value="">Both</option><option value="Referral">Referrals</option><option value="Lab">Lab cases</option></select></label>
        <label>Status <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}>
          <option value="">Any in this view</option>
          {["Draft", "Sent", "InProgress", "Received", "Completed", "Cancelled"].map((s) => <option key={s} value={s}>{statusLabel(s)}</option>)}</select></label>
      </div>

      {data.items.length === 0 ? <EmptyState title="No cases in this view" description="Try another view or status." /> : (
        <table className="alv-fu-table">
          <caption className="alv-fu-sr">Referrals and lab cases, {data.total} case{data.total === 1 ? "" : "s"}</caption>
          <thead><tr><th scope="col">Case</th><th scope="col">Patient</th><th scope="col">Outside party</th><th scope="col">Status</th><th scope="col">Due</th></tr></thead>
          <tbody>
            {data.items.map(({ case: c, patientName }) => (
              <tr key={c.id}>
                <td><Link to={`/external-cases/${routeOf(c.kind)}/${c.id}`}><strong>{c.summary}</strong></Link><br /><span className="alv-fu-item__meta">{kindLabel(c.kind)}</span></td>
                <td><Link to={`/patients/${c.patientId}/external-cases`}>{patientName || "Open the patient"}</Link></td>
                <td>{c.partyName}{!c.partyActive && <span className="alv-fu-item__meta"> (no longer active)</span>}</td>
                <td>{statusLabel(c.status)}</td>
                <td>{formatDay(c.dueOn)}{c.overdue && <span className="alv-fu-badge alv-fu-badge--overdue">Overdue</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {data.total > 0 && (
        <p className="alv-fu-pager">
          Showing {first} to {last} of {data.total}.{" "}
          <Button type="button" disabled={data.page <= 1} onClick={() => setPage(data.page - 1)}>Previous</Button>{" "}
          <Button type="button" disabled={last >= data.total} onClick={() => setPage(data.page + 1)}>Next</Button>
        </p>
      )}
    </section>
  );
}
