import type { AllergyAssessment } from "../../services/prescriptionApi";
import type { SafetyContext, SafetyEntry } from "../../services/safetyApi";

interface Props {
  /** The patient's safety context, `null` while it loads, `"failed"` when it could not be read. */
  safety: SafetyContext | null | "failed";
  /** What the allergy check said about the entry being written, or null before one was checked. */
  assessment: AllergyAssessment | null;
  doseLimitation: string;
  allergyLimitation: string;
}

const line = (e: SafetyEntry) => [e.title, e.severity ? `(${e.severity.toLowerCase()})` : "(severity not recorded)", e.detail].filter(Boolean).join(" ");

/**
 * STORY-016: the persistent patient-safety panel beside the prescription form. It shows what the application KNOWS (the patient's active allergies, current medications, other active alerts and the parts of the
 * record that were never reviewed), what the allergy check said about the entry being written, and, always, what is NOT checked: drug interactions and the clinical appropriateness of a dose. A missing or
 * unreadable list is said to be missing; it is never drawn as "no allergies".
 */
export function PrescriptionSafetyPanel({ safety, assessment, doseLimitation, allergyLimitation }: Props) {
  const entries = safety && safety !== "failed" ? safety.entries : [];
  const allergies = entries.filter((e) => e.origin === "ClinicalRecord" && e.category === "Allergy");
  const medications = entries.filter((e) => e.origin === "ClinicalRecord" && e.category === "Medication");
  const alerts = entries.filter((e) => e.origin === "Alert");
  return (
    <aside className="alv-rx__safety" aria-labelledby="alv-rx-safety-title">
      <h3 id="alv-rx-safety-title">Patient safety</h3>
      {safety === null && <p className="alv-clinical__meta">Loading what is recorded for this patient…</p>}
      {safety === "failed" && (
        <p className="alv-clinical__meta" role="alert">
          The patient&apos;s allergies and medications could not be loaded, so they are not shown here. Nothing listed here does not mean none. The allergy check still runs when you save, and a save is refused if it cannot run.
        </p>
      )}
      {safety && safety !== "failed" && (
        <>
          <h4>Recorded allergies</h4>
          {allergies.length === 0 ? <p className="alv-clinical__meta">No allergy is recorded. Nothing listed here does not mean none.</p> : <ul aria-label="Recorded allergies">{allergies.map((e) => <li key={e.id}>{line(e)}</li>)}</ul>}
          <h4>Current medications</h4>
          {medications.length === 0 ? <p className="alv-clinical__meta">No current medication is recorded.</p> : <ul aria-label="Current medications">{medications.map((e) => <li key={e.id}>{line(e)}</li>)}</ul>}
          {alerts.length > 0 && (<><h4>Other active alerts</h4><ul aria-label="Other active alerts">{alerts.map((e) => <li key={e.id}>{e.title} ({(e.severity ?? "").toLowerCase()})</li>)}</ul></>)}
          {safety.gaps.length > 0 && (<ul aria-label="What is not established">{safety.gaps.map((g) => <li key={g.section}>{g.message}</li>)}</ul>)}
        </>
      )}

      <h4>Allergy check for this entry</h4>
      <p className="alv-rx__assessment" role="status" aria-live="polite" data-outcome={assessment?.outcome ?? "none"}>
        {assessment ? assessment.message : "Not checked yet. Enter the medication to compare its name with the recorded allergies."}
      </p>

      <h4>What is not checked</h4>
      <p className="alv-clinical__meta"><strong>Not checked:</strong> drug interactions, and whether a dose is clinically appropriate for this medication or this patient.</p>
      <p className="alv-clinical__meta">{allergyLimitation}</p>
      <p className="alv-clinical__meta">{doseLimitation}</p>
    </aside>
  );
}
