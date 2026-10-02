/**
 * STORY-004: typed client for scheduling (Controllers/AppointmentsController).
 * Booking sends an `Idempotency-Key` header: the caller makes one key per booking attempt and reuses it on a retry of the SAME request, so a
 * retry after a dropped connection returns the same appointment instead of booking twice. Start times are practice-local wall-clock
 * ("yyyy-MM-ddTHH:mm"); the server owns the time zone.
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

export interface Appointment {
  id: string;
  patientId: string;
  patientName: string;
  providerId: string;
  providerName: string;
  operatoryId: string;
  operatoryName: string;
  appointmentTypeId: string;
  appointmentTypeName: string;
  startUtc: string;
  endUtc: string;
  /** Practice-local "yyyy-MM-ddTHH:mm". */
  startLocal: string;
  endLocal: string;
  durationMinutes: number;
  status: string;
  createdAtUtc: string;
  /** ALV-004-C01: the version to send back with a reschedule, cancel, no-show or note change. */
  rowVersion?: string | null;
  notes?: string | null;
  cancelReason?: string | null;
  statusChangedAtUtc?: string | null;
  /** STORY-011: where the patient is in the visit (see PatientFlowState). Absent on appointments from before patient flow existed. */
  flowState?: PatientFlowState;
  flowChangedAtUtc?: string | null;
}

export interface ScheduleInput {
  patientId: string;
  providerId: string;
  operatoryId: string;
  appointmentTypeId: string;
  startLocal: string;
  /** Blank = the appointment type's default. */
  durationMinutes: number | null;
  notes?: string | null;
}

/** One key per booking attempt (renewed whenever the request changes, reused on every retry of the same request). */
export const newScheduleKey = () => `book-${crypto.randomUUID()}`;

export async function scheduleAppointment(input: ScheduleInput, idempotencyKey: string): Promise<Appointment> {
  const csrfToken = await fetchCsrfToken();
  return request<Appointment>("/api/appointments", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(input),
  });
}

export function listAppointments(fromDay: string, toDay: string, providerId?: string, signal?: AbortSignal): Promise<Appointment[]> {
  const params = new URLSearchParams({ from: fromDay, to: toDay });
  if (providerId) params.set("providerId", providerId);
  return request<Appointment[]>(`/api/appointments?${params}`, { signal });
}

export const getAppointment = (id: string, signal?: AbortSignal) => request<Appointment>(`/api/appointments/${id}`, { signal });

/** The id of the appointment already holding the time behind a provider/operatory conflict, if any. */
export function conflictingAppointmentIdOf(err: unknown): string | null {
  if (!(err instanceof ApiError)) return null;
  return typeof err.body.conflictingAppointmentId === "string" ? err.body.conflictingAppointmentId : null;
}

export const UNAVAILABLE_REASONS: Record<string, string> = {
  outside_working_hours: "that time is outside the provider's working hours",
  blocked_time: "the provider has blocked that time",
  provider_inactive: "the provider is no longer active",
  crosses_dst_transition: "that time spans a daylight-saving change, which cannot be booked",
};

/** "09:00" from a practice-local "yyyy-MM-ddTHH:mm". */
export const timeOf = (local: string) => local.slice(11, 16);
export const dateOf = (local: string) => local.slice(0, 10);

// ---------- ALV-004-C01: the calendar and what can happen to a booked appointment ----------

export type AppointmentStatus = "Scheduled" | "Cancelled" | "NoShow";

/** STORY-011: Scheduled -> CheckedIn -> InTreatment (optional) -> Completed. Only a Scheduled appointment has a flow. */
export type PatientFlowState = "Scheduled" | "CheckedIn" | "InTreatment" | "Completed";

export interface AppointmentEvent {
  eventType: string;
  actorUserId: string | null;
  occurredAtUtc: string;
  detail: string | null;
  previousStartLocal: string | null;
  previousProviderName: string | null;
  previousOperatoryName: string | null;
}

/** Every appointment in the range whatever its status (cancelled and no-show ones are shown distinctly by the calendar). */
export function listCalendar(fromDay: string, toDay: string, providerId?: string, operatoryId?: string, signal?: AbortSignal): Promise<Appointment[]> {
  const params = new URLSearchParams({ from: fromDay, to: toDay, includeAll: "true" });
  if (providerId) params.set("providerId", providerId);
  if (operatoryId) params.set("operatoryId", operatoryId);
  return request<Appointment[]>(`/api/appointments?${params}`, { signal });
}

export const getAppointmentHistory = (id: string, signal?: AbortSignal) => request<AppointmentEvent[]>(`/api/appointments/${id}/history`, { signal });

export interface RescheduleInput {
  providerId: string;
  operatoryId: string;
  startLocal: string;
  /** Blank = keep the appointment's current duration. */
  durationMinutes: number | null;
}

export const rescheduleAppointment = (id: string, input: RescheduleInput, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/appointments/${id}/reschedule`, "PUT", { ...input, rowVersion }); 
export const cancelAppointment = (id: string, reason: string, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/appointments/${id}/cancel`, "POST", { reason, rowVersion });
export const markNoShow = (id: string, rowVersion: string) => requestWithCsrf<Appointment>(`/api/appointments/${id}/no-show`, "POST", { rowVersion });
export const updateAppointmentNotes = (id: string, notes: string, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/appointments/${id}/notes`, "PUT", { notes, rowVersion });

// ---------- STORY-011: patient flow ----------

export const checkInPatient = (id: string, rowVersion: string) => requestWithCsrf<Appointment>(`/api/appointments/${id}/check-in`, "POST", { rowVersion });
export const startTreatment = (id: string, rowVersion: string) => requestWithCsrf<Appointment>(`/api/appointments/${id}/start-treatment`, "POST", { rowVersion });
export const completeTreatment = (id: string, rowVersion: string) => requestWithCsrf<Appointment>(`/api/appointments/${id}/complete`, "POST", { rowVersion });
