import { useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { completeAcceptedProcedures, planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";
import type { ChangeProps } from "./TreatmentPlanActions";
import { acceptedOpenItems } from "./treatmentPlanRules";

const label = (i: PlanItem) => [i.procedureCode, i.procedureDescription, i.toothKey ? `tooth ${i.toothKey}` : null, i.surface ? `surface ${i.surface}` : null].filter(Boolean).join(" · ");

/**
 * STORY-008: complete every accepted procedure of a plan at once. The confirmation lists exactly what will be completed; completing is final and all-or-nothing, so a refusal, a stale copy or a dropped
 * connection completes none of them and says so. The plan's version travels with the request, so what is completed is what was on screen.
 */
export function TreatmentPlanCompleteAccepted({ plan, onDone, onConflict, onCancel }: { plan: TreatmentPlan; onDone: ChangeProps["onChanged"]; onConflict: () => void; onCancel: () => void }) {
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const open = acceptedOpenItems(plan);

  async function confirm() {
    setBusy(true);
    setNote(null);
    try {
      onDone(await completeAcceptedProcedures(plan), `${open.length} accepted procedure${open.length === 1 ? "" : "s"} completed and added to the clinical history.`);
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (isConcurrencyConflict(err)) { setNote("Nothing was completed: this plan changed since you opened it. Reload and look again."); onConflict(); }
      else if (fields.length > 0) setNote(`Nothing was completed: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Nothing was completed: the connection dropped. Try again.");
      else setNote(`Nothing was completed: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label="Complete the accepted procedures" onSubmit={(e) => { e.preventDefault(); void confirm(); }}>
      <p className="alv-clinical__meta">These {open.length} procedure{open.length === 1 ? "" : "s"} will be completed and added to the clinical history. Completed is final: the answer and the schedule can no longer be changed.</p>
      <ul aria-label="Procedures that will be completed">{open.map((i) => <li key={i.id}>{label(i)}</li>)}</ul>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Confirm: complete {open.length}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
