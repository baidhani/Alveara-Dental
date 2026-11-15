import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";

interface Props {
  /** The form's accessible name, e.g. "Undo check-in for Ann Lee". */
  title: string;
  /** What will happen, in one sentence. */
  explanation: string;
  submitLabel: string;
  busy: boolean;
  onSubmit: (reason: string) => void;
  onCancel: () => void;
}

/** The longest reason the server accepts (AppointmentFlowService / VisitAssignmentService). */
export const MAX_REASON = 400;

/**
 * ALV-N021 A2: a required reason for a change the server records with who and why (undo check-in, keep despite blocked time, provider change).
 * An empty or blank reason is not sent; the server refuses it too.
 */
export function ReasonForm({ title, explanation, submitLabel, busy, onSubmit, onCancel }: Props) {
  const [reason, setReason] = useState("");
  const [missing, setMissing] = useState(false);
  const id = useId();
  const hintId = useId();
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (reason.trim() === "") { setMissing(true); return; }
    onSubmit(reason.trim());
  };
  return (
    <form className="flow-assign" onSubmit={submit} aria-label={title} noValidate>
      <p className="alv-workspace__note">{explanation}</p>
      <div className="alv-form-field">
        <label htmlFor={id} className="alv-form-field__label">Reason (required)</label>
        <textarea id={id} className="alv-form-field__input alv-form-inputs__textarea" rows={2} maxLength={MAX_REASON} value={reason} disabled={busy}
          aria-invalid={missing || undefined} aria-describedby={missing ? hintId : undefined}
          onChange={(e) => { setReason(e.target.value); if (missing) setMissing(false); }} />
        {missing && <p id={hintId} className="alv-form-field__error" role="alert">Give a reason.</p>}
      </div>
      <div className="flow-actions">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : submitLabel}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Back</Button>
      </div>
    </form>
  );
}
