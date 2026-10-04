/**
 * ALV-005-C01: typed client for a patient's longitudinal clinical record (Controllers/ClinicalRecordController): medical and dental history, allergies and medications
 * across encounters, each with a status, who reviewed each section and when, and the full history of every item.
 *
 * - A change to an item echoes that item's `rowVersion`; a stale one comes back as the shared 409 concurrency-conflict shape.
 * - A section is "reviewed", "needs review", "none known", "unknown" or "not reviewed" - five different statements, derived by the server, so no screen ever has to
 *   invent a clinical fact to fill a required field.
 */
import { request, requestWithCsrf } from "./authApi";
import type { EntryInput } from "./clinicalApi";

export type SectionStatus = "Reviewed" | "NeedsReview" | "NoneKnown" | "Unknown" | "NotReviewed";
export type ReviewState = "Reviewed" | "NoneKnown" | "Unknown" | "NotReviewed";
export type ItemStatus = "Active" | "Inactive" | "Resolved" | "Discontinued";

/** The statuses an item of each kind can have (the server and a database check agree). */
export const statusesFor = (kind: string): ItemStatus[] => (kind === "Medication" ? ["Active", "Inactive", "Discontinued"] : ["Active", "Inactive", "Resolved"]);

export interface RecordItem {
  id: string;
  kind: string;
  name: string;
  detail: string | null;
  reaction: string | null;
  severity: string | null;
  dose: string | null;
  frequency: string | null;
  status: ItemStatus;
  createdAtUtc: string;
  createdByName: string | null;
  updatedAtUtc: string | null;
  updatedByName: string | null;
  rowVersion: string;
}

export interface RecordSection {
  kind: string;
  status: SectionStatus;
  items: RecordItem[];
  reviewedAtUtc: string | null;
  reviewedByName: string | null;
}

export interface RecordEvent {
  section: string;
  eventType: string;
  actorName: string | null;
  occurredAtUtc: string;
  encounterId: string | null;
  detail: string | null;
}

export interface ClinicalRecord {
  patientId: string;
  sections: RecordSection[];
  timeline: RecordEvent[];
}

export interface ItemVersion {
  versionNumber: number;
  changeType: "Added" | "Changed" | "StatusChanged" | "RemovedInError";
  name: string;
  detail: string | null;
  reaction: string | null;
  severity: string | null;
  dose: string | null;
  frequency: string | null;
  status: ItemStatus;
  reason: string | null;
  encounterId: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export interface ItemHistory {
  itemId: string;
  kind: string;
  versions: ItemVersion[];
}

export const SECTION_STATUS_WORDS: Record<SectionStatus, string> = {
  Reviewed: "Reviewed", NeedsReview: "Needs review", NoneKnown: "None known", Unknown: "Unknown", NotReviewed: "Not reviewed",
};
export const CHANGE_LABELS: Record<ItemVersion["changeType"], string> = { Added: "Added", Changed: "Details changed", StatusChanged: "Status changed", RemovedInError: "Removed as entered in error" };

export const getRecord = (patientId: string, signal?: AbortSignal) => request<ClinicalRecord>(`/api/patients/${patientId}/clinical-record`, { signal });
export const getItemHistory = (itemId: string, signal?: AbortSignal) => request<ItemHistory>(`/api/clinical-record/items/${itemId}/history`, { signal });

/** Adds an item; adding one that is already listed with the same values returns the record unchanged (a retry is quiet). */
export const addRecordItem = (patientId: string, kind: string, input: EntryInput) => requestWithCsrf<ClinicalRecord>(`/api/patients/${patientId}/clinical-record/items`, "POST", { kind, ...input });
export const updateRecordItem = (itemId: string, input: EntryInput, rowVersion: string, reason?: string) =>
  requestWithCsrf<ClinicalRecord>(`/api/clinical-record/items/${itemId}`, "PUT", { ...input, rowVersion, reason: reason || undefined });
export const setItemStatus = (itemId: string, status: ItemStatus, rowVersion: string, reason?: string) =>
  requestWithCsrf<ClinicalRecord>(`/api/clinical-record/items/${itemId}/status`, "POST", { status, rowVersion, reason: reason || undefined });
export const removeRecordItem = (itemId: string, rowVersion: string, reason: string) => requestWithCsrf<ClinicalRecord>(`/api/clinical-record/items/${itemId}/remove`, "POST", { rowVersion, reason });
export const setSectionReview = (patientId: string, section: string, state: ReviewState) =>
  requestWithCsrf<ClinicalRecord>(`/api/patients/${patientId}/clinical-record/sections/${section}/review`, "PUT", { state });
