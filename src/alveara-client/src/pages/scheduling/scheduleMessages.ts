import { ApiError } from "../../services/authApi";

/** The message for a refusal that is not a conflict: the server's own wording for validation problems, a plain fallback otherwise. */
export function formFieldMessage(err: unknown): string {
  if (!(err instanceof ApiError)) return "Could not book the appointment. Check your connection and try again.";
  if (err.code === "invalid_duration" || err.code === "invalid_local_time" || err.code === "start_in_past" || err.code === "validation_failed") return err.message;
  if (err.code === "patient_inactive" || err.code === "operatory_inactive" || err.code === "appointment_type_inactive") return err.message;
  if (err.code === "schedule_busy") return "The schedule is busy right now. Please try again in a moment.";
  if (err.code === "idempotency_key_reused") return "That request was already used for a different appointment. Reload the page and try again.";
  if (err.status === 403) return "Your role is not allowed to book appointments.";
  return err.message || "Could not book the appointment. Try again.";
}
