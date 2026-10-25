import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { caseErrorMessage, changeCaseStatus, isStaleCaseChange, NOTE_MAX } from "../../services/externalCasesApi";
import type { CaseStatus, ExternalCase } from "../../services/externalCasesApi";
import { moveLabel, needsDate, noteRequired, statusLabel, todayInput } from "./externalCaseFormat";
import "../followups/followups.css";

interface Props { c: ExternalCase; to: CaseStatus; onDone: (updated: ExternalCase) => void; onCancel: () => void; onStale: () => void }

/**
 * ALV-009-C02: move one case to another status. Going out and coming back take the date it happened (today unless changed, never in the future); a cancellation and sending a lab case back for a remake need a written
 * note, which is kept in the case's history. The request carries the version the screen was showing, so if someone else moved the case meanwhile the person is told and offered a reload instead of overwriting.
 */
export function CaseStatusForm({ c, to, onDone, onCancel, onStale }: Props) {
  const [date, setDate] = useState(todayInput());
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [stale, setStale] = useState(false);
  const [busy, setBusy] = useState(false);
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  const mustNote = noteRequired(c.kind, c.status, to);
  const label = moveLabel(c.kind, c.status, to);

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (busy) return;
    const trimmed = note.trim();
    if (mustNote && trimmed === "") { setError(to === "Cancelled" ? "Write why the case is being cancelled." : "Write why the work is being sent back."); return; }
    if (trimmed.length > NOTE_MAX) { setError(`Keep the note to ${NOTE_MAX} characters.`); return; }
    if (needsDate(to) && (date === "" || date > todayInput())) { setError("Choose the date it happened. It cannot be in the future."); return; }
    setBusy(true); setError(null);
    try {
      const result = await changeCaseStatus(c.kind, c.id, to, trimmed === "" ? null : trimmed, needsDate(to) ? date : null, c.rowVersion);
      onDone(result.case);
    } catch (err) {
      if (isStaleCaseChange(err)) setStale(true);
      setError(caseErrorMessage(err));
    } finally { setBusy(false); }
  }

  return (
    <form className="alv-fu-form" onSubmit={submit} aria-labelledby="alv-ec-move-title" noValidate>
      <h2 id="alv-ec-move-title" tabIndex={-1} ref={heading}>{label}: {c.summary}</h2>
      <p className="alv-clinical__meta">From {statusLabel(c.status)} to {statusLabel(to)}.</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {stale && <Button type="button" onClick={onStale}>Reload the case</Button>}
      {needsDate(to) && (
        <div className="alv-form-field">
          <label htmlFor="alv-ec-move-date" className="alv-form-field__label">{to === "Sent" ? "Date it went out" : "Date it came back"}</label>
          <input id="alv-ec-move-date" className="alv-form-field__input" type="date" value={date} max={todayInput()} onChange={(e) => setDate(e.target.value)} />
        </div>
      )}
      <div className="alv-form-field">
        <label htmlFor="alv-ec-move-note" className="alv-form-field__label">{mustNote ? "Reason (required" : "Note (optional"}, up to {NOTE_MAX} characters)</label>
        <textarea id="alv-ec-move-note" rows={3} value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
      <div className="alv-fu-actions">
        <Button type="submit" variant={to === "Cancelled" ? "danger" : "primary"} disabled={busy}>{busy ? "Saving…" : label}</Button>
        <Button type="button" onClick={onCancel}>Keep it as it is</Button>
      </div>
    </form>
  );
}
