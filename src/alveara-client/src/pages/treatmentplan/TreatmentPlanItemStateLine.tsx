import type { PlanItem } from "../../services/treatmentPlansApi";
import { dayOf, when } from "./treatmentPlanRules";

/**
 * ALV-015-C01: where a proposed procedure stands: the patient's answer (or that none has been given), who gave it and who entered it, why it was declined, whether it is scheduled and whether it is completed.
 * Every word is text (never colour alone) and comes from the plan response, so a reader who may not change plans sees exactly what a dentist sees.
 */
export function TreatmentPlanItemStateLine({ item }: { item: PlanItem }) {
  const answered = item.acceptance !== "Pending";
  return (
    <div className="alv-tp__state">
      <p className="alv-clinical__meta">
        <span className="alv-dx__badge">{answered ? item.acceptance : "Not yet answered"}</span>
        {item.scheduling === "Scheduled" && <> <span className="alv-dx__badge">Scheduled{item.plannedDate ? ` for ${dayOf(item.plannedDate)}` : ""}</span></>}
        {item.completion === "Completed" && <> <span className="alv-dx__badge">Completed</span></>}
      </p>
      {answered && (
        <p className="alv-clinical__meta">
          {item.decidedByName ? `Answer given by ${item.decidedByName}. ` : ""}
          Entered{item.recordedByName ? ` by ${item.recordedByName}` : ""}{item.recordedAtUtc ? ` on ${when(item.recordedAtUtc)}` : ""}{item.decisionRevision ? ` (plan revision ${item.decisionRevision})` : ""}.
          {item.acceptance === "Declined" && item.decisionReason ? ` Reason: ${item.decisionReason}` : ""}
        </p>
      )}
    </div>
  );
}
