import { useCallback, useEffect, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { listEncounters } from "../../services/clinicalApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { listDiagnoses } from "../../services/diagnosisApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PatientDetail } from "../../services/patientsApi";
import { DEFAULT_NUMBERING } from "../odontogram/toothNumbering";
import type { NumberingSystem } from "../odontogram/toothNumbering";
import { DiagnosisForm } from "./DiagnosisForm";
import { DiagnosisItem } from "./DiagnosisItem";
import "./Diagnosis.css";

interface Props {
  patient: PatientDetail;
  numbering?: NumberingSystem;
  /** Whether the signed-in person may record, correct and withdraw. It only decides which controls are drawn (the server checks every call). */
  canWrite?: boolean;
}

/**
 * STORY-013: the patient's structured diagnoses, in the clinical documentation module. Each diagnosis is linked to the patient and to the encounter it was made in; it can carry an optional treatment-plan
 * REFERENCE, which this screen always labels as unresolved (treatment plans do not exist yet, so it is a note to link later and proves nothing). People who can write record, correct and withdraw
 * diagnoses (every correction and withdrawal asks why; nothing is ever deleted); everyone who can read sees the list and each diagnosis's history. An empty list says only that none has been recorded, and
 * a failed load says it could not load rather than showing an empty list.
 */
export function DiagnosesPanel({ patient, numbering = DEFAULT_NUMBERING, canWrite = false }: Props) {
  const [diagnoses, setDiagnoses] = useState<Diagnosis[] | null>(null);
  const [encounters, setEncounters] = useState<EncounterSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [showWithdrawn, setShowWithdrawn] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const load = useCallback((withdrawn: boolean, signal?: AbortSignal) => {
    listDiagnoses(patient.id, { includeWithdrawn: withdrawn }, signal).then((r) => { setDiagnoses(r.diagnoses); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patient.id]);

  useEffect(() => {
    const controller = new AbortController();
    load(false, controller.signal);
    listEncounters(patient.id, controller.signal).then(setEncounters).catch(() => { if (!controller.signal.aborted) setEncounters([]); });
    return () => controller.abort();
  }, [patient.id, load]);

  function toggleWithdrawn(next: boolean) {
    setShowWithdrawn(next);
    load(next);
  }

  function saved(_d: Diagnosis, text: string) {
    setConflict(false);
    setStatus(text);
    load(showWithdrawn);
  }

  if (failed && diagnoses === null) return <ErrorState title="Could not load the diagnoses" description="Do not assume none are recorded. Check your connection and reload the page." />;
  if (diagnoses === null || encounters === null) return <LoadingState label="Loading the diagnoses…" />;

  return (
    <section className="alv-clinical alv-dx" aria-labelledby="alv-dx-title">
      <h2 id="alv-dx-title" className="alv-workspace__section-title">Diagnoses</h2>
      <p className="alv-clinical__meta">
        Each diagnosis is recorded for an encounter of this patient. A diagnosis is never deleted: a wrong one is corrected or withdrawn with a reason, and its history is kept. A treatment plan reference is a
        note to link a plan later and does not mean a plan exists.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canWrite ? "Nothing is saved until you record a diagnosis." : "You can read this but your role cannot change it.")}</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", entityType: "diagnosis", entityId: patient.id }} onReload={() => { setConflict(false); setStatus("Reloaded."); load(showWithdrawn); }} />}
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the diagnoses. What is shown may be out of date.</p>}

      {canWrite && (encounters.length > 0
        ? <DiagnosisForm patientId={patient.id} numbering={numbering} encounters={encounters} onSaved={saved} onConflict={() => setConflict(true)} />
        : <p className="alv-clinical__meta" role="note">This patient has no encounter yet, and a diagnosis is recorded for an encounter. Start an encounter in the Clinical tab first.</p>)}

      <h3>Diagnoses on record</h3>
      <label className="alv-dx__showall"><input type="checkbox" checked={showWithdrawn} onChange={(e) => toggleWithdrawn(e.target.checked)} /> Show withdrawn diagnoses</label>
      {diagnoses.length === 0
        ? <p className="alv-clinical__meta">No diagnosis has been recorded for this patient. That means none has been recorded, not that nothing is wrong.</p>
        : <ul className="alv-dx__list" aria-label="Diagnoses">{diagnoses.map((d) => <DiagnosisItem key={d.id} diagnosis={d} encounters={encounters} numbering={numbering} canWrite={canWrite} onChanged={saved} onConflict={() => setConflict(true)} />)}</ul>}
    </section>
  );
}
