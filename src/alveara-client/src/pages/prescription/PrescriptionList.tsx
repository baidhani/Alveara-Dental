import type { Prescription } from "../../services/prescriptionApi";
import { when } from "../treatmentplan/treatmentPlanRules";
import { allergyResultWords, doseWords } from "./prescriptionText";

/** The patient's prescriptions, newest first, each with who prescribed it and when, and what the allergy check said at that moment. A prescription is never edited or deleted here. */
export function PrescriptionList({ prescriptions }: { prescriptions: Prescription[] }) {
  if (prescriptions.length === 0) return <p className="alv-clinical__meta">No prescription has been recorded for this patient. That means none has been recorded here, not that none was ever given.</p>;
  return (
    <ul className="alv-rx__list" aria-label="Prescriptions">
      {prescriptions.map((p) => (
        <li key={p.id} className="alv-rx__item">
          <h4 className="alv-rx__medication">{p.medication}</h4>
          <p>{doseWords(p)} · {p.frequency}</p>
          <p className="alv-clinical__meta">Prescribed by {p.prescribedByName ?? "an unnamed user"} on {when(p.prescribedAtUtc)}.</p>
          <p className="alv-clinical__meta">{allergyResultWords(p)}</p>
          {p.reviewNote && <p className="alv-rx__note"><strong>Prescriber&apos;s review:</strong> {p.reviewNote}</p>}
        </li>
      ))}
    </ul>
  );
}
