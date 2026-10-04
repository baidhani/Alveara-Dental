import { useId, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError } from "../../services/authApi";
import { addAddendum, clinicalFieldErrorsOf, isNetworkFailure, newKey } from "../../services/clinicalApi";
import type { EncounterDetail } from "../../services/clinicalApi";
import { AMENDABLE_LABELS } from "../../services/clinicalNotesApi";
import "./Clinical.css";

const when = (iso: string) => new Date(iso).toLocaleString();

interface Props {
  encounter: EncounterDetail;
  canWrite: boolean;
  /** Receives the encounter as the server returned it, with the new addendum in it. */
  onAdded: (encounter: EncounterDetail) => void;
}

/**
 * STORY-005: the addenda of a finalized encounter, oldest first, and (for a clinician) a way to add one. An addendum is shown beside the original note, never merged
 * into it. The request carries one key per addendum that is kept across retries: if the connection drops after the server stored it, sending again returns the
 * first addendum instead of adding a second - and the typed text stays in the box until the server has accepted it.
 *
 * ALV-005-C01: this is also the timeline of amendments. It opens with when the original was finalized and by whom, then lists each addendum in order with who wrote it, when,
 * and which part of the note it amends (a documentation section, a note section, the vital signs, or nothing in particular). The original itself is never edited.
 */
export function AddendumPanel({ encounter, canWrite, onAdded }: Props) {
  const [text, setText] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [section, setSection] = useState("");
  const key = useRef<string | null>(null);
  const textId = useId();
  const sectionId = useId();
  useUnsavedChangesWarning(text.trim() !== "");

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (text.trim() === "") {
      setFieldError("Write the addendum.");
      return;
    }
    setBusy(true);
    setError(null);
    setFieldError(null);
    key.current ??= newKey(); // one key for this addendum, reused by every retry of it
    try {
      const updated = await addAddendum(encounter.id, text.trim(), key.current, section || undefined);
      key.current = null;
      setText("");
      setSection("");
      onAdded(updated);
    } catch (err) {
      const fields = clinicalFieldErrorsOf(err);
      if (fields.text) setFieldError(fields.text);
      else if (isNetworkFailure(err)) setError("The connection dropped, so it is not certain the addendum was saved. Your text is still here: send it again - it cannot be added twice.");
      else setError(err instanceof ApiError ? err.message : "Could not add the addendum. Try again.");
      if (err instanceof ApiError) key.current = null; // a definite refusal: the next attempt is a new addendum
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="alv-clinical__addenda" aria-labelledby="alv-clinical-addenda-title">
      <h3 id="alv-clinical-addenda-title" className="alv-clinical__section-title">Addenda</h3>
      {encounter.finalizedAtUtc && (
        <p className="alv-clinical__meta">Original finalized {when(encounter.finalizedAtUtc)}{encounter.finalizedByName ? ` by ${encounter.finalizedByName}` : ""}.</p>
      )}
      {encounter.addenda.length === 0 ? (
        <p className="alv-clinical__note">No addenda. The note above is exactly as it was finalized.</p>
      ) : (
        <ol className="alv-clinical__addendum-list">
          {encounter.addenda.map((a) => (
            <li key={a.id} className="alv-clinical__addendum">
              <p className="alv-clinical__addendum-text">{a.text}</p>
              <p className="alv-clinical__meta">Added {when(a.createdAtUtc)}{a.createdByName ? ` by ${a.createdByName}` : ""}</p>
              {a.section && <p className="alv-clinical__meta">Amends: {AMENDABLE_LABELS[a.section] ?? a.section}</p>}
            </li>
          ))}
        </ol>
      )}
      {canWrite && (
        <form onSubmit={submit} noValidate aria-label="Add an addendum" className="alv-clinical__addendum-form">
          <div className="alv-form-field">
            <label htmlFor={textId} className="alv-form-field__label">New addendum</label>
            <textarea id={textId} className="alv-form-field__input alv-clinical__details" rows={3} maxLength={4000} value={text} onChange={(e) => setText(e.target.value)} aria-invalid={fieldError ? true : undefined} />
            {fieldError && <p className="alv-form-field__error" role="alert">{fieldError}</p>}
          </div>
          <div className="alv-form-field">
            <label htmlFor={sectionId} className="alv-form-field__label">What does this amend? (optional)</label>
            <select id={sectionId} className="alv-form-field__input" value={section} onChange={(e) => setSection(e.target.value)}>
              <option value="">Nothing in particular</option>
              {Object.entries(AMENDABLE_LABELS).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          </div>
          {error && <p className="alv-form-field__error" role="alert">{error}</p>}
          <Button type="submit" variant="primary" disabled={busy}>Add addendum</Button>
        </form>
      )}
    </section>
  );
}
