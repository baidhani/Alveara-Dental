/**
 * STORY-016: typed client for prescriptions (Controllers/PrescriptionsController).
 *
 * - A prescription is checked and saved against a patient. `check` judges an entry without saving it (the problems, the allergy assessment, whether the prescriber's own review is required); `save` records it.
 * - "Dosage accuracy" here is STRUCTURAL: a positive number, a known unit and frequency, not above a typing-error ceiling for the unit. The application has no drug-knowledge source, so nothing here says a dose is
 *   clinically right, and the two limitation sentences travel with every answer so the screen can say so.
 * - The allergy assessment compares the medication's NAME with the patient's active recorded allergies. `NoRecordedMatch` is not a clearance; `NotEstablished` means the allergy list was never reviewed, is unknown
 *   or changed since it was confirmed. A match or an unestablished list needs the prescriber's own `reviewNote` before the save is accepted (409 `allergy_review_required`).
 * - A save carries an idempotency key; the same key and entry returns the prescription already saved (`replayed` true); the same key with a different entry is a 409. A prescription is never changed or deleted.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

export type AllergyOutcome = "RecordedAllergyMatch" | "NoRecordedMatch" | "NotEstablished";
export type AllergyListState = "Established" | "Unconfirmed" | "NotEstablished";

export interface PrescriptionProblem { field: string; code: string; message: string }

export interface RecordedAllergy { name: string; severity: string | null; reaction: string | null }

export interface AllergyAssessment {
  outcome: AllergyOutcome;
  matches: RecordedAllergy[];
  listState: AllergyListState;
  message: string;
  limitation: string;
}

export interface Prescription {
  id: string;
  patientId: string;
  medication: string;
  doseValue: number;
  doseUnit: string;
  frequency: string;
  allergyOutcome: AllergyOutcome;
  allergyListState: AllergyListState;
  allergyMatches: string | null;
  reviewNote: string | null;
  prescribedByName: string | null;
  prescribedAtUtc: string;
}

export interface SavedPrescription { prescription: Prescription; replayed: boolean }

export interface PrescriptionPreview {
  problems: PrescriptionProblem[];
  assessment: AllergyAssessment | null;
  reviewRequired: boolean;
  reviewProblem: PrescriptionProblem | null;
  doseLimitation: string;
  allergyLimitation: string;
}

export interface PrescriptionOptions {
  doseUnits: string[];
  typingCeilings: Record<string, number>;
  frequencies: string[];
  doseDecimals: number;
  medicationMax: number;
  reviewNoteMax: number;
  doseLimitation: string;
  allergyLimitation: string;
}

/** The entry as the form sends it. `doseValue` is null when the dose box is empty. */
export interface PrescriptionEntryInput {
  medication: string;
  doseValue: number | null;
  doseUnit: string;
  frequency: string;
  reviewNote: string | null;
}

export const getPrescriptionOptions = (signal?: AbortSignal) => request<PrescriptionOptions>("/api/prescriptions/options", { signal });

export const listPrescriptions = (patientId: string, signal?: AbortSignal) =>
  request<{ patientId: string; prescriptions: Prescription[] }>(`/api/patients/${patientId}/prescriptions`, { signal });

export const checkPrescription = (patientId: string, entry: PrescriptionEntryInput) =>
  requestWithCsrf<PrescriptionPreview>(`/api/patients/${patientId}/prescriptions/check`, "POST", entry);

export const savePrescription = (patientId: string, idempotencyKey: string, entry: PrescriptionEntryInput) =>
  requestWithCsrf<SavedPrescription>(`/api/patients/${patientId}/prescriptions`, "POST", { idempotencyKey, ...entry });

/** The problems a refused request lists (empty for any other failure), so the screen can point at each one. */
export function prescriptionProblemsOf(err: unknown): PrescriptionProblem[] {
  if (!(err instanceof ApiError)) return [];
  const raw = err.body.problems;
  return Array.isArray(raw) ? (raw as PrescriptionProblem[]) : [];
}

/** The allergy assessment a refused save carries (409 `allergy_review_required`), or null. */
export function assessmentOf(err: unknown): AllergyAssessment | null {
  if (!(err instanceof ApiError)) return null;
  const raw = err.body.assessment;
  return raw && typeof raw === "object" ? (raw as AllergyAssessment) : null;
}
