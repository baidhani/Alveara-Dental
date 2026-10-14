import { useCallback, useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { getPrescriptionOptions, listPrescriptions } from "../../services/prescriptionApi";
import type { AllergyAssessment, Prescription, PrescriptionOptions } from "../../services/prescriptionApi";
import type { PatientDetail } from "../../services/patientsApi";
import { getSafety } from "../../services/safetyApi";
import type { SafetyContext } from "../../services/safetyApi";
import { PrescriptionForm } from "./PrescriptionForm";
import { PrescriptionList } from "./PrescriptionList";
import { PrescriptionSafetyPanel } from "./PrescriptionSafetyPanel";
import "./Prescription.css";

interface Props {
  patient: PatientDetail;
  /** Whether the signed-in person may prescribe. It only decides which controls are drawn (the server checks every call). */
  canPrescribe?: boolean;
}

/**
 * STORY-016: the patient's prescriptions. People who may prescribe get the entry form beside a persistent patient-safety panel (allergies, current medications, what is not established, the allergy check for the
 * entry being written, and what is NOT checked); everyone who may read sees the safety panel and the list. A failed load says it could not load rather than showing an empty list, and an unreadable safety
 * context is said to be unreadable, never drawn as "no allergies".
 */
export function PrescriptionsPanel({ patient, canPrescribe = false }: Props) {
  const [options, setOptions] = useState<PrescriptionOptions | null>(null);
  const [prescriptions, setPrescriptions] = useState<Prescription[] | null>(null);
  const [safety, setSafety] = useState<SafetyContext | null | "failed">(null);
  const [assessment, setAssessment] = useState<AllergyAssessment | null>(null);
  const [failed, setFailed] = useState(false);
  const [status, setStatus] = useState<string | null>(null);

  const loadList = useCallback((signal?: AbortSignal) => {
    listPrescriptions(patient.id, signal).then((r) => { setPrescriptions(r.prescriptions); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patient.id]);

  useEffect(() => {
    const controller = new AbortController();
    getPrescriptionOptions(controller.signal).then(setOptions).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    loadList(controller.signal);
    getSafety(patient.id, controller.signal).then(setSafety).catch(() => { if (!controller.signal.aborted) setSafety("failed"); });
    return () => controller.abort();
  }, [patient.id, loadList]);

  if (failed && (prescriptions === null || options === null)) return <ErrorState title="Could not load the prescriptions" description="Do not assume none are recorded. Check your connection and reload the page." />;
  if (prescriptions === null || options === null) return <LoadingState label="Loading the prescriptions…" />;

  return (
    <section className="alv-clinical alv-rx" aria-labelledby="alv-rx-title">
      <h2 id="alv-rx-title" className="alv-workspace__section-title">Prescriptions</h2>
      <p className="alv-clinical__meta">
        Each prescription is checked against the allergies recorded for this patient and saved with who prescribed it and when. A prescription is never changed or deleted. This screen checks the form of a dose
        only; it has no drug-knowledge source, so it does not check drug interactions or whether a dose is clinically right.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canPrescribe ? "Nothing is saved until you save a prescription." : "You can read this but your role cannot prescribe.")}</p>
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the prescriptions. What is shown may be out of date.</p>}

      <div className="alv-rx__layout">
        {canPrescribe && (
          <PrescriptionForm patientId={patient.id} options={options} assessment={assessment} onAssessment={setAssessment}
            onSaved={(_saved, text) => { setStatus(text); loadList(); }} />
        )}
        <PrescriptionSafetyPanel safety={safety} assessment={assessment} doseLimitation={options.doseLimitation} allergyLimitation={options.allergyLimitation} />
      </div>

      <h3>Prescriptions on record</h3>
      <PrescriptionList prescriptions={prescriptions} />
    </section>
  );
}
