import { useEffect, useRef } from "react";
import type { AllergyAssessment } from "../../../services/prescriptionApi";
import type { DraftView } from "../../../services/prescriptionWorkflowApi";
import { quantityWords } from "./workflowRules";

interface Props {
  draft: DraftView;
  assessment: AllergyAssessment | null;
  busy: boolean;
  /** The standing statement that no drug-knowledge source is configured. */
  limitation: string;
  message: string | null;
  onConfirm: () => void;
  onBack: () => void;
}

/**
 * ALV-016-C01: the last look before a prescription is finalized. It shows exactly what will be recorded, what the allergy check said (a name comparison, never a clearance), what is NOT checked, and that finalizing
 * records the prescription with the prescriber's name and the time and cannot be edited afterwards (it can be cancelled, discontinued or amended, each recorded). Nothing is saved until "Finalize prescription".
 */
export function FinalizeConfirmation({ draft, assessment, busy, limitation, message, onConfirm, onBack }: Props) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus(); }, []);
  return (
    <section className="alv-rx__form" aria-labelledby="alv-rx-confirm-title">
      <h3 id="alv-rx-confirm-title" ref={heading} tabIndex={-1}>Confirm finalization</h3>
      {message && <p role="alert" className="alv-rx__message">{message}</p>}
      <dl className="alv-rx__summary">
        <dt>Medication</dt><dd>{draft.medication}</dd>
        <dt>Dose</dt><dd>{draft.doseValue} {draft.doseUnit}, {draft.frequency.toLowerCase()}</dd>
        <dt>Instructions</dt><dd className="alv-rx__note">{draft.instructions}</dd>
        <dt>Quantity</dt><dd>{quantityWords(draft.quantityValue, draft.quantityUnit)}</dd>
        <dt>Refills</dt><dd>{draft.refills}</dd>
        <dt>Allergy check</dt><dd>{assessment ? assessment.message : "The allergy check will run when you finalize."}</dd>
        {draft.reviewNote && (<><dt>Prescriber&apos;s review</dt><dd className="alv-rx__note">{draft.reviewNote}</dd></>)}
      </dl>
      <p className="alv-clinical__meta">{assessment?.limitation}</p>
      <p className="alv-clinical__meta"><strong>Not checked:</strong> {limitation}</p>
      <p>
        Finalizing records this prescription with your name and the time. It cannot be edited afterwards; it can be cancelled, discontinued or amended, and each of those is recorded.
        {draft.supersedesPrescriptionId ? " It will replace the prescription it amends, which will then show as superseded." : ""}
      </p>
      <div className="alv-clinical__row-actions">
        <button type="button" className="btn btn-primary" disabled={busy} onClick={onConfirm}>Finalize prescription</button>
        <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={onBack}>Back to editing</button>
      </div>
    </section>
  );
}
