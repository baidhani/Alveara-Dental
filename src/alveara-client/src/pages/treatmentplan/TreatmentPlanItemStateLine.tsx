import type { PlanItem } from "../../services/treatmentPlansApi";
import { dayOf, when } from "./treatmentPlanRules";

/** ALV-N023 (DR-1): whether a real appointment books the procedure, read live from the appointment. */
function bookingWords(item: PlanItem): string {
  const a = item.appointment;
  if (!a) return "Not booked.";
  if (a.live) return `Booked: ${when(a.startUtc)}${a.providerName ? ` with ${a.providerName}` : ""}.`;
  return `The appointment on ${when(a.startUtc)} was ${a.status === "NoShow" ? "marked no-show" : "cancelled"}, so it is not booked.`;
}

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
        {/* ALV-N023 (DR-1, PD-15): a target date is a target, never a booking */}
        {item.scheduling === "Scheduled" && item.plannedDate && <> <span className="alv-dx__badge">Target date {dayOf(item.plannedDate)}</span></>}
        {item.targetOverdue && <> <span className="alv-dx__badge alv-tp__overdue">Overdue</span></>}
        {item.completion === "Completed" && <> <span className="alv-dx__badge">Completed</span></>}
      </p>
      {item.acceptance === "Accepted" && item.completion !== "Completed" && <p className="alv-clinical__meta" data-testid="booking">{bookingWords(item)}</p>}
      {answered && (
        <p className="alv-clinical__meta">
          {item.decidedByName ? `Answer given by ${item.decidedByName}. ` : ""}
          {/* ALV-N021 A1 (F-184, F-218): when the answer itself was entered, which scheduling later never moves; any later change is shown apart */}
          Entered{item.answerRecordedByName ? ` by ${item.answerRecordedByName}` : ""}{item.answerRecordedAtUtc ? ` on ${when(item.answerRecordedAtUtc)}` : ""}{item.decisionRevision ? ` (plan revision ${item.decisionRevision})` : ""}.
          {item.acceptance === "Declined" && item.decisionReason ? ` Reason: ${item.decisionReason}` : ""}
          {item.recordedAtUtc && item.recordedAtUtc !== item.answerRecordedAtUtc ? ` Last changed${item.recordedByName ? ` by ${item.recordedByName}` : ""} on ${when(item.recordedAtUtc)}.` : ""}
        </p>
      )}
    </div>
  );
}
