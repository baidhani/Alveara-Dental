import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { followUpErrorMessage, isStaleFollowUp, NOTE_MAX } from "../../services/followUpsApi";
import type { FollowUp } from "../../services/followUpsApi";
import "./followups.css";

interface Props {
  mode: "complete" | "cancel";
  task: FollowUp;
  /** Sends the request with the note (null for none). Throws the API's error to have it shown here. */
  onSubmit: (note: string | null) => Promise<void>;
  onCancel: () => void;
  onStale: () => void;
}

/**
 * STORY-009: complete or cancel one follow-up. Completing takes an optional note and says when a next follow-up will be made; cancelling needs a reason, which is kept with the task. The request carries the
 * version the screen was showing, so if someone else changed or closed the task meanwhile the person is told and offered a reload instead of overwriting.
 */
export function FollowUpActionForm({ mode, task, onSubmit, onCancel, onStale }: Props) {
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  const completing = mode === "complete";

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const trimmed = note.trim();
    if (!completing && trimmed === "") { setError("Write why the follow-up is being cancelled."); return; }
    if (trimmed.length > NOTE_MAX) { setError(`Keep the note to ${NOTE_MAX} characters.`); return; }
    setBusy(true); setError(null);
    try {
      await onSubmit(trimmed === "" ? null : trimmed);
    } catch (err) {
      if (isStaleFollowUp(err)) setStale(true);
      setError(followUpErrorMessage(err));
    } finally { setBusy(false); }
  }

  return (
    <form className="alv-fu-form" onSubmit={submit} aria-labelledby="fu-action-title" noValidate>
      <h3 id="fu-action-title" tabIndex={-1} ref={heading}>{completing ? "Complete" : "Cancel"} follow-up: {task.title}</h3>
      {completing && task.recallIntervalDays && <p>A new follow-up will be made for {task.recallIntervalDays} day{task.recallIntervalDays === 1 ? "" : "s"} after you complete this one.</p>}
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={onStale}>Reload the follow-up</Button>}
      <div className="alv-form-field">
        <label htmlFor="fu-action-note" className="alv-form-field__label">{completing ? "Note (optional" : "Reason (required"}, up to {NOTE_MAX} characters)</label>
        <textarea id="fu-action-note" rows={3} value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
      <div className="alv-fu-actions">
        <Button type="submit" variant={completing ? "primary" : "danger"} disabled={busy}>{busy ? "Saving…" : completing ? "Mark complete" : "Cancel the follow-up"}</Button>
        <Button type="button" onClick={onCancel}>Keep it open</Button>
      </div>
    </form>
  );
}
