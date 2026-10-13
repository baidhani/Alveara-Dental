import { useState } from "react";
import type { PlanItem, TreatmentPlan } from "../../services/treatmentPlansApi";
import type { ChangeProps } from "./TreatmentPlanActions";
import { TreatmentPlanDecisionForm } from "./TreatmentPlanDecisionForm";
import { TreatmentPlanScheduleForm } from "./TreatmentPlanScheduleForm";

type Mode = "accept" | "decline" | "schedule" | null;

/**
 * ALV-015-C01: what a person who may change plans can do with one procedure of a Proposed plan: record that the patient accepted or declined it and mark it scheduled (completing is done on the plan, with Review and complete). Only what makes sense
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
        </div>
      )}
      {mode === "accept" && <TreatmentPlanDecisionForm plan={plan} item={item} decision="Accepted" onDone={done} onConflict={onConflict} onCancel={cancel} />}
      {mode === "decline" && <TreatmentPlanDecisionForm plan={plan} item={item} decision="Declined" onDone={done} onConflict={onConflict} onCancel={cancel} />}
      {mode === "schedule" && <TreatmentPlanScheduleForm plan={plan} item={item} onDone={done} onCancel={cancel} />}
    </div>
  );
}
