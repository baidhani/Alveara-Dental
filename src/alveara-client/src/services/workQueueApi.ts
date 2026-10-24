/**
 * ALV-009-C01: typed client for the work queue, its quick actions, a patient's follow-up timeline and the recall settings (Controllers/WorkQueueController, docs/FOLLOW_UPS.md).
 *
 * - Every action carries the `rowVersion` the screen was showing; a stale one is 409 `task_changed` and nothing is stored (`isStaleQueueChange`).
 * - Doing what is already done is a quiet replay (`replayed: true`), so a double click is safe.
 * - Every refusal has a stable `error` code (see `queueErrorMessage`).
 */
import { ApiError, request, requestWithCsrf } from "./authApi";
import { followUpErrorMessage } from "./followUpsApi";
import type { FollowUp, FollowUpKind, FollowUpPriority, FollowUpResult, FollowUpStatus } from "./followUpsApi";

export type QueueFilter = "overdue" | "duesoon" | "unassigned" | "mine" | "all";
export type Urgency = "Overdue" | "DueSoon" | "Later" | "Closed";

export interface QueueItem { task: FollowUp; patientName: string; appointmentStartUtc: string | null; urgency: Urgency }
export interface QueueCounts { overdue: number; dueSoon: number; unassigned: number; mine: number; all: number }
export interface QueuePage { items: QueueItem[]; total: number; page: number; pageSize: number; counts: QueueCounts; dueSoonDays: number }

export interface QueueQuery { filter: QueueFilter; priority?: FollowUpPriority | ""; kind?: FollowUpKind | ""; owner?: string; status?: "open" | "closed" | "all"; page?: number; pageSize?: number }

export interface TimelineEntry {
  taskId: string; taskTitle: string; taskKind: FollowUpKind; taskStatus: FollowUpStatus; eventKind: string; actorUserId: string | null; actorName: string | null; occurredAtUtc: string; detail: string | null;
}

export interface RecallSettings { enabled: boolean; defaultIntervalDays: number; leadDays: number; maxVisitAgeMonths: number; dueSoonDays: number; updatedAtUtc: string; updatedByUserId: string | null; rowVersion: string }
export type RecallSettingsInput = Pick<RecallSettings, "enabled" | "defaultIntervalDays" | "leadDays" | "maxVisitAgeMonths" | "dueSoonDays">;

export type RecallVerdict = "Due" | "NotDue" | "OptedOut" | "NoVisit" | "LapsedPatient" | "OpenRecall" | "AlreadyBooked";
export interface PatientRecall {
  patientId: string; hasPlan: boolean; ownIntervalDays: number | null; optedOut: boolean; reason: string | null; effectiveIntervalDays: number;
  lastCompletedVisitUtc: string | null; nextDueUtc: string | null; verdict: RecallVerdict; rowVersion: string | null;
}

export const PRIORITIES: FollowUpPriority[] = ["Urgent", "High", "Normal", "Low"];
export const KINDS: { id: FollowUpKind; label: string }[] = [
  { id: "Manual", label: "Follow-up" }, { id: "Recall", label: "Recall" }, { id: "UnscheduledTreatment", label: "Unscheduled treatment" },
];
export const kindLabel = (kind: FollowUpKind) => KINDS.find((k) => k.id === kind)?.label ?? kind;

export function getWorkQueue(q: QueueQuery, signal?: AbortSignal) {
  const params = new URLSearchParams({ filter: q.filter });
  if (q.priority) params.set("priority", q.priority);
  if (q.kind) params.set("kind", q.kind);
  if (q.owner) params.set("owner", q.owner);
  if (q.status && q.status !== "open") params.set("status", q.status);
  if (q.page && q.page > 1) params.set("page", String(q.page));
  if (q.pageSize) params.set("pageSize", String(q.pageSize));
  return request<QueuePage>(`/api/work-queue?${params}`, { signal });
}

export const assignFollowUp = (id: string, assigneeUserId: string | null, rowVersion: string) => requestWithCsrf<FollowUpResult>(`/api/follow-ups/${id}/assign`, "POST", { assigneeUserId, rowVersion });
export const claimFollowUp = (id: string, rowVersion: string) => requestWithCsrf<FollowUpResult>(`/api/follow-ups/${id}/claim`, "POST", { rowVersion });
export const setFollowUpPriority = (id: string, priority: FollowUpPriority, rowVersion: string) => requestWithCsrf<FollowUpResult>(`/api/follow-ups/${id}/priority`, "POST", { priority, rowVersion });

export const getPatientTimeline = (patientId: string, signal?: AbortSignal) => request<TimelineEntry[]>(`/api/patients/${patientId}/follow-ups/timeline`, { signal });

export const getRecallSettings = (signal?: AbortSignal) => request<RecallSettings>("/api/recall/settings", { signal });
export const updateRecallSettings = (input: RecallSettingsInput, rowVersion: string) => requestWithCsrf<RecallSettings>("/api/recall/settings", "PUT", { ...input, rowVersion });
export const getPatientRecall = (patientId: string, signal?: AbortSignal) => request<PatientRecall>(`/api/patients/${patientId}/recall`, { signal });
export const setPatientRecall = (patientId: string, input: { intervalDays: number | null; optedOut: boolean; reason: string | null }, rowVersion: string | null) =>
  requestWithCsrf<PatientRecall>(`/api/patients/${patientId}/recall`, "PUT", { ...input, rowVersion });
export const resetPatientRecall = (patientId: string, rowVersion: string | null) => requestWithCsrf<PatientRecall>(`/api/patients/${patientId}/recall/reset`, "POST", { rowVersion });

const MESSAGES: Record<string, string> = {
  filter_invalid: "That filter is not one of overdue, due soon, unassigned, mine or all.",
  priority_invalid: "The priority is Low, Normal, High or Urgent.",
  kind_invalid: "That kind of follow-up is not known.",
  owner_invalid: "That owner is not valid.",
  status_invalid: "The status filter is open, closed or all.",
  appointment_invalid: "Choose an appointment of this patient.",
  settings_invalid: "A recall setting is outside its limits. Check the numbers.",
  settings_changed: "Someone else changed the recall settings. Reload them to see the current values, then try again.",
  plan_changed: "Someone else changed this patient's recall arrangement. Reload it, then try again.",
  plan_empty: "Choose an interval, or opt the patient out, or reset the patient to the practice default.",
  reason_invalid: "The reason is up to 200 characters of plain text.",
  row_version_required: "Reload and try again.",
};

/** A message safe to show for a refused queue, timeline or recall request. */
export function queueErrorMessage(err: unknown): string {
  if (err instanceof ApiError && MESSAGES[err.code]) return MESSAGES[err.code];
  return followUpErrorMessage(err);
}

export const isStaleQueueChange = (err: unknown) => err instanceof ApiError && ["task_changed", "task_closed", "settings_changed", "plan_changed", "row_version_required"].includes(err.code);
