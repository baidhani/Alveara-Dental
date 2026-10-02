/**
 * STORY-003 / ALV-003-C01: typed client for the patient API (Controllers/PatientsController).
 * Registration sends an `Idempotency-Key` header: the caller generates one key per registration
 * attempt and reuses it on a retry, so a retried request returns the same patient instead of
 * creating a second one. Every edit echoes back the `rowVersion` it read; a stale one comes back as
 * the shared 409 concurrency-conflict shape (see authApi.isConcurrencyConflict). There is no delete:
 * patients are only inactivated. Reuses authApi's cookie-session + CSRF helpers.
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

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
  isActive?: boolean;
  rowVersion?: string;
}

export interface PatientSummary {
  id: string;
  firstName: string;
  middleName: string | null;
  lastName: string;
  dateOfBirth: string;
  age: number;
  sex: string | null;
  phone: string;
  city: string;
  state: string;
  isActive: boolean;
}

export interface PatientLink {
  id: string;
  displayName: string;
  isActive: boolean;
}

export interface HouseholdMember extends PatientLink {
  relationship: string | null;
}

export interface PatientDetail extends RegisterPatientInput {
  id: string;
  age: number;
  createdAtUtc: string;
  updatedAtUtc: string | null;
  isActive: boolean;
  rowVersion: string;
  guarantor: PatientLink | null;
  guaranteeFor: PatientLink[];
  household: { id: string; relationship: string | null; members: HouseholdMember[] } | null;
}

export interface DuplicateCandidate {
  id: string;
  firstName: string;
  middleName: string | null;
  lastName: string;
  dateOfBirth: string;
  phone: string;
  email: string | null;
  city: string;
  state: string;
  isActive: boolean;
  reasons: string[];
  exact: boolean;
}

export interface PatientHistoryRow {
  id: string;
  changedAtUtc: string;
  changedByUserId: string | null;
  changeType: string;
  fieldName: string;
  oldValue: string | null;
  newValue: string | null;
}

/** Which optional fields this practice requires (ALV-003-C01). `rowVersion` is "" until the practice has saved settings once. */
export interface RegistrationSettings {
  requireEmail: boolean;
  requireSex: boolean;
  rowVersion: string;
  updatedAtUtc: string | null;
}
export interface PatientRequirements {
  requireEmail: boolean;
  requireSex: boolean;
}

export const getRegistrationSettings = (signal?: AbortSignal) => request<RegistrationSettings>("/api/patients/registration-settings", { signal });
export const saveRegistrationSettings = (requireEmail: boolean, requireSex: boolean, rowVersion: string) =>
  requestWithCsrf<RegistrationSettings>("/api/patients/registration-settings", "PUT", { requireEmail, requireSex, rowVersion });

export const HOUSEHOLD_RELATIONSHIPS = ["Self", "Spouse", "Parent", "Child", "Sibling", "Grandparent", "Grandchild", "Other"] as const;

export async function registerPatient(input: RegisterPatientInput, idempotencyKey: string, acknowledgedDuplicateIds?: string[]): Promise<PatientRecord> {
  const csrfToken = await fetchCsrfToken();
  const body = acknowledgedDuplicateIds && acknowledgedDuplicateIds.length > 0 ? { ...input, acknowledgedDuplicateIds } : input;
  return request<PatientRecord>("/api/patients", {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(body),
  });
}

export const checkDuplicates = (input: RegisterPatientInput) =>
  requestWithCsrf<{ candidates: DuplicateCandidate[] }>("/api/patients/duplicate-check", "POST", input);

export function searchPatients(query: string, includeInactive: boolean, signal?: AbortSignal): Promise<PatientSummary[]> {
  const params = new URLSearchParams({ q: query, includeInactive: String(includeInactive), take: "25" });
  return request<PatientSummary[]>(`/api/patients?${params}`, { signal });
}

export const getPatient = (id: string, signal?: AbortSignal) => request<PatientDetail>(`/api/patients/${id}`, { signal });
export const getPatientHistory = (id: string, signal?: AbortSignal) => request<PatientHistoryRow[]>(`/api/patients/${id}/history`, { signal });

export const updatePatient = (id: string, input: RegisterPatientInput, rowVersion: string) =>
  requestWithCsrf<PatientRecord>(`/api/patients/${id}`, "PUT", { ...input, rowVersion });
export const setPatientActive = (id: string, isActive: boolean, rowVersion: string) =>
  requestWithCsrf<PatientRecord>(`/api/patients/${id}/active`, "PUT", { isActive, rowVersion });
export const setGuarantor = (id: string, guarantorPatientId: string | null, rowVersion: string) =>
  requestWithCsrf<PatientRecord>(`/api/patients/${id}/guarantor`, "PUT", { guarantorPatientId, rowVersion });
export const setHousehold = (id: string, anchorPatientId: string | null, relationship: string | null, rowVersion: string) =>
  requestWithCsrf<PatientRecord>(`/api/patients/${id}/household`, "PUT", { anchorPatientId, relationship, rowVersion });

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

/** The candidates behind a 409 `possible_duplicate` or `duplicate_patient` response (empty for any other error). */
export function candidatesOf(err: unknown): DuplicateCandidate[] {
  if (!(err instanceof ApiError) || (err.code !== "possible_duplicate" && err.code !== "duplicate_patient")) return [];
  return Array.isArray(err.body.candidates) ? (err.body.candidates as DuplicateCandidate[]) : [];
}

export const displayName = (p: { firstName: string; lastName: string }) => `${p.firstName} ${p.lastName}`;
