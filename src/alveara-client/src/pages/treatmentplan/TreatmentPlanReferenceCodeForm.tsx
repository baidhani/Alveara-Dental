import { useId, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { planFieldErrorsOf, setReferenceCode } from "../../services/treatmentPlansApi";
import type { TreatmentPlan } from "../../services/treatmentPlansApi";

interface Props {
  plan: TreatmentPlan;
  onDone: (plan: TreatmentPlan, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-015-C01: give a plan its reference code: a short code, unique among this patient's plans and set once, that an old diagnosis's free-text treatment-plan reference can be matched to. It is not a change to
 * the plan's content, so it does not make a revision. A refusal (the code is in use, or the plan already has one) is shown beside the field and what was typed is kept.
 */
export function TreatmentPlanReferenceCodeForm({ plan, onDone, onConflict, onCancel }: Props) {
  const [code, setCode] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const id = useId();

  async function save() {
    if (code.trim() === "") return setNote("Not saved: enter the reference code.");
    setBusy(true);
    setNote(null);
    try {
      onDone(await setReferenceCode(plan, code.trim()), "Reference code set.");
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (isConcurrencyConflict(err)) { setNote("Not saved: someone else changed this treatment plan. Reload and try again."); onConflict(); }
      else if (fields.length > 0) setNote(`Not saved: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not saved: the connection dropped. Try again.");
      else setNote(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label="Set the reference code of this treatment plan" onSubmit={(e) => { e.preventDefault(); void save(); }}>
      <p className="alv-clinical__meta">A diagnosis recorded before plans existed may carry a free-text reference such as &quot;plan-2026-07&quot;. Give this plan that code and the reference can be tied to it. The code is set once.</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={id}>Reference code
        <input id={id} type="text" maxLength={120} value={code} onChange={(e) => { setCode(e.target.value); setNote(null); }} />
      </label>
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Set reference code</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
