import { ApiError } from "../../services/authApi";
import type { PatientFlowState } from "../../services/schedulingApi";
import type { ReadinessStatus } from "../../services/visitsApi";

/** Column headings: the visit's states in words. */
export const STATE_LABELS: Record<PatientFlowState, string> = {
  Scheduled: "Scheduled", Confirmed: "Confirmed", CheckedIn: "Checked in", Ready: "Ready", Seated: "Seated",
  InTreatment: "In treatment", CheckedOut: "Checked out", Completed: "Completed",
};

/** The button for moving a visit INTO each state. */
export const ACTION_LABELS: Partial<Record<PatientFlowState, string>> = {
  Confirmed: "Confirm", CheckedIn: "Check in", Ready: "Mark ready", Seated: "Seat patient",
  InTreatment: "Start treatment", CheckedOut: "Check out", Completed: "Complete visit",
};

/** Said after a move went through (read out by the live region). */
export const DONE_LABELS: Partial<Record<PatientFlowState, string>> = {
  Confirmed: "confirmed", CheckedIn: "checked in", Ready: "marked ready", Seated: "seated",
  InTreatment: "in treatment", CheckedOut: "checked out", Completed: "completed",
};

/**
 * Which permission a move needs. This mirrors the server's rule (VisitStateMachine.DutyFor) only so the screen offers buttons the person can actually use;
 * the server decides every time and a mismatch is a 403 that is explained, never a silent success.
 */
export const permissionFor = (target: PatientFlowState): "UpdateVisitFlow" | "UpdateChairsideFlow" =>
  target === "Confirmed" || target === "CheckedIn" || target === "CheckedOut" ? "UpdateVisitFlow" : "UpdateChairsideFlow";

/** Completing a visit is final (there is no undo), so it asks first. Every other move is quick. */
export const needsConfirmation = (target: PatientFlowState) => target === "Completed";

export const READINESS_WORDS: Record<ReadinessStatus, string> = {
  Complete: "Complete", InProgress: "Started, not signed", SignedEarlierVersion: "Signed an earlier version", Missing: "Not done",
};

/** The plain words for a refused move or assignment, naming what is in the way. Always ends by saying nothing changed where that is true. */
export function explainVisitRefusal(err: unknown, ctx: { operatory?: string }): string {
  if (!(err instanceof ApiError)) return "We could not confirm whether that was saved - the connection dropped. Refresh the board to see where the patient is now, then try again.";
  switch (err.code) {
    case "operatory_occupied":
      return `${ctx.operatory ?? "That operatory"} already has a patient who is seated or in treatment. Nothing was changed. Use another room, or move on the patient who is there first.`;
    case "invalid_flow_transition":
    case "appointment_in_progress":
    case "appointment_not_scheduled":
    case "visit_completed":
    case "provider_inactive":
    case "operatory_inactive":
      return `${err.message} Nothing was changed.`;
    case "provider_not_found":
    case "operatory_not_found":
    case "appointment_not_found":
      return `${err.message} Refresh the board.`;
    case "schedule_busy":
      return "The schedule is busy right now. Please try again in a moment.";
    case "permission_denied":
      return "Your role is not allowed to make that change.";
    default:
      return err.status === 403 ? "Your role is not allowed to make that change." : err.message || "That did not work. Try again.";
  }
}
