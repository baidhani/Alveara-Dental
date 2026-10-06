/**
 * STORY-013: typed client for structured diagnoses (Controllers/DiagnosesController).
 *
 * - A diagnosis belongs to a patient and an encounter of that patient; both are authoritative and never change.
 * - `treatmentPlanReference` is an opaque FORWARD reference: treatment plans do not exist yet, it is never looked up, and `treatmentPlanReferenceState` is "Unresolved" whenever it is present. A
 *   correction leaves it as it is unless `treatmentPlanReference` is given (replace) or `clearTreatmentPlanReference` is true (remove); both are recorded in the history.
 * - A change echoes the `rowVersion` it read (a stale one is the shared 409 concurrency conflict). A diagnosis is never deleted: it is corrected or withdrawn with a reason.
 * - A save carries an idempotency key; the same key and entry returns the diagnosis already saved.
 * - ALV-013-C01: a diagnosis may carry optional coding (system and code together), a source with a note, and a tooth OR an oral region; it can be amended (structure), resolved and reactivated (with a
 *   reason), and linked to a finding or periodontal chart of the same patient. Nothing here ever marks the treatment-plan reference as resolved.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

export interface Diagnosis {
  id: string;
  patientId: string;
  encounterId: string;
  encounterAtUtc: string;
  label: string;
  toothKey: string | null;
  notes: string | null;
  treatmentPlanReference: string | null;
  /** "Unresolved" when a reference is present, null when there is none. */
  treatmentPlanReferenceState: string | null;
  status: "Active" | "Resolved" | "Withdrawn";
  recordedByName: string | null;
  recordedAtUtc: string;
  updatedByName: string | null;
  updatedAtUtc: string | null;
  withdrawnByName: string | null;
  withdrawnAtUtc: string | null;
  withdrawnReason: string | null;
  rowVersion: string;
  codingSystem?: string | null;
  code?: string | null;
  source?: string;
  sourceNote?: string | null;
  regionKey?: string | null;
  links?: DiagnosisLink[];
}

/** A link to a finding or periodontal chart of the same patient: what it points at in words, who linked it and when. */
export interface DiagnosisLink { linkType: "Finding" | "PerioExam"; targetId: string; summary: string; linkedByName: string | null; linkedAtUtc: string }

export interface DiagnosisVersion {
  versionNumber: number;
  changeType: "Recorded" | "Corrected" | "Withdrawn" | "Amended" | "Resolved" | "Reactivated";
  label: string;
  toothKey: string | null;
  notes: string | null;
  treatmentPlanReference: string | null;
  treatmentPlanReferenceState: string | null;
  status: string;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
  codingSystem?: string | null;
  code?: string | null;
  source?: string;
  sourceNote?: string | null;
  regionKey?: string | null;
}

export interface DiagnosisProblem { field: string; code: string; message: string }

export interface DiagnosisEntryInput {
  encounterId: string;
  label: string;
  toothKey: string | null;
  notes: string | null;
  treatmentPlanReference: string | null;
  /** ALV-013-C01: optional; left out means none (and the source is Manual). */
  codingSystem?: string | null;
  code?: string | null;
  source?: string | null;
  sourceNote?: string | null;
  regionKey?: string | null;
}

/** ALV-013-C01: the whole structure as it should now stand (null means none). The label, notes and treatment-plan reference are not touched. */
export interface DiagnosisAmendmentInput {
  toothKey: string | null;
  regionKey: string | null;
  codingSystem: string | null;
  code: string | null;
  source: string | null;
  sourceNote: string | null;
  reason: string;
}

export interface DiagnosisCorrectionInput {
  label: string;
  toothKey: string | null;
  notes: string | null;
  /** Left null the current reference is kept; a value replaces it. */
  treatmentPlanReference: string | null;
  /** True removes the reference (never combined with a value). */
  clearTreatmentPlanReference: boolean;
  reason: string;
}

export const listDiagnoses = (patientId: string, options: { encounterId?: string; includeWithdrawn?: boolean } = {}, signal?: AbortSignal) => {
  const q = new URLSearchParams();
  if (options.encounterId) q.set("encounterId", options.encounterId);
  if (options.includeWithdrawn) q.set("includeWithdrawn", "true");
  const qs = q.toString();
  return request<{ patientId: string; diagnoses: Diagnosis[] }>(`/api/patients/${patientId}/diagnoses${qs ? `?${qs}` : ""}`, { signal });
};

export const getDiagnosis = (id: string, signal?: AbortSignal) => request<Diagnosis>(`/api/diagnoses/${id}`, { signal });
export const getDiagnosisHistory = (id: string, signal?: AbortSignal) => request<{ diagnosisId: string; versions: DiagnosisVersion[] }>(`/api/diagnoses/${id}/history`, { signal });

export const recordDiagnosis = (patientId: string, idempotencyKey: string, entry: DiagnosisEntryInput) =>
  requestWithCsrf<Diagnosis>(`/api/patients/${patientId}/diagnoses`, "POST", { idempotencyKey, ...entry });

export const correctDiagnosis = (diagnosis: Pick<Diagnosis, "id" | "rowVersion">, correction: DiagnosisCorrectionInput) =>
  requestWithCsrf<Diagnosis>(`/api/diagnoses/${diagnosis.id}/correct`, "POST", { rowVersion: diagnosis.rowVersion, ...correction });

export const withdrawDiagnosis = (diagnosis: Pick<Diagnosis, "id" | "rowVersion">, reason: string) =>
  requestWithCsrf<Diagnosis>(`/api/diagnoses/${diagnosis.id}/withdraw`, "POST", { rowVersion: diagnosis.rowVersion, reason });

type Versioned = Pick<Diagnosis, "id" | "rowVersion">;

export const amendDiagnosis = (diagnosis: Versioned, amendment: DiagnosisAmendmentInput) =>
  requestWithCsrf<Diagnosis>(`/api/diagnoses/${diagnosis.id}/amend`, "POST", { rowVersion: diagnosis.rowVersion, ...amendment });

export const resolveDiagnosis = (diagnosis: Versioned, reason: string) =>
  requestWithCsrf<Diagnosis>(`/api/diagnoses/${diagnosis.id}/resolve`, "POST", { rowVersion: diagnosis.rowVersion, reason });

export const reactivateDiagnosis = (diagnosis: Versioned, reason: string) =>
  requestWithCsrf<Diagnosis>(`/api/diagnoses/${diagnosis.id}/reactivate`, "POST", { rowVersion: diagnosis.rowVersion, reason });

/** Links a diagnosis to a finding or periodontal chart of the same patient (once; the same link again changes nothing). */
export const linkDiagnosis = (diagnosisId: string, linkType: DiagnosisLink["linkType"], targetId: string) =>
  requestWithCsrf<Diagnosis>(`/api/diagnoses/${diagnosisId}/links`, "POST", { linkType, targetId });

/** The entries a refused save lists (empty for any other failure), so the screen can point at each one. */
export function diagnosisProblemsOf(err: unknown): DiagnosisProblem[] {
  if (!(err instanceof ApiError)) return [];
  const raw = err.body.problems;
  return Array.isArray(raw) ? (raw as DiagnosisProblem[]) : [];
}
