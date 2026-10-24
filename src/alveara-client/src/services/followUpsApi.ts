/**
 * STORY-009: typed client for follow-up tasks (Controllers/FollowUpsController, docs/design/FOLLOW_UP_TASKS_DESIGN.md).
 *
 * - A create carries an `Idempotency-Key` (the same key for a retry of the same attempt; a new one when the form changes).
 * - An edit, completion or cancellation carries the `rowVersion` the screen was showing; a stale one is 409 `task_changed` and nothing is stored.
 * - Every refusal has a stable `error` code (see `followUpErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request } from "./authApi";

export type FollowUpStatus = "Open" | "Completed" | "Cancelled";
export type FollowUpPriority = "Low" | "Normal" | "High" | "Urgent";
export type FollowUpKind = "Manual" | "Recall" | "UnscheduledTreatment";

export interface FollowUp {
  id: string;
  patientId: string;
  title: string;
  note: string | null;
  dueAtUtc: string;
  status: FollowUpStatus;
  overdue: boolean;
  /** Null for a task nobody owns yet (the system made it and it waits for someone to take it). */
  assigneeUserId: string | null;
  assigneeName: string | null;
  recallIntervalDays: number | null;
  recallOfTaskId: string | null;
  recallTaskId: string | null;
  /** Null for a task the system made. */
  createdByUserId: string | null;
  createdAtUtc: string;
  closedAtUtc: string | null;
  closedByUserId: string | null;
  closureNote: string | null;
  rowVersion: string;
  priority: FollowUpPriority;
  kind: FollowUpKind;
  appointmentId: string | null;
  sourcePlanItemId: string | null;
  closedBySystem: boolean;
}

export interface FollowUpEvent { kind: string; actorUserId: string | null; actorName: string | null; occurredAtUtc: string; detail: string | null }
export interface FollowUpDetail { task: FollowUp; events: FollowUpEvent[] }
export interface FollowUpPage { items: FollowUp[]; count: number; more: boolean }
export interface FollowUpResult { task: FollowUp; replayed: boolean; recallTaskId: string | null }
export interface Assignee { id: string; name: string; role: string }

export interface FollowUpInput { title: string; note: string | null; dueAtUtc: string; assigneeUserId: string; recallIntervalDays: number | null }

export const newFollowUpKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);

export const TITLE_MAX = 120;
export const NOTE_MAX = 500;
export const INTERVAL_MAX = 3650;

async function write<T>(url: string, method: "PUT" | "POST", body: unknown, key?: string): Promise<T> {
  const csrf = await fetchCsrfToken();
  const headers: Record<string, string> = { "Content-Type": "application/json", "X-CSRF-Token": csrf };
  if (key) headers["Idempotency-Key"] = key;
  return request<T>(url, { method, headers, body: JSON.stringify(body) });
}

export type FollowUpFilter = "open" | "closed" | "all";

export const listForPatient = (patientId: string, status: FollowUpFilter, signal?: AbortSignal) =>
  request<FollowUpPage>(`/api/patients/${patientId}/follow-ups?status=${status}`, { signal });
export const listMine = (status: FollowUpFilter, signal?: AbortSignal) => request<FollowUpPage>(`/api/follow-ups/mine?status=${status}`, { signal });
export const getFollowUp = (id: string, signal?: AbortSignal) => request<FollowUpDetail>(`/api/follow-ups/${id}`, { signal });
export const listAssignees = (signal?: AbortSignal) => request<Assignee[]>("/api/follow-ups/assignees", { signal });

export const createFollowUp = (patientId: string, input: FollowUpInput, key: string) => write<FollowUpResult>(`/api/patients/${patientId}/follow-ups`, "POST", input, key);
export const updateFollowUp = (id: string, input: FollowUpInput, rowVersion: string) => write<FollowUpResult>(`/api/follow-ups/${id}`, "PUT", { ...input, rowVersion });
export const completeFollowUp = (id: string, note: string | null, rowVersion: string) => write<FollowUpResult>(`/api/follow-ups/${id}/complete`, "POST", { note, rowVersion });
export const cancelFollowUp = (id: string, note: string, rowVersion: string) => write<FollowUpResult>(`/api/follow-ups/${id}/cancel`, "POST", { note, rowVersion });

const MESSAGES: Record<string, string> = {
  title_invalid: `Write a short title (up to ${TITLE_MAX} characters) on one line.`,
  note_invalid: `The note can be up to ${NOTE_MAX} characters of plain text.`,
  note_required: "Write why the follow-up is being cancelled.",
  due_required: "Choose when the follow-up is due.",
  due_out_of_range: "The due date is too far in the past or in the future. Check the date.",
  interval_invalid: `A recall interval is 1 to ${INTERVAL_MAX} days, or none.`,
  assignee_invalid: "The responsible person must be an enabled account that can see follow-ups.",
  patient_not_found: "That patient was not found.",
  task_not_found: "That follow-up was not found.",
  idempotency_key_reused: "That request was already used for a different follow-up. Start again.",
  row_version_required: "Reload the follow-up and try again.",
  task_changed: "Someone else changed this follow-up. Reload it to see the current version, then try again.",
  task_closed: "That follow-up is already closed.",
  no_change: "Nothing was changed.",
  permission_denied: "Your role cannot do that.",
};

/** A message safe to show for a refused follow-up request. */
export function followUpErrorMessage(err: unknown): string {
  if (err instanceof ApiError) return MESSAGES[err.code] ?? err.message ?? "That did not work. Try again.";
  return "That did not work. Check your connection and try again.";
}

export const isStaleFollowUp = (err: unknown) => err instanceof ApiError && (err.code === "task_changed" || err.code === "task_closed");
