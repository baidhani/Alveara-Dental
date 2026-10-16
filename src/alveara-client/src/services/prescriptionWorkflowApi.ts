/**
 * ALV-016-C01: typed client for the prescription workflow (Controllers/PrescriptionWorkflowController), which adds drafts, finalization, amendment, cancellation, discontinuation and printing to STORY-016's
 * prescriptions (services/prescriptionApi.ts, unchanged).
 *
 * - A draft is a prescription being written. It may be saved incomplete, is edited while it is a draft (echo its `rowVersion`; a stale one is a 409 `concurrency_conflict`), and is never deleted (discarding keeps it).
 * - A draft remembers the allergy and medication lists the prescriber last acknowledged. If they changed, finalizing is refused (409 `safety_context_changed`, with what changed in `detail`) until the prescriber
 *   acknowledges the lists as they are now (`acknowledgeSafety`, which must echo the fingerprint the screen showed).
 * - Finalizing runs the same allergy check as STORY-016 (a match or an unestablished list needs the prescriber's own `reviewNote`, 409 `allergy_review_required`) and requires instructions, quantity and refills.
 *   A finalized prescription is never edited: it can be cancelled or discontinued (with a reason), amended (a new draft that replaces it only when finalized) and printed.
 * - There is NO drug-knowledge source: the options say `decisionSupportConfigured: false`, and nothing here checks interactions or whether a dose is clinically right.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";
import type { AllergyAssessment, AllergyListState, AllergyOutcome, PrescriptionProblem } from "./prescriptionApi";

export type DraftStatus = "Draft" | "Finalized" | "Discarded";
export type PrescriptionStatus = "Active" | "Cancelled" | "Discontinued" | "Superseded";

export interface WorkflowOptions {
  doseUnits: string[];
  doseTypingCeilings: Record<string, number>;
  frequencies: string[];
  quantityUnits: string[];
  quantityTypingCeiling: number;
  refillsMax: number;
  instructionsMax: number;
  medicationMax: number;
  reasonMax: number;
  decisionSupportConfigured: boolean;
  decisionSupportMessage: string;
  doseLimitation: string;
  quantityLimitation: string;
  allergyLimitation: string;
}

export interface DraftView {
  id: string;
  patientId: string;
  medication: string;
  doseValue: number;
  doseUnit: string;
  frequency: string;
  instructions: string | null;
  quantityValue: number | null;
  quantityUnit: string | null;
  refills: number | null;
  reviewNote: string | null;
  status: DraftStatus;
  supersedesPrescriptionId: string | null;
  finalizedPrescriptionId: string | null;
  discardedReason: string | null;
  safetyFingerprint: string;
  createdByName: string | null;
  createdAtUtc: string;
  updatedByName: string | null;
  updatedAtUtc: string;
  rowVersion: string;
}

export interface SavedDraft { draft: DraftView; replayed: boolean }

export interface SafetyCheck {
  fingerprint: string;
  changedSinceAcknowledged: boolean;
  added: string[];
  removed: string[];
  assessment: AllergyAssessment;
  reviewRequired: boolean;
  reviewProblem: PrescriptionProblem | null;
}

export interface DraftDetail { draft: DraftView; safety: SafetyCheck }

export interface DraftCheck {
  problems: PrescriptionProblem[];
  safety: SafetyCheck;
  canFinalize: boolean;
  doseLimitation: string;
  quantityLimitation: string;
  allergyLimitation: string;
  decisionSupport: string;
}

export interface PrescriptionEvent { eventType: string; reason: string | null; actorName: string | null; occurredAtUtc: string; supersededByPrescriptionId: string | null }

export interface PrescriptionRecord {
  id: string;
  patientId: string;
  medication: string;
  doseValue: number;
  doseUnit: string;
  frequency: string;
  instructions: string | null;
  quantityValue: number | null;
  quantityUnit: string | null;
  refills: number | null;
  allergyOutcome: AllergyOutcome;
  allergyListState: AllergyListState;
  allergyMatches: string | null;
  reviewNote: string | null;
  prescribedByName: string | null;
  prescribedAtUtc: string;
  status: PrescriptionStatus;
  supersedesPrescriptionId: string | null;
  supersededByPrescriptionId: string | null;
  hasDetails: boolean;
  events: PrescriptionEvent[];
}

export interface SavedRecord { record: PrescriptionRecord; replayed: boolean }

export interface PrintContent {
  prescriptionId: string;
  status: PrescriptionStatus;
  practice: { name: string; phone: string | null; addressLine: string | null } | null;
  patientName: string;
  patientDateOfBirth: string;
  prescriberName: string;
  medication: string;
  dose: string;
  frequency: string;
  instructions: string;
  quantity: string;
  refills: number;
  prescribedAtUtc: string;
  allergyStatement: string;
  limitationStatement: string;
  supersedesPrescriptionId: string | null;
  supersededByPrescriptionId: string | null;
  contentHash: string;
}

/** The entry as the composer sends it. Numbers are null when their box is empty. */
export interface WorkflowEntry {
  medication: string;
  doseValue: number | null;
  doseUnit: string;
  frequency: string;
  instructions: string | null;
  quantityValue: number | null;
  quantityUnit: string | null;
  refills: number | null;
  reviewNote: string | null;
}

export interface SafetyChange { currentFingerprint: string; added: string[]; removed: string[] }

const base = "/api/prescription-drafts";

export const getWorkflowOptions = (signal?: AbortSignal) => request<WorkflowOptions>("/api/prescription-workflow/options", { signal });
export const listDrafts = (patientId: string, signal?: AbortSignal) => request<{ patientId: string; drafts: DraftView[] }>(`/api/patients/${patientId}/prescription-drafts`, { signal });
export const getDraft = (id: string, signal?: AbortSignal) => request<DraftDetail>(`${base}/${id}`, { signal });
export const listRecords = (patientId: string, signal?: AbortSignal) => request<{ patientId: string; prescriptions: PrescriptionRecord[] }>(`/api/patients/${patientId}/prescription-records`, { signal });
export const getPrint = (id: string) => request<PrintContent>(`/api/prescriptions/${id}/print`);

export const createDraft = (patientId: string, idempotencyKey: string, entry: WorkflowEntry) =>
  requestWithCsrf<SavedDraft>(`/api/patients/${patientId}/prescription-drafts`, "POST", { idempotencyKey, ...entry });
export const updateDraft = (id: string, rowVersion: string, entry: WorkflowEntry) => requestWithCsrf<DraftView>(`${base}/${id}`, "PUT", { ...entry, rowVersion });
export const checkDraft = (id: string) => requestWithCsrf<DraftCheck>(`${base}/${id}/check`, "POST", {});
export const acknowledgeSafety = (id: string, rowVersion: string, seenFingerprint: string) => requestWithCsrf<DraftView>(`${base}/${id}/acknowledge-safety`, "POST", { rowVersion, seenFingerprint });
export const finalizeDraft = (id: string, rowVersion: string) => requestWithCsrf<SavedRecord>(`${base}/${id}/finalize`, "POST", { rowVersion });
export const discardDraft = (id: string, rowVersion: string, reason: string | null) => requestWithCsrf<DraftView>(`${base}/${id}/discard`, "POST", { rowVersion, reason });
export const amendPrescription = (id: string, idempotencyKey: string) => requestWithCsrf<SavedDraft>(`/api/prescriptions/${id}/amend`, "POST", { idempotencyKey });
export const cancelPrescription = (id: string, reason: string) => requestWithCsrf<SavedRecord>(`/api/prescriptions/${id}/cancel`, "POST", { reason });
export const discontinuePrescription = (id: string, reason: string) => requestWithCsrf<SavedRecord>(`/api/prescriptions/${id}/discontinue`, "POST", { reason });

/** What changed in the allergy and medication lists, from a refused finalization or acknowledgement (409 `safety_context_changed`), or null. */
export function safetyChangeOf(err: unknown): SafetyChange | null {
  if (!(err instanceof ApiError) || err.code !== "safety_context_changed") return null;
  const d = err.body.detail as Partial<SafetyChange> | undefined;
  return d && typeof d.currentFingerprint === "string" ? { currentFingerprint: d.currentFingerprint, added: d.added ?? [], removed: d.removed ?? [] } : null;
}
