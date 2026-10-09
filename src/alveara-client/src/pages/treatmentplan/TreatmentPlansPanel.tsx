import { useCallback, useEffect, useState } from "react";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { listDiagnoses } from "../../services/diagnosisApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import type { PatientDetail } from "../../services/patientsApi";
import { listActiveProcedures, listTreatmentPlans } from "../../services/treatmentPlansApi";
import type { PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanCard } from "./TreatmentPlanCard";
import { TreatmentPlanForm } from "./TreatmentPlanForm";
import "../diagnosis/Diagnosis.css";
import "./TreatmentPlan.css";

interface Props {
  patient: PatientDetail;
  /** Whether the signed-in person may create and change plans. It only decides which controls are drawn (the server checks every call). */
  canWrite?: boolean;
}

/**
 * STORY-015: the patient's treatment plans, in the clinical documentation module. A plan proposes catalog procedures for the patient's current diagnoses and shows the practice's fee estimate (not an
 * insurance estimate). People who can write create plans; everyone who can read sees the plans exactly as saved. A plan is never deleted. An empty list says only that none has been created, and a failed
 * load says it could not load rather than showing an empty list. The catalog is read only by people who write plans (reading it needs billing access, and a reader's view needs nothing from it).
 */
export function TreatmentPlansPanel({ patient, canWrite = false }: Props) {
  const [plans, setPlans] = useState<TreatmentPlan[] | null>(null);
  const [diagnoses, setDiagnoses] = useState<Diagnosis[] | null>(canWrite ? null : []);
  const [procedures, setProcedures] = useState<PlanningProcedure[] | null>(canWrite ? null : []);
  const [failed, setFailed] = useState(false);
  const [catalogFailed, setCatalogFailed] = useState(false);
  const [showWithdrawn, setShowWithdrawn] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const load = useCallback((withdrawn: boolean, signal?: AbortSignal) => {
    listTreatmentPlans(patient.id, { includeWithdrawn: withdrawn }, signal).then((r) => { setPlans(r.plans); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [patient.id]);

  useEffect(() => {
    const controller = new AbortController();
    load(false, controller.signal);
    if (canWrite) {
      listDiagnoses(patient.id, {}, controller.signal).then((r) => setDiagnoses(r.diagnoses.filter((d) => d.status !== "Withdrawn"))).catch(() => { if (!controller.signal.aborted) setDiagnoses([]); });
      listActiveProcedures({}, controller.signal).then(setProcedures).catch(() => { if (!controller.signal.aborted) { setProcedures([]); setCatalogFailed(true); } });
    }
    return () => controller.abort();
  }, [patient.id, canWrite, load]);

  function toggleWithdrawn(next: boolean) {
    setShowWithdrawn(next);
    load(next);
  }

  function created(plan: TreatmentPlan) {
    changed(plan, `Treatment plan "${plan.title}" created.`);
  }

  function changed(_plan: TreatmentPlan, text: string) {
    setConflict(false);
    setStatus(text);
    load(showWithdrawn);
  }

  if (failed && plans === null) return <ErrorState title="Could not load the treatment plans" description="Do not assume none exist. Check your connection and reload the page." />;
  if (plans === null || diagnoses === null || procedures === null) return <LoadingState label="Loading the treatment plans…" />;

  return (
    <section className="alv-clinical alv-dx alv-tp" aria-labelledby="alv-tp-title">
      <h2 id="alv-tp-title" className="alv-workspace__section-title">Treatment plan</h2>
      <p className="alv-clinical__meta">
        A treatment plan proposes catalog procedures for this patient's diagnoses, with the practice's fee for each. It is never deleted: a wrong procedure or plan is withdrawn with a reason, and the history is kept.
      </p>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? (canWrite ? "Nothing is saved until you create a plan." : "You can read this but your role cannot change it.")}</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", entityType: "treatment plan", entityId: patient.id }} onReload={() => { setConflict(false); setStatus("Reloaded."); load(showWithdrawn); }} />}
      {failed && <p className="alv-clinical__meta" role="alert">Could not refresh the treatment plans. What is shown may be out of date.</p>}
      {canWrite && catalogFailed && <p className="alv-clinical__meta" role="alert">The procedure catalog could not be loaded, so a plan cannot be created now. Reload the page to try again.</p>}

      {canWrite && !catalogFailed && <TreatmentPlanForm patientId={patient.id} diagnoses={diagnoses} procedures={procedures} onSaved={created} />}

      <h3>Plans on record</h3>
      <label className="alv-dx__showall"><input type="checkbox" checked={showWithdrawn} onChange={(e) => toggleWithdrawn(e.target.checked)} /> Show withdrawn plans</label>
      {plans.length === 0
        ? <p className="alv-clinical__meta">No treatment plan has been created for this patient. That means none has been created, not that no treatment is needed.</p>
        : <ul className="alv-dx__list" aria-label="Treatment plans">{plans.map((p) => <TreatmentPlanCard key={p.id} plan={p} canWrite={canWrite} diagnoses={diagnoses} procedures={procedures} onChanged={changed} onConflict={() => setConflict(true)} />)}</ul>}
    </section>
  );
}
