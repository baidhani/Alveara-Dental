import { Button } from "../../components/Button";
import { SECTION_LABELS, SECTION_STATUS_LABELS } from "../../services/clinicalApi";
import type { EncounterDetail } from "../../services/clinicalApi";
import { NOTE_LABELS } from "../../services/clinicalNotesApi";
import "./Clinical.css";

interface Props {
  encounter: EncounterDetail;
  busy: boolean;
  /** The server's own refusal (for example another clinician removed an entry meanwhile): shown above the list; the local summary is never trusted over it. */
  refusal: { message: string; sections: string[] } | null;
  onConfirm: () => void;
  /** Signs the note (the middle state between draft and finalized); absent when the caller does not offer signing. */
  onSign?: () => void;
  onCancel: () => void;
}

/**
 * STORY-005 / ALV-005-C01: the review before an encounter is signed or finalized. It lists every documentation section with where it stands AND every note the template
 * made required, and flags the ones still unresolved, so nothing is signed or finalized by surprise. Signing and finalizing are disabled while anything is unresolved (the
 * server refuses both too); a section with nothing to report is resolved by marking it "reviewed - none reported", never by inventing an entry. A signed note is locked
 * until it is finalized or unsigned; finalizing it needs no further signature.
 */
export function FinalizeReview({ encounter, busy, refusal, onConfirm, onSign, onCancel }: Props) {
  const unresolved = encounter.sections.filter((s) => s.status === "Empty");
  const flagged = new Set([...unresolved.map((s) => s.kind), ...(refusal?.sections ?? [])]);
  const notes = encounter.notes.filter((n) => n.required);
  const blocked = unresolved.length > 0 || encounter.missingNotes.length > 0;
  return (
    <section className="alv-clinical__review" aria-labelledby="alv-clinical-review-title">
      <h3 id="alv-clinical-review-title" className="alv-clinical__section-title">Review before finalizing</h3>
      <p className="alv-clinical__note">
        Once finalized, this note cannot be changed. If something needs correcting later you add an addendum; the original stays exactly as it is.
      </p>
      {refusal && <p className="alv-form-field__error" role="alert">{refusal.message}</p>}
      <ul className="alv-clinical__review-list">
        {encounter.sections.map((s) => (
          <li key={s.kind} className={flagged.has(s.kind) ? "alv-clinical__review-item alv-clinical__review-item--open" : "alv-clinical__review-item"}>
            <span className="alv-clinical__review-name">{SECTION_LABELS[s.kind]}</span>
            <span>{flagged.has(s.kind) ? "Needs attention - record an entry or mark it reviewed, none reported" : `${SECTION_STATUS_LABELS[s.status]}${s.entries.length > 0 ? ` (${s.entries.length})` : ""}`}</span>
          </li>
        ))}
        {notes.map((n) => {
          const open = encounter.missingNotes.includes(n.section) || (refusal?.sections ?? []).includes(`note:${n.section}`);
          return (
            <li key={n.section} className={open ? "alv-clinical__review-item alv-clinical__review-item--open" : "alv-clinical__review-item"}>
              <span className="alv-clinical__review-name">{NOTE_LABELS[n.section]}</span>
              <span>{open ? "Needs attention - write this note (the template requires it)" : "Written"}</span>
            </li>
          );
        })}
      </ul>
      <div className="alv-clinical__row-actions">
        {onSign && !encounter.isSigned && <Button type="button" onClick={onSign} disabled={busy || blocked}>Sign note</Button>}
        <Button type="button" variant="primary" onClick={onConfirm} disabled={busy || blocked}>Finalize encounter</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Keep editing</Button>
      </div>
    </section>
  );
}
