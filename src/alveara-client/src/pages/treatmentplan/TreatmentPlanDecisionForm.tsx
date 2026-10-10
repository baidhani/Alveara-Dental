import { useId, useState } from "react";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { decideItem, decidePlan, planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";

interface Props {
  plan: TreatmentPlan;
  /** The procedure being answered; absent means every procedure of the plan that has not been answered yet. */
  item?: PlanItem;
  decision: "Accepted" | "Declined";
  onDone: (plan: TreatmentPlan, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-015-C01: record the patient's answer. A decline says why; who gave the answer (the patient, a guardian) may be written down, and the signed-in account is recorded as who entered it. The answer is sent
 * with the plan's version, so what is recorded is what was on screen. Answering the whole plan never overwrites an opposite answer: the server refuses and says how many procedures already have one, and this form
 * shows that and keeps what was typed. Nothing is recorded by a refusal, a stale copy or a dropped connection.
 */
export function TreatmentPlanDecisionForm({ plan, item, decision, onDone, onConflict, onCancel }: Props) {
  const [reason, setReason] = useState("");
  const [by, setBy] = useState("");
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const uid = useId();
  const verb = decision === "Accepted" ? "Accept" : "Decline";
  const subject = item ? item.procedureCode : "all pending procedures";
  const past = decision === "Accepted" ? "accepted" : "declined";
  const needsReason = decision === "Declined";

  async function save() {
    if (needsReason && reason.trim() === "") return setNote("Not recorded: say why it was declined.");
    setBusy(true);
    setNote(null);
    const input = { decision, reason: needsReason ? reason.trim() : null, decidedByName: by.trim() || null };
    try {
      const saved = item ? await decideItem(plan, item.id, input) : await decidePlan(plan, input);
      onDone(saved, item ? `${item.procedureCode} ${past}.` : `All pending procedures ${past}.`);
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (isConcurrencyConflict(err)) { setNote("Not recorded: this plan changed since you opened it. Reload and look again."); onConflict(); }
      else if (fields.length > 0) setNote(`Not recorded: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not recorded: the connection dropped. What you entered is still here; try again.");
      else setNote(`Not recorded: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`${verb} ${subject}`} onSubmit={(e) => { e.preventDefault(); void save(); }}>
      <p className="alv-clinical__meta">
        {item ? `Record that the patient ${past} ${item.procedureCode}.` : `Record that the patient ${past} every procedure that has not been answered yet.`}
        {!item && " If any procedure already has the opposite answer, nothing is recorded and you decide those one by one."}
      </p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <label htmlFor={`${uid}-by`}>Who gave this answer? (optional)
        <input id={`${uid}-by`} type="text" maxLength={140} value={by} onChange={(e) => { setBy(e.target.value); setNote(null); }} />
        <span className="alv-clinical__meta">For example the patient or a guardian, with their name. You are recorded as the person who entered it.</span>
      </label>
      {needsReason && (
        <label htmlFor={`${uid}-reason`}>Why was it declined?
          <input id={`${uid}-reason`} type="text" maxLength={500} value={reason} onChange={(e) => { setReason(e.target.value); setNote(null); }} />
        </label>
      )}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>{decision === "Accepted" ? "Record acceptance" : "Record decline"}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
