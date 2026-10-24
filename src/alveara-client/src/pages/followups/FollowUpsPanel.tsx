import { useCallback, useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { cancelFollowUp, completeFollowUp, createFollowUp, listAssignees, listForPatient, updateFollowUp } from "../../services/followUpsApi";
import type { Assignee, FollowUp, FollowUpFilter, FollowUpPage } from "../../services/followUpsApi";
import { FollowUpActionForm } from "./FollowUpActionForm";
import { FollowUpForm } from "./FollowUpForm";
import { FollowUpItem } from "./FollowUpItem";
import { PatientFollowUpTimeline } from "./PatientFollowUpTimeline";
import { PatientRecallCard } from "./PatientRecallCard";
import "./followups.css";

type Editor = { kind: "new" } | { kind: "edit"; task: FollowUp } | { kind: "complete"; task: FollowUp } | { kind: "cancel"; task: FollowUp } | null;

const FILTERS: { id: FollowUpFilter; label: string }[] = [{ id: "open", label: "Open" }, { id: "closed", label: "Closed" }, { id: "all", label: "All" }];

/**
 * STORY-009: a patient's follow-up tasks. Everyone who may view follow-ups sees the list and each task's history; people who may manage them also create, edit, complete and cancel. A failed load says it could not
 * load rather than showing an empty list; every change is announced in words and reloads the list; a task someone else changed meanwhile is explained and reloaded rather than overwritten.
 */
export function FollowUpsPanel({ patientId, canManage }: { patientId: string; canManage: boolean }) {
  const { state } = useAuth();
  const [filter, setFilter] = useState<FollowUpFilter>("open");
  const [page, setPage] = useState<FollowUpPage | null>(null);
  const [assignees, setAssignees] = useState<Assignee[]>([]);
  const [failed, setFailed] = useState(false);
  const [editor, setEditor] = useState<Editor>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [version, setVersion] = useState(0);   // bumped after every change so the timeline and the recall card reload

  const load = useCallback((signal?: AbortSignal) => {
    listForPatient(patientId, filter, signal).then((p) => { setPage(p); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patientId, filter]);

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    if (!canManage) return;
    const controller = new AbortController();
    listAssignees(controller.signal).then(setAssignees).catch(() => { /* the form says the list could not be loaded when it is needed */ });
    return () => controller.abort();
  }, [canManage]);

  // new tasks default to the signed-in person (the assignee list names accounts by username)
  const me = state.kind === "signed-in" ? assignees.find((a) => a.name === state.username)?.id : undefined;
  const done = (message: string) => { setEditor(null); setStatus(message); setVersion((v) => v + 1); load(); };
  const reload = () => { setEditor(null); setStatus("Reloaded the follow-ups."); setVersion((v) => v + 1); load(); };

  if (failed && page === null) return <ErrorState title="Could not load the follow-ups" description="Do not assume there are none. Check your connection and reload the page." />;
  if (page === null) return <LoadingState label="Loading the follow-ups…" />;

  return (
    <section className="alv-clinical alv-fu" aria-labelledby="alv-fu-title">
      <h2 id="alv-fu-title" className="alv-workspace__section-title">Follow-ups</h2>
      <p className="alv-clinical__meta">
        Tasks that keep this patient&apos;s care continuous. The responsible person is reminded when a task is due. Every action is recorded with who did it and when.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canManage ? "Nothing is saved until you create or change a follow-up." : "You can read follow-ups but your role cannot change them.")}</p>
      {failed && <p role="alert" className="alv-fu-error">Could not refresh the follow-ups. What is shown may be out of date.</p>}

      <div className="alv-fu-toolbar">
        <div role="group" aria-label="Show">
          {FILTERS.map((f) => (
            <Button key={f.id} type="button" variant={filter === f.id ? "primary" : "secondary"} aria-pressed={filter === f.id} onClick={() => { setFilter(f.id); setStatus(null); }}>{f.label}</Button>
          ))}
        </div>
        {canManage && editor === null && <Button type="button" variant="primary" onClick={() => { setEditor({ kind: "new" }); setStatus(null); }}>New follow-up</Button>}
      </div>

      {editor?.kind === "new" && (
        <FollowUpForm assignees={assignees} defaultAssignee={me} onCancel={() => setEditor(null)} onStale={reload}
          onSubmit={async (input, key) => { const r = await createFollowUp(patientId, input, key); done(r.replayed ? "That follow-up was already created." : `Created the follow-up: ${r.task.title}.`); }} />
      )}
      {editor?.kind === "edit" && (
        <FollowUpForm initial={editor.task} assignees={assignees} onCancel={() => setEditor(null)} onStale={reload}
          onSubmit={async (input) => { const r = await updateFollowUp(editor.task.id, input, editor.task.rowVersion); done(`Saved the changes to: ${r.task.title}.`); }} />
      )}
      {editor?.kind === "complete" && (
        <FollowUpActionForm mode="complete" task={editor.task} onCancel={() => setEditor(null)} onStale={reload}
          onSubmit={async (note) => { const r = await completeFollowUp(editor.task.id, note, editor.task.rowVersion); done(r.recallTaskId ? `Completed: ${r.task.title}. The next follow-up was created.` : `Completed: ${r.task.title}.`); }} />
      )}
      {editor?.kind === "cancel" && (
        <FollowUpActionForm mode="cancel" task={editor.task} onCancel={() => setEditor(null)} onStale={reload}
          onSubmit={async (note) => { await cancelFollowUp(editor.task.id, note ?? "", editor.task.rowVersion); done(`Cancelled: ${editor.task.title}.`); }} />
      )}

      {page.items.length === 0
        ? <EmptyState title={filter === "open" ? "No open follow-ups" : filter === "closed" ? "No closed follow-ups" : "No follow-ups"} description={canManage && filter === "open" ? "Use New follow-up to add one." : undefined} />
        : (
          <ul className="alv-fu-list" aria-label={`${filter} follow-ups`}>
            {page.items.map((t) => (
              <FollowUpItem key={t.id} task={t} canManage={canManage}
                onEdit={(task) => { setEditor({ kind: "edit", task }); setStatus(null); }} onComplete={(task) => { setEditor({ kind: "complete", task }); setStatus(null); }} onCancel={(task) => { setEditor({ kind: "cancel", task }); setStatus(null); }} />
            ))}
          </ul>
        )}
      {page.more && <p className="alv-clinical__meta">Showing the first {page.count}. Narrow the list with Open or Closed.</p>}

      <PatientRecallCard key={`recall-${version}`} patientId={patientId} canManage={canManage} onChanged={() => setVersion((v) => v + 1)} />
      <PatientFollowUpTimeline patientId={patientId} version={version} />
    </section>
  );
}
