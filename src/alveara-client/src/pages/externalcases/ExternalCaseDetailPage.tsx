import { useCallback, useEffect, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { getCase, getCaseHistory, kindOfRoute } from "../../services/externalCasesApi";
import type { CaseEvent, CaseStatus, ExternalCase } from "../../services/externalCasesApi";
import { CaseDocumentsSection } from "./CaseDocumentsSection";
import { CaseEditForm } from "./CaseEditForm";
import { CaseStatusForm } from "./CaseStatusForm";
import { describeEvent, formatDay, kindLabel, moveLabel, statusLabel } from "./externalCaseFormat";
import "../followups/followups.css";

type Editor = { kind: "move"; to: CaseStatus } | { kind: "edit" } | null;

/**
 * ALV-009-C02: one referral or lab case in full: what it is, who it is with, where it stands, when it went out, came back and is due (OVERDUE said in words), its notes and closure, the statuses it can move to, its linked
 * documents, the follow-up task chasing it in the work queue, and its history with who did each thing. People who may manage cases can move it, edit it and link documents; everyone else reads. A stale change is
 * explained and the case reloaded rather than overwritten; a failed load says it could not load.
 */
export function ExternalCaseDetailPage() {
  const { route, id = "" } = useParams();
  const kind = kindOfRoute(route);
  const { hasPermission } = useAuth();
  const canManage = hasPermission("ManageExternalCases");
  const canSeeDocuments = hasPermission("ViewDocuments");
  const [c, setC] = useState<ExternalCase | null>(null);
  const [events, setEvents] = useState<CaseEvent[]>([]);
  const [failed, setFailed] = useState(false);
  const [editor, setEditor] = useState<Editor>(null);
  const [status, setStatus] = useState<string | null>(null);

  const load = useCallback((signal?: AbortSignal) => {
    if (!kind) return;
    Promise.all([getCase(kind, id, signal), getCaseHistory(kind, id, signal)])
      .then(([loaded, history]) => { setC(loaded); setEvents(history); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [kind, id]);
  useEffect(() => { const controller = new AbortController(); load(controller.signal); return () => controller.abort(); }, [load]);

  if (!kind) return <ErrorState title="That is not a referral or lab case" description="The link may be wrong. Open the list of referrals and lab cases instead." />;
  if (failed && c === null) return <ErrorState title="Could not load the case" description="Check your connection and reload the page." />;
  if (c === null) return <LoadingState label="Loading the case…" />;

  const done = (updated: ExternalCase, message: string) => { setC(updated); setEditor(null); setStatus(message); load(); };
  const reload = () => { setEditor(null); setStatus("Reloaded the case."); load(); };
  const finished = c.status === "Completed" || c.status === "Cancelled";

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-ecd-title">
      <p><Link to="/external-cases">All referrals and lab cases</Link> · <Link to={`/patients/${c.patientId}/external-cases`}>This patient&apos;s referrals and lab cases</Link></p>
      <h1 id="alv-ecd-title" className="alv-workspace__section-title">{c.summary}</h1>
      <p className="alv-clinical__meta">{kindLabel(c.kind)} · {c.partyName}{!c.partyActive && " (no longer active)"}</p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
      {failed && <p role="alert" className="alv-fu-error">Could not refresh the case. What is shown may be out of date.</p>}

      <dl className="alv-fu-facts">
        <dt>Status</dt><dd>{statusLabel(c.status)}</dd>
        <dt>Went out</dt><dd>{formatDay(c.sentOn)}</dd>
        <dt>Came back</dt><dd>{formatDay(c.receivedOn)}</dd>
        <dt>Follow up by</dt><dd>{formatDay(c.dueOn)}{c.overdue && <span className="alv-fu-badge alv-fu-badge--overdue">Overdue</span>}</dd>
        {c.kind === "Lab" && (<><dt>Tooth</dt><dd>{c.toothKey ?? "Not set"}</dd><dt>Material</dt><dd>{c.material ?? "Not set"}</dd><dt>Shade</dt><dd>{c.shade ?? "Not set"}</dd></>)}
        {c.notes && (<><dt>Notes</dt><dd>{c.notes}</dd></>)}
        {finished && (<><dt>{c.status === "Cancelled" ? "Cancelled because" : "Closing note"}</dt><dd>{c.closureNote ?? "None"}</dd></>)}
      </dl>
      {c.openChaseTaskId && <p role="note">The practice is chasing this case: a follow-up task is open in the <Link to="/work-queue">work queue</Link>.</p>}

      {canManage && !finished && editor === null && (
        <div className="alv-fu-actions" role="group" aria-label="Case actions">
          {c.nextStatuses.map((to) => (
            <Button key={to} type="button" variant={to === "Cancelled" ? "danger" : "secondary"} onClick={() => { setEditor({ kind: "move", to }); setStatus(null); }}>{moveLabel(c.kind, c.status, to)}</Button>
          ))}
          <Button type="button" onClick={() => { setEditor({ kind: "edit" }); setStatus(null); }}>Edit</Button>
        </div>
      )}
      {editor?.kind === "move" && <CaseStatusForm c={c} to={editor.to} onCancel={() => setEditor(null)} onStale={reload} onDone={(u) => done(u, `Moved to ${statusLabel(u.status)}.`)} />}
      {editor?.kind === "edit" && <CaseEditForm c={c} onCancel={() => setEditor(null)} onStale={reload} onSaved={(u) => done(u, "Saved the changes.")} />}
      {!canManage && !finished && <p className="alv-clinical__meta">Your role can read this case but cannot change it.</p>}

      {canSeeDocuments
        ? <CaseDocumentsSection c={c} canManage={canManage} onChanged={() => load()} />
        : <p className="alv-clinical__meta">Your role cannot open the document library, so the linked documents are not shown.</p>}

      <section aria-labelledby="alv-ec-history-title">
        <h2 id="alv-ec-history-title">History</h2>
        <ol className="alv-fu-history" aria-label="Case history">
          {events.map((e) => (
            <li key={e.id}>
              <strong>{describeEvent(e)}</strong> by {e.actorName ?? "a former colleague"} · {new Date(e.occurredAtUtc).toLocaleString()}
              {e.note && <span className="alv-fu-item__meta"> — {e.note}</span>}
            </li>
          ))}
        </ol>
      </section>
    </section>
  );
}
