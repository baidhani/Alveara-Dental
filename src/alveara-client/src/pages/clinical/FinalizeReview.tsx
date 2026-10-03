import { Button } from "../../components/Button";
import { SECTION_LABELS, SECTION_STATUS_LABELS } from "../../services/clinicalApi";
import type { EncounterDetail } from "../../services/clinicalApi";
import "./Clinical.css";

interface Props {
  encounter: EncounterDetail;
  busy: boolean;
  /** The server's own refusal (for example another clinician removed an entry meanwhile): shown above the list; the local summary is never trusted over it. */
  refusal: { message: string; sections: string[] } | null;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * STORY-005: the review before an encounter is finalized. It lists every section with where it stands and flags the ones still not addressed, so nothing is
 * finalized by surprise. Finalizing is disabled while a section is unresolved (the server refuses it too); a section with nothing to report is resolved by
 * marking it "reviewed - none reported", never by inventing an entry.
 */
export function FinalizeReview({ encounter, busy, refusal, onConfirm, onCancel }: Props) {
  const unresolved = encounter.sections.filter((s) => s.status === "Empty");
  const flagged = new Set([...unresolved.map((s) => s.kind), ...(refusal?.sections ?? [])]);
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
      </ul>
      <div className="alv-clinical__row-actions">
        <Button type="button" variant="primary" onClick={onConfirm} disabled={busy || unresolved.length > 0}>Finalize encounter</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Keep editing</Button>
      </div>
    </section>
  );
}
