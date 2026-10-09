import { useId, useRef, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure, newKey } from "../../services/clinicalApi";
import type { Diagnosis } from "../../services/diagnosisApi";
import { addPlanItem, planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { PlanningProcedure, TreatmentPlan } from "../../services/treatmentPlansApi";
import { TreatmentPlanItemFields } from "./TreatmentPlanItemFields";
import { EMPTY_ITEM, itemGap, otherMessages, toItemInput } from "./treatmentPlanRules";
import type { ItemDraft } from "./treatmentPlanRules";

interface Props {
  plan: TreatmentPlan;
  diagnoses: Diagnosis[];
  procedures: PlanningProcedure[];
  onDone: (plan: TreatmentPlan) => void;
  onConflict: () => void;
  onCancel: () => void;
}

const KNOWN = ["diagnosisId", "procedureId", "toothKey", "surface"] as const;

/** STORY-015: one more proposed procedure for a plan. Same behaviour as creating a plan: a gap is named beside its field first, a refusal keeps what was typed, and a retry of the same entry carries the same key. */
export function TreatmentPlanAddItemForm({ plan, diagnoses, procedures, onDone, onConflict, onCancel }: Props) {
  const uid = useId();
  const [item, setItem] = useState<ItemDraft>(EMPTY_ITEM);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);

  async function save() {
    setMessage(null);
    const gap = itemGap(item);
    if (gap) {
      setErrors({ [!item.diagnosisId ? "diagnosisId" : !item.procedure ? "procedureId" : "toothKey"]: gap });
      return setMessage("Not added: some entries need correcting. Nothing was sent.");
    }
    setErrors({});
    const input = toItemInput(item);
    const signature = JSON.stringify([plan.id, plan.rowVersion, input]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    try {
      onDone(await addPlanItem(plan, attempt.current.key, input));
    } catch (err) {
      const server = planFieldErrorsOf(err);
      if (isConcurrencyConflict(err)) { setMessage("Not added: someone else changed this treatment plan. What you entered is still here."); onConflict(); }
      else if (Object.keys(server).length > 0) { setErrors(server); setMessage("Not added: some entries need correcting. What you entered is still here."); }
      else if (isNetworkFailure(err)) setMessage("Not added: the connection dropped. What you entered is still here; try again.");
      else setMessage(`Not added: ${err instanceof ApiError ? err.message : "something went wrong."} What you entered is still here.`);
    } finally {
      setBusy(false);
    }
  }

  const rest = otherMessages(errors, KNOWN);
  return (
    <form className="alv-dx__form" aria-label="Add a procedure to this treatment plan" onSubmit={(e) => { e.preventDefault(); void save(); }}>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <TreatmentPlanItemFields idBase={`${uid}-add`} diagnoses={diagnoses} procedures={procedures} value={item} onChange={(v) => { setItem(v); setMessage(null); }} errors={errors} />
      {rest.length > 0 && <ul className="alv-tp__error">{rest.map((m) => <li key={m}>{m}</li>)}</ul>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Add procedure</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
