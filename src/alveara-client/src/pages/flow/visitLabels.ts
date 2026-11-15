import { ApiError } from "../../services/authApi";
import type { PatientFlowState } from "../../services/schedulingApi";
import type { ReadinessStatus } from "../../services/visitsApi";

/** Column headings: the visit's states in words. */
export const STATE_LABELS: Record<PatientFlowState, string> = {
  Scheduled: "Scheduled", Confirmed: "Confirmed", CheckedIn: "Checked in", Ready: "Ready", Seated: "Seated",
  InTreatment: "In treatment", CheckedOut: "Ready for checkout", Completed: "Completed",   // ALV-N021 A2 (briefing 5.1): CheckedOut is a compatibility name
};

/** Said after a move went through (read out by the live region). ALV-N021 A2: button labels, and who may press them, come from the server's actions list. */
export const DONE_LABELS: Partial<Record<PatientFlowState, string>> = {
  Confirmed: "confirmed", CheckedIn: "checked in", Ready: "marked ready", Seated: "seated",
  InTreatment: "in treatment", CheckedOut: "ready for checkout", Completed: "completed",
};

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
    // ALV-N021 A2: the server's own sentence for each workflow rule
    case "illegal_transition":
    case "terminal_state":
    case "block_flag_unresolved":
    case "checkout_incomplete":
    case "check_in_wrong_day":
    case "undo_check_in_too_late":
    case "not_flagged":
    case "reason_required":
    case "provider_double_booked":
    case "operatory_double_booked":
    case "operatory_conflict":       // ALV-N021 A2: a room change moves the booking, so the booking guards answer first
    case "patient_double_booked":
    case "provider_unavailable":
      return `${err.message} Nothing was changed.`;
    case "visit_move_forbidden":
      return `${err.message || "Your role is not allowed to make that change."} Nothing was changed.`;
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
