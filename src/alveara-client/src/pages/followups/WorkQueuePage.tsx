import { useCallback, useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { cancelFollowUp, completeFollowUp, listAssignees } from "../../services/followUpsApi";
import type { Assignee, FollowUp, FollowUpKind, FollowUpPriority } from "../../services/followUpsApi";
import { claimFollowUp, getWorkQueue, isStaleQueueChange, KINDS, kindLabel, PRIORITIES, queueErrorMessage } from "../../services/workQueueApi";
import type { QueueFilter, QueuePage } from "../../services/workQueueApi";
import { FollowUpActionForm } from "./FollowUpActionForm";
import { QueueRowEditor } from "./QueueRowEditor";
import { formatWhen } from "./followUpFormat";
import "./followups.css";

export const QUEUE_REFRESH_MS = 60_000;

type Editor = { kind: "owner" | "complete" | "cancel"; task: FollowUp } | null;

const FILTERS: { id: QueueFilter; label: string; count: (c: QueuePage["counts"]) => number }[] = [
  { id: "overdue", label: "Overdue", count: (c) => c.overdue },
  { id: "duesoon", label: "Due soon", count: (c) => c.dueSoon },
  { id: "unassigned", label: "Unassigned", count: (c) => c.unassigned },
  { id: "mine", label: "Mine", count: (c) => c.mine },
  { id: "all", label: "All open", count: (c) => c.all },
];

const PAGE_SIZE = 25;

/**
 * ALV-009-C01: the work queue. Everyone who may view follow-ups sees the open work, most urgent first, filtered by the chips (each says how many it would show); people who may manage follow-ups can also claim a task, change its
 * owner and priority, complete it or cancel it right there. Overdue and priority are said in words as well as shown in colour. A failed load says it could not load rather than showing an empty queue; a task someone else changed
 * meanwhile is explained and the queue reloaded rather than overwritten; the list refreshes itself every minute unless a form is open or the page is hidden.
 */
export function WorkQueuePage() {
  const { hasPermission } = useAuth();
  const canManage = hasPermission("ManageFollowUps");
  const [filter, setFilter] = useState<QueueFilter>("all");
  const [priority, setPriority] = useState<FollowUpPriority | "">("");
  const [kind, setKind] = useState<FollowUpKind | "">("");
  const [owner, setOwner] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<QueuePage | null>(null);
  const [assignees, setAssignees] = useState<Assignee[]>([]);
  const [failed, setFailed] = useState(false);
  const [editor, setEditor] = useState<Editor>(null);
  const [status, setStatus] = useState<string | null>(null);
  const editing = useRef(false);
  useEffect(() => { editing.current = editor !== null; }, [editor]);

  const load = useCallback((signal?: AbortSignal) => {
    getWorkQueue({ filter, priority, kind, owner: owner || undefined, page, pageSize: PAGE_SIZE }, signal)
      .then((d) => { setData(d); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [filter, priority, kind, owner, page]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    const timer = window.setInterval(() => { if (!editing.current && document.visibilityState !== "hidden") load(); }, QUEUE_REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [load]);

  useEffect(() => {
    if (!canManage) return;
    const controller = new AbortController();
    listAssignees(controller.signal).then(setAssignees).catch(() => { /* the owner form says the list could not be loaded when it is needed */ });
    return () => controller.abort();
  }, [canManage]);

  const done = (message: string) => { setEditor(null); setStatus(message); load(); };
  const reload = () => { setEditor(null); setStatus("Reloaded the queue."); load(); };
  const choose = (next: QueueFilter) => { setFilter(next); setPage(1); setStatus(null); };

  async function claim(task: FollowUp) {
    setStatus(null);
    try { await claimFollowUp(task.id, task.rowVersion); done(`You took: ${task.title}.`); }
    catch (err) { setStatus(isStaleQueueChange(err) ? `${queueErrorMessage(err)} The queue was reloaded.` : queueErrorMessage(err)); if (isStaleQueueChange(err)) load(); }
  }

  if (failed && data === null) return <ErrorState title="Could not load the work queue" description="Do not assume there is nothing to do. Check your connection and reload the page." />;
  if (data === null) return <LoadingState label="Loading the work queue…" />;

  const first = (data.page - 1) * data.pageSize + 1;
  const last = Math.min(data.page * data.pageSize, data.total);

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-wq-title">
      <h1 id="alv-wq-title" className="alv-workspace__section-title">Work queue</h1>
      <p className="alv-clinical__meta">
        Open follow-ups, recalls and accepted treatment nobody has scheduled yet, most urgent first. {canManage ? "Take a task, change who owns it or how urgent it is, or close it from here." : "You can read the queue but your role cannot change tasks."}
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
      {failed && <p role="alert" className="alv-fu-error">Could not refresh the queue. What is shown may be out of date.</p>}

      <div role="group" aria-label="Queue filter" className="alv-fu-chips">
        {FILTERS.map((f) => (
          <Button key={f.id} type="button" variant={filter === f.id ? "primary" : "secondary"} aria-pressed={filter === f.id} onClick={() => choose(f.id)}>{f.label} ({f.count(data.counts)})</Button>
        ))}
      </div>
      <div className="alv-fu-narrow">
        <label>Priority <select value={priority} onChange={(e) => { setPriority(e.target.value as FollowUpPriority | ""); setPage(1); }}>
          <option value="">Any</option>{PRIORITIES.map((p) => <option key={p} value={p}>{p}</option>)}</select></label>
        <label>Kind <select value={kind} onChange={(e) => { setKind(e.target.value as FollowUpKind | ""); setPage(1); }}>
          <option value="">Any</option>{KINDS.map((k) => <option key={k.id} value={k.id}>{k.label}</option>)}</select></label>
        <label>Owner <select value={owner} onChange={(e) => { setOwner(e.target.value); setPage(1); }}>
          <option value="">Anyone</option><option value="none">Unassigned</option>{assignees.map((a) => <option key={a.id} value={a.id}>{a.name}</option>)}</select></label>
      </div>

      {editor?.kind === "owner" && <QueueRowEditor task={editor.task} assignees={assignees} onSaved={done} onCancel={() => setEditor(null)} onStale={reload} />}
      {editor?.kind === "complete" && (
        <FollowUpActionForm mode="complete" task={editor.task} onCancel={() => setEditor(null)} onStale={reload}
          onSubmit={async (note) => { const r = await completeFollowUp(editor.task.id, note, editor.task.rowVersion); done(`Completed: ${r.task.title}.`); }} />
      )}
      {editor?.kind === "cancel" && (
        <FollowUpActionForm mode="cancel" task={editor.task} onCancel={() => setEditor(null)} onStale={reload}
          onSubmit={async (note) => { await cancelFollowUp(editor.task.id, note ?? "", editor.task.rowVersion); done(`Cancelled: ${editor.task.title}.`); }} />
      )}

      {data.items.length === 0 ? <EmptyState title="Nothing in this view" description="Try another filter." /> : (
        <table className="alv-fu-table">
          <caption className="alv-fu-sr">Work queue, {data.total} task{data.total === 1 ? "" : "s"}</caption>
          <thead><tr><th scope="col">Priority</th><th scope="col">Task</th><th scope="col">Patient</th><th scope="col">Due</th><th scope="col">Owner</th>{canManage && <th scope="col">Actions</th>}</tr></thead>
          <tbody>
            {data.items.map((item) => {
              const t = item.task;
              return (
                <tr key={t.id}>
                  <td><span className={`alv-fu-badge alv-fu-badge--${t.priority.toLowerCase()}`}>{t.priority}</span></td>
                  <td><strong>{t.title}</strong><br /><span className="alv-fu-item__meta">{kindLabel(t.kind)}{t.status !== "Open" ? ` · ${t.status}` : ""}</span></td>
                  <td><Link to={`/patients/${t.patientId}/follow-ups`}>{item.patientName || "Open the patient"}</Link></td>
                  <td>{formatWhen(t.dueAtUtc)}{item.urgency === "Overdue" && <span className="alv-fu-badge alv-fu-badge--overdue">Overdue</span>}{item.urgency === "DueSoon" && <span className="alv-fu-badge">Due soon</span>}
                    {item.appointmentStartUtc && <><br /><span className="alv-fu-item__meta">Appointment {formatWhen(item.appointmentStartUtc)}</span></>}</td>
                  <td>{t.assigneeUserId === null ? "Unassigned" : t.assigneeName ?? "A former colleague"}</td>
                  {canManage && (
                    <td className="alv-fu-actions">
                      {t.status === "Open" && (
                        <>
                          {t.assigneeUserId === null && <Button type="button" onClick={() => claim(t)} aria-label={`Take ${t.title}`}>Take</Button>}
                          <Button type="button" onClick={() => { setEditor({ kind: "owner", task: t }); setStatus(null); }} aria-label={`Change owner or priority of ${t.title}`}>Owner and priority</Button>
                          <Button type="button" onClick={() => { setEditor({ kind: "complete", task: t }); setStatus(null); }} aria-label={`Complete ${t.title}`}>Complete</Button>
                          <Button type="button" variant="danger" onClick={() => { setEditor({ kind: "cancel", task: t }); setStatus(null); }} aria-label={`Cancel ${t.title}`}>Cancel</Button>
                        </>
                      )}
                    </td>
                  )}
                </tr>
              );
            })}
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
