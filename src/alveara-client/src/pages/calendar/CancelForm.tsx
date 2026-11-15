import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { CANCEL_CATEGORIES, CANCEL_NOTE_MAX } from "../../services/schedulingApi";

interface Props {
  busy: boolean;
  onSubmit: (category: string, note: string) => void;
  onBack: () => void;
}

/**
 * ALV-N021 A2 (OD-19): cancelling asks WHY as a category (required, from the server's fixed list) plus an optional note. Short notice (under 24 hours)
 * is worked out by the server from the time of cancelling; it is not asked for here.
 */
export function CancelForm({ busy, onSubmit, onBack }: Props) {
  const [category, setCategory] = useState("");
  const [note, setNote] = useState("");
  const [missing, setMissing] = useState(false);
  const ids = { category: useId(), note: useId(), hint: useId() };
  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!category) { setMissing(true); return; }
    onSubmit(category, note);
  };
  return (
    <form className="alv-workspace__form" onSubmit={submit} noValidate aria-label="Cancel appointment">
      <div className="alv-form-field">
        <label htmlFor={ids.category} className="alv-form-field__label">Why is it cancelled? (required)</label>
        <select id={ids.category} className="alv-form-field__input" value={category} disabled={busy}
          aria-invalid={missing || undefined} aria-describedby={missing ? ids.hint : undefined}
          onChange={(e) => { setCategory(e.target.value); if (missing) setMissing(false); }}>
          <option value="">Choose…</option>
          {CANCEL_CATEGORIES.map((c) => <option key={c.code} value={c.code}>{c.label}</option>)}
        </select>
        {missing && <p id={ids.hint} className="alv-form-field__error" role="alert">Choose why the appointment is cancelled.</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor={ids.note} className="alv-form-field__label">Note (optional)</label>
        <textarea id={ids.note} className="alv-form-field__input alv-form-inputs__textarea" rows={2} maxLength={CANCEL_NOTE_MAX} value={note} onChange={(e) => setNote(e.target.value)} disabled={busy} />
      </div>
      <p className="alv-workspace__note">The appointment stays on the calendar, marked as cancelled, and stops holding the time.</p>
      <div className="cal-panel__actions">
        <Button type="submit" variant="danger" disabled={busy}>{busy ? "Cancelling…" : "Confirm cancellation"}</Button>
        <Button type="button" onClick={onBack} disabled={busy}>Keep appointment</Button>
      </div>
    </form>
  );
}
