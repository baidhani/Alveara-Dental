/**
 * STORY-017: typed client for finding possible duplicate patient records and merging two of them (Controllers/PatientMergesController). Administrator only.
 *
 * - `previewMerge` reads what a merge would do and changes nothing; `mergePatients` does it, with an `Idempotency-Key` (the same key for a retry of the same attempt, a new one when the choices change) and the record
 *   versions the preview showed, so a record changed since is 409 `patient_changed` and nothing is merged.
 * - Every refusal has a stable `error` code (see `mergeErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request } from "./authApi";

export interface PatientBrief {
  id: string; firstName: string; middleName: string | null; lastName: string; dateOfBirth: string; phone: string; email: string | null; city: string; state: string;
  isActive: boolean; createdAtUtc: string; householdId: string | null;
}

export interface DuplicatePair {
  first: PatientBrief; second: PatientBrief; score: number; reasons: string[]; suggestedSurvivorId: string; suggestionReason: string; sameHousehold: boolean;
}
export interface DuplicateScan { pairs: DuplicatePair[]; truncated: boolean; patientsConsidered: number }

export interface MergeFieldConflict { field: string; survivorValue: string | null; sourceValue: string | null }
export interface MergeAutoFill { field: string; sourceValue: string }
export interface MergeMoveCount { table: string; column: string; rows: number }
export interface MergeLeftBehind { table: string; rowKey: string; why: string }
export interface MergeBlocker { code: string; message: string }
export interface MergePreview {
  source: PatientBrief; survivor: PatientBrief; conflicts: MergeFieldConflict[]; autoFills: MergeAutoFill[]; moves: MergeMoveCount[]; leftBehind: MergeLeftBehind[];
  blockers: MergeBlocker[]; canMerge: boolean; sourceRowVersion: string; survivorRowVersion: string;
}

export type FieldChoice = "Survivor" | "Source";
export interface MergeInput { sourcePatientId: string; survivorPatientId: string; reason: string; sourceRowVersion: string; survivorRowVersion: string; fieldChoices: Record<string, FieldChoice> }
export interface MergeResolution { kind: "Field" | "Record"; subject: string; choice: string; replacedValue: string | null }
export interface MergeResult {
  mergeId: string; sourcePatientId: string; survivorPatientId: string; mergedByUserId: string; mergedAtUtc: string; reason: string; moves: MergeMoveCount[]; resolutions: MergeResolution[]; replayed: boolean;
}
export interface MergeLogEntry { mergeId: string; source: PatientBrief; survivor: PatientBrief; mergedByUserId: string; mergedByName: string; mergedAtUtc: string; reason: string; rowsMoved: number; recordsLeftBehind: number }

export const REASON_MAX = 500;

export const newMergeKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);

export const scanDuplicates = (signal?: AbortSignal) => request<DuplicateScan>("/api/patient-merges/duplicates", { signal });
export const previewMerge = (sourceId: string, survivorId: string, signal?: AbortSignal) =>
  request<MergePreview>(`/api/patient-merges/preview?sourceId=${encodeURIComponent(sourceId)}&survivorId=${encodeURIComponent(survivorId)}`, { signal });
export const getMergeLog = (signal?: AbortSignal) => request<MergeLogEntry[]>("/api/patient-merges/log", { signal });

export async function mergePatients(input: MergeInput, key: string): Promise<MergeResult> {
  const csrf = await fetchCsrfToken();
  return request<MergeResult>("/api/patient-merges", { method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-Token": csrf, "Idempotency-Key": key }, body: JSON.stringify(input) });
}

const MESSAGES: Record<string, string> = {
  choice_required: "Every field that differs needs an answer: keep the surviving record's value or take the retired record's.",
  choice_invalid: "One of the answers is not valid. Reload the review and choose again.",
  patient_changed: "One of the records changed after you reviewed it. Nothing was merged. Reload the review and check both records again.",
  already_merged: "The record to merge away has already been merged into another record. Reload the list.",
  survivor_merged: "The record to keep has itself been merged into another record. Reload the list and keep that one instead.",
  merge_blocked: "These records cannot be merged yet: something that must stay with the retired record is linked to something that would move. Nothing was changed.",
  merge_inconsistent: "The merge was stopped because the result would not have been consistent. Nothing was merged. Tell support.",
  merge_conflict: "Another request merged these records at the same moment. Reload the list.",
  key_reused: "That attempt was already used for a different merge. Review the records again.",
  reason_invalid: `Write the reason for the merge (up to ${REASON_MAX} characters) on one line.`,
  idempotency_key_invalid: "That did not work. Reload the review and try again.",
  row_version_required: "Reload the review and try again.",
  row_version_invalid: "Reload the review and try again.",
  same_patient: "A record cannot be merged into itself.",
  patient_not_found: "One of the records was not found. Reload the list.",
  permission_denied: "Only an administrator can merge patient records.",
};

/** A message safe to show for a refused merge request. */
export function mergeErrorMessage(err: unknown): string {
  if (err instanceof ApiError) return MESSAGES[err.code] ?? err.message ?? "That did not work. Try again.";
  return "That did not work. Check your connection and try again.";
}

/** The review is out of date and must be reloaded rather than retried. */
export const isStaleMerge = (err: unknown) => err instanceof ApiError && ["patient_changed", "already_merged", "survivor_merged", "merge_conflict", "key_reused"].includes(err.code);

export const briefName = (p: { firstName: string; middleName?: string | null; lastName: string }) => [p.firstName, p.middleName, p.lastName].filter(Boolean).join(" ");
