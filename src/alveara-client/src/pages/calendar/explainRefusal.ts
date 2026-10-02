import { ApiError } from "../../services/authApi";
import { UNAVAILABLE_REASONS, conflictingAppointmentIdOf, getAppointment, timeOf } from "../../services/schedulingApi";

/**
 * ALV-004-C01: turns a refused booking or reschedule into the plain words shown in the drawer, naming WHAT is in the way (the appointment already
 * holding the provider, operatory or patient, with its time) - the "conflict explanation". The server decides every conflict; this only explains.
 */
export async function explainRefusal(err: unknown, names: { provider?: string; operatory?: string }): Promise<string[]> {
  if (!(err instanceof ApiError)) return ["Could not reach the server. Check your connection and try again."];

  const holderId = conflictingAppointmentIdOf(err);
  let holder = "";
  if (holderId) {
    try {
      const a = await getAppointment(holderId);
      holder = ` from ${timeOf(a.startLocal)} to ${timeOf(a.endLocal)}`;
    } catch {
      holder = "";
    }
  }
  switch (err.code) {
    case "provider_double_booked":
      return [`${names.provider ?? "The provider"} already has an appointment${holder || " during that time"}.`, "Nothing was changed. Choose another time or provider."];
    case "operatory_conflict":
      return [`${names.operatory ?? "That operatory"} is already in use${holder || " during that time"}.`, "Nothing was changed. Choose another time or operatory."];
    case "patient_double_booked":
      return [`This patient already has an appointment${holder || " during that time"}.`, "A patient cannot be in two places at once. Nothing was changed."];
    case "provider_unavailable":
      return [`${names.provider ?? "The provider"} is not available: ${UNAVAILABLE_REASONS[String(err.body.reason)] ?? "they are not available then"}.`, "Nothing was changed. Choose another time."];
    case "schedule_busy":
      return ["The schedule is busy right now. Please try again in a moment."];
    case "appointment_not_scheduled":
    case "no_show_too_early":
    case "reason_required":
    case "reason_too_long":
    case "invalid_duration":
    case "invalid_local_time":
    case "invalid_notes":
    case "start_in_past":
    case "patient_inactive":
    case "operatory_inactive":
    case "appointment_type_inactive":
    case "validation_failed":
      return [err.message];
    default:
      return [err.status === 403 ? "Your role is not allowed to change appointments." : err.message || "That did not work. Try again."];
  }
}
