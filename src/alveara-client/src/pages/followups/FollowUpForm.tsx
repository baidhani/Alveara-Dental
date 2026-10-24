import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { followUpErrorMessage, isStaleFollowUp, newFollowUpKey, NOTE_MAX, TITLE_MAX, INTERVAL_MAX } from "../../services/followUpsApi";
import type { Assignee, FollowUp, FollowUpInput } from "../../services/followUpsApi";
import { toInput, toLocalInput, validate } from "./followUpFormat";
import type { FormErrors, FormValues } from "./followUpFormat";
import "./followups.css";

interface Props {
  /** The task being edited, or nothing for a new one. */
  initial?: FollowUp;
  assignees: Assignee[];
  defaultAssignee?: string;
  /** Sends the request; the key is the same for a retry of the same attempt. Throws the API's error to have it shown here. */
  onSubmit: (input: FollowUpInput, key: string) => Promise<void>;
  onCancel: () => void;
  /** Called when the server says the task changed meanwhile: the screen reloads and closes this form. */
  onStale: () => void;
}

/**
 * STORY-009: create or edit one follow-up. Checks the entry before sending (the server checks it again), says in words what is wrong beside the field, keeps one Idempotency-Key per attempt (pressing Save again
 * after a failure cannot create a second task; changing the form starts a new attempt) and, if the task was changed by someone else, explains that and offers a reload instead of overwriting.
 */
export function FollowUpForm({ initial, assignees, defaultAssignee, onSubmit, onCancel, onStale }: Props) {
  const [values, setValues] = useState<FormValues>(() => ({
    title: initial?.title ?? "", note: initial?.note ?? "", due: initial ? toLocalInput(initial.dueAtUtc) : "",
    assignee: initial?.assigneeUserId ?? defaultAssignee ?? "", interval: initial?.recallIntervalDays ? String(initial.recallIntervalDays) : "",
  }));
  const [errors, setErrors] = useState<FormErrors>({});
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const key = useRef(newFollowUpKey());
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);

  const set = (name: keyof FormValues) => (e: { target: { value: string } }) => { key.current = newFollowUpKey(); setValues((v) => ({ ...v, [name]: e.target.value })); };
  const describe = (name: keyof FormValues) => (errors[name] ? `fu-${name}-error` : undefined);
  const known = assignees.some((a) => a.id === values.assignee);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const found = validate(values);
    setErrors(found);
    if (Object.keys(found).length > 0) { setError("Fix the marked fields and save again."); return; }
    setBusy(true); setError(null);
    try {
      await onSubmit(toInput(values), key.current);
    } catch (err) {
      if (isStaleFollowUp(err)) setStale(true);
      setError(followUpErrorMessage(err));
    } finally { setBusy(false); }
  }

  return (
    <form className="alv-fu-form" onSubmit={submit} aria-labelledby="fu-form-title" noValidate>
      <h3 id="fu-form-title" tabIndex={-1} ref={heading}>{initial ? "Edit follow-up" : "New follow-up"}</h3>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={onStale}>Reload the follow-up</Button>}

      <div className="alv-form-field">
        <label htmlFor="fu-title" className="alv-form-field__label">Title (required, up to {TITLE_MAX} characters)</label>
        <input id="fu-title" className="alv-form-field__input" value={values.title} onChange={set("title")} aria-invalid={!!errors.title} aria-describedby={describe("title")} autoComplete="off" />
        {errors.title && <p id="fu-title-error" className="alv-fu-error">{errors.title}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor="fu-note" className="alv-form-field__label">Note (optional, up to {NOTE_MAX} characters)</label>
        <textarea id="fu-note" rows={3} value={values.note} onChange={set("note")} aria-invalid={!!errors.note} aria-describedby={describe("note")} />
        {errors.note && <p id="fu-note-error" className="alv-fu-error">{errors.note}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor="fu-due" className="alv-form-field__label">Due (required)</label>
        <input id="fu-due" type="datetime-local" className="alv-form-field__input" value={values.due} onChange={set("due")} aria-invalid={!!errors.due} aria-describedby={describe("due")} />
        {errors.due && <p id="fu-due-error" className="alv-fu-error">{errors.due}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor="fu-assignee" className="alv-form-field__label">Responsible person (required)</label>
        <select id="fu-assignee" className="alv-form-field__input" value={values.assignee} onChange={set("assignee")} aria-invalid={!!errors.assignee} aria-describedby={describe("assignee")}>
          <option value="">Choose a person</option>
          {!known && initial && <option value={values.assignee}>{initial.assigneeName ?? "Former colleague"} (cannot receive reminders)</option>}
          {assignees.map((a) => <option key={a.id} value={a.id}>{a.name} ({a.role})</option>)}
        </select>
        {errors.assignee && <p id="fu-assignee-error" className="alv-fu-error">{errors.assignee}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor="fu-interval" className="alv-form-field__label">Repeat every (days after it is completed; optional, 1 to {INTERVAL_MAX})</label>
        <input id="fu-interval" className="alv-form-field__input" inputMode="numeric" value={values.interval} onChange={set("interval")} aria-invalid={!!errors.interval} aria-describedby={describe("interval")} autoComplete="off" />
        {errors.interval && <p id="fu-interval-error" className="alv-fu-error">{errors.interval}</p>}
      </div>
      <div className="alv-fu-actions">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : initial ? "Save changes" : "Create follow-up"}</Button>
        <Button type="button" onClick={onCancel}>Cancel</Button>
      </div>
    </form>
  );
}
