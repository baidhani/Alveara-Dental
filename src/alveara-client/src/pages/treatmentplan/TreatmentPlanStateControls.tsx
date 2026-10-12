import { useState } from "react";
import { ApiError } from "../../services/authApi";
import { isNetworkFailure } from "../../services/clinicalApi";
import { completeItem, planFieldErrorsOf } from "../../services/treatmentPlansApi";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";
import type { ChangeProps } from "./TreatmentPlanActions";
import { TreatmentPlanDecisionForm } from "./TreatmentPlanDecisionForm";
import { TreatmentPlanScheduleForm } from "./TreatmentPlanScheduleForm";

type Mode = "accept" | "decline" | "schedule" | "complete" | null;

function CompleteConfirm({ plan, item, onDone, onCancel }: { plan: TreatmentPlan; item: PlanItem; onDone: (p: TreatmentPlan, text: string) => void; onCancel: () => void }) {
  const [note, setNote] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function confirm() {
    setBusy(true);
    setNote(null);
    try {
      onDone(await completeItem(plan.id, item.id), `${item.procedureCode} marked completed.`);
    } catch (err) {
      const fields = Object.values(planFieldErrorsOf(err));
      if (fields.length > 0) setNote(`Not saved: ${fields.join(" ")}`);
      else if (isNetworkFailure(err)) setNote("Not saved: the connection dropped. Try again.");
      else setNote(`Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}`);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`Mark ${item.procedureCode} completed`} onSubmit={(e) => { e.preventDefault(); void confirm(); }}>
      <p className="alv-clinical__meta">Completed is final: the answer and the schedule can no longer be changed, and the procedure stays on the plan.</p>
      <p className="alv-clinical__meta"><strong>Legacy completion.</strong> This records only that the procedure is completed: no provider, encounter, chart change or charge. Use Review and complete on the plan instead.</p>
      {note && <p className="alv-dx__message" role="alert">{note}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Confirm completed</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}

/**
 * ALV-015-C01: what a person who may change plans can do with one procedure of a Proposed plan: record that the patient accepted or declined it, mark it scheduled, and mark it completed. Only what makes sense
 * for where the procedure stands is offered (an unanswered procedure cannot be scheduled; a completed one is final and offers nothing). The server checks every call again.
 */
export function TreatmentPlanStateControls({ plan, item, onChanged, onConflict }: ChangeProps & { plan: TreatmentPlan; item: PlanItem }) {
  const [mode, setMode] = useState<Mode>(null);
  if (item.completion === "Completed") return null;
  const done = (p: TreatmentPlan, text: string) => { setMode(null); onChanged(p, text); };
  const cancel = () => setMode(null);
  const accepted = item.acceptance === "Accepted";
  const button = (m: Exclude<Mode, null>, label: string) => <button type="button" className="btn btn-outline-primary btn-sm" onClick={() => setMode(m)}>{label}</button>;

  return (
    <div className="alv-tp__controls">
      {mode === null && (
        <div className="alv-clinical__row-actions">
          {!accepted && button("accept", `Accept ${item.procedureCode}`)}
          {item.acceptance !== "Declined" && button("decline", `Decline ${item.procedureCode}`)}
          {accepted && button("schedule", `${item.scheduling === "Scheduled" ? "Change schedule of" : "Schedule"} ${item.procedureCode}`)}
          {accepted && button("complete", `Mark ${item.procedureCode} completed`)}
        </div>
      )}
      {mode === "accept" && <TreatmentPlanDecisionForm plan={plan} item={item} decision="Accepted" onDone={done} onConflict={onConflict} onCancel={cancel} />}
      {mode === "decline" && <TreatmentPlanDecisionForm plan={plan} item={item} decision="Declined" onDone={done} onConflict={onConflict} onCancel={cancel} />}
      {mode === "schedule" && <TreatmentPlanScheduleForm plan={plan} item={item} onDone={done} onCancel={cancel} />}
      {mode === "complete" && <CompleteConfirm plan={plan} item={item} onDone={done} onCancel={cancel} />}
    </div>
  );
}
