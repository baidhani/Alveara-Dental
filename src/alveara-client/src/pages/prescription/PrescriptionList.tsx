import type { Prescription } from "../../services/prescriptionApi";
import { when } from "../treatmentplan/treatmentPlanRules";
import { allergyResultWords, doseWords } from "./prescriptionText";
import { quantityWords, statusWords } from "./workflow/workflowRules";

/**
 * The patient's prescriptions, newest first, each with who prescribed it and when, and what the allergy check said at that moment. A prescription is never edited or deleted here.
 * ALV-N022 A3 (F-106): each also shows its status, its instructions, quantity and refills, and whether it was superseded by (or supersedes) another, as the prescription
 * workflow recorded them - the same words as the workflow's own list. One saved before the workflow says those were not recorded; nothing is invented.
 */
export function PrescriptionList({ prescriptions }: { prescriptions: Prescription[] }) {
  if (prescriptions.length === 0) return <p className="alv-clinical__meta">No prescription has been recorded for this patient. That means none has been recorded here, not that none was ever given.</p>;
  return (
    <ul className="alv-rx__list" aria-label="Prescriptions">
      {prescriptions.map((p) => (
        <li key={p.id} className="alv-rx__item" data-status={p.status ?? undefined}>
          <h4 className="alv-rx__medication">{p.medication}</h4>
          <p>{doseWords(p)} · {p.frequency}</p>
          {p.status && <p><strong>Status:</strong> {statusWords(p.status)}</p>}
          {p.hasWorkflowDetails ? (
            <>
              <p className="alv-rx__note">{p.instructions}</p>
              <p>Quantity {quantityWords(p.quantityValue ?? null, p.quantityUnit ?? null)} · Refills {p.refills}</p>
            </>
          ) : <p className="alv-clinical__meta">Saved before instructions, quantity and refills were recorded.</p>}
          {p.supersededByPrescriptionId && <p className="alv-clinical__meta">Replaced by a later amendment (listed above).</p>}
          {p.supersedesPrescriptionId && <p className="alv-clinical__meta">This is an amendment; it replaces an earlier version (listed below).</p>}
          <p className="alv-clinical__meta">Prescribed by {p.prescribedByName ?? "an unnamed user"} on {when(p.prescribedAtUtc)}.</p>
          <p className="alv-clinical__meta">{allergyResultWords(p)}</p>
          {p.reviewNote && <p className="alv-rx__note"><strong>Prescriber&apos;s review:</strong> {p.reviewNote}</p>}
        </li>
      ))}
    </ul>
  );
}
