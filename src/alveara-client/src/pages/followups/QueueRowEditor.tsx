import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import type { Assignee, FollowUp, FollowUpPriority } from "../../services/followUpsApi";
import { assignFollowUp, isStaleQueueChange, PRIORITIES, queueErrorMessage, setFollowUpPriority } from "../../services/workQueueApi";
import "./followups.css";

interface Props {
  task: FollowUp;
  assignees: Assignee[];
  onSaved: (message: string) => void;
  onCancel: () => void;
  /** The server says the task changed meanwhile: the screen reloads and closes this form. */
  onStale: () => void;
}

/**
 * ALV-009-C01: change who owns a task and how urgent it is, in one explicit save (a select that acted on every arrow key would reassign a task by accident). Owner first, then priority, each with the version the
 * previous one returned, so the two changes cannot overwrite each other; if either is refused or the task changed meanwhile, the person is told in words and offered a reload.
 */
export function QueueRowEditor({ task, assignees, onSaved, onCancel, onStale }: Props) {
  const [owner, setOwner] = useState(task.assigneeUserId ?? "");
  const [priority, setPriority] = useState<FollowUpPriority>(task.priority);
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);

  const ownerChanged = owner !== (task.assigneeUserId ?? "");
  const priorityChanged = priority !== task.priority;
  const known = assignees.some((a) => a.id === owner);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy || (!ownerChanged && !priorityChanged)) return;
    setBusy(true); setError(null);
    try {
      let version = task.rowVersion;
      if (ownerChanged) version = (await assignFollowUp(task.id, owner === "" ? null : owner, version)).task.rowVersion;
      if (priorityChanged) await setFollowUpPriority(task.id, priority, version);
      onSaved(`Saved: ${task.title}.`);
    } catch (err) {
      if (isStaleQueueChange(err)) setStale(true);
      setError(queueErrorMessage(err));
    } finally { setBusy(false); }
  }

  return (
    <form className="alv-fu-form" onSubmit={submit} aria-labelledby={`qre-title-${task.id}`} noValidate>
      <h2 id={`qre-title-${task.id}`} tabIndex={-1} ref={heading}>Owner and priority: {task.title}</h2>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={onStale}>Reload the queue</Button>}
      <div className="alv-form-field">
        <label htmlFor={`qre-owner-${task.id}`} className="alv-form-field__label">Owner</label>
        <select id={`qre-owner-${task.id}`} className="alv-form-field__input" value={owner} onChange={(e) => setOwner(e.target.value)}>
          <option value="">{task.kind === "Manual" ? "Choose a person" : "Unassigned"}</option>
          {!known && owner && <option value={owner}>{task.assigneeName ?? "Former colleague"} (cannot receive reminders)</option>}
          {assignees.map((a) => <option key={a.id} value={a.id}>{a.name} ({a.role})</option>)}
        </select>
      </div>
      <div className="alv-form-field">
        <label htmlFor={`qre-priority-${task.id}`} className="alv-form-field__label">Priority</label>
        <select id={`qre-priority-${task.id}`} className="alv-form-field__input" value={priority} onChange={(e) => setPriority(e.target.value as FollowUpPriority)}>
          {PRIORITIES.map((p) => <option key={p} value={p}>{p}</option>)}
        </select>
      </div>
      <div className="alv-fu-actions">
        <Button type="submit" variant="primary" disabled={busy || (!ownerChanged && !priorityChanged)}>{busy ? "Saving…" : "Save"}</Button>
        <Button type="button" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  );
}
