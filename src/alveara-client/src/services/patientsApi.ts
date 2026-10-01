/**
 * STORY-003: typed client for patient registration (Controllers/PatientsController).
 * Registration sends an `Idempotency-Key` header: the caller generates one key per registration
 * attempt and reuses it on a retry, so a retried request returns the same patient instead of
 * creating a second one. Reuses authApi's cookie-session + CSRF helpers.
 */
import { ApiError, fetchCsrfToken, request } from "./authApi";

export interface RegisterPatientInput {
  firstName: string;
  middleName: string;
  lastName: string;
  /** yyyy-MM-dd (a calendar date, never a timestamp). */
  dateOfBirth: string;
  sex: string;
  phone: string;
  email: string;
  addressLine1: string;
  addressLine2: string;
  city: string;
  state: string;
  postalCode: string;
}

export interface PatientRecord extends RegisterPatientInput {
  id: string;
  createdAtUtc: string;
}

export async function registerPatient(input: RegisterPatientInput, idempotencyKey: string): Promise<PatientRecord> {
  const csrfToken = await fetchCsrfToken();
  return request<PatientRecord>("/api/patients", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(input),
  });
}

/** The per-field messages of a 400 `validation_failed` response (empty for any other error). */
export function fieldErrorsOf(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError) || err.code !== "validation_failed") return {};
  const raw = err.body.fieldErrors;
  return raw && typeof raw === "object" ? (raw as Record<string, string>) : {};
}

/** The id of the already-registered patient behind a 409 `duplicate_patient` response, if any. */
export function existingPatientIdOf(err: unknown): string | null {
  if (!(err instanceof ApiError) || err.code !== "duplicate_patient") return null;
  return typeof err.body.existingPatientId === "string" ? err.body.existingPatientId : null;
}
