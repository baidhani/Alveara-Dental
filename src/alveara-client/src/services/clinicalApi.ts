/**
 * STORY-005: typed client for the clinical documentation API (Controllers/ClinicalController).
 *
 * - Every change echoes the `rowVersion` it read; a stale one comes back as the shared 409 concurrency-conflict shape (see authApi.isConcurrencyConflict).
 * - Starting an encounter and adding an addendum send an `Idempotency-Key`: the caller makes one key when the action begins and reuses it on every retry of that
 *   same action, so a retry after a dropped connection can never create a second encounter or a second addendum (the server replays the first result).
 * - A finalized encounter is never edited; the only addition is an addendum, shown beside the original.
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

/** The four sections of an encounter, in the order they are presented. The server uses the same names. */
export const SECTION_ORDER = ["MedicalHistory", "DentalHistory", "Allergy", "Medication"] as const;
export type SectionKind = (typeof SECTION_ORDER)[number];
export const SECTION_LABELS: Record<string, string> = { MedicalHistory: "Medical history", DentalHistory: "Dental history", Allergy: "Allergies", Medication: "Medications" };
/** What one entry in a section is called, for button names ("Add allergy"). */
export const ENTRY_NOUNS: Record<string, string> = { MedicalHistory: "medical history item", DentalHistory: "dental history item", Allergy: "allergy", Medication: "medication" };
export const SEVERITIES = ["Mild", "Moderate", "Severe"] as const;
export const SECTION_STATUS_LABELS: Record<string, string> = { Recorded: "Recorded", NoneReported: "Reviewed - none reported", Empty: "Not yet addressed" };
export const EVENT_LABELS: Record<string, string> = {
  Created: "Encounter started", EntryAdded: "Entry added", EntryChanged: "Entry changed", EntryRemoved: "Entry removed", SectionMarked: "Section reviewed, none reported",
  SectionUnmarked: "Section review cleared", Finalized: "Encounter finalized", AddendumAdded: "Addendum added",
};

export type EncounterStatus = "Draft" | "Finalized";

export interface EncounterEntry {
  id: string;
  kind: string;
  name: string;
  detail: string | null;
  reaction: string | null;
  severity: string | null;
  dose: string | null;
  frequency: string | null;
  createdAtUtc: string;
  createdByUserId: string | null;
  updatedAtUtc: string | null;
}

export interface EncounterSection {
  kind: string;
  status: "Recorded" | "NoneReported" | "Empty";
  entries: EncounterEntry[];
  reviewedAtUtc: string | null;
  reviewedByUserId: string | null;
}

export interface EncounterAddendum {
  id: string;
  text: string;
  createdAtUtc: string;
  createdByUserId: string | null;
}

export interface EncounterEvent {
  eventType: string;
  actorUserId: string | null;
  occurredAtUtc: string;
  detail: string | null;
}

export interface EncounterDetail {
  id: string;
  patientId: string;
  appointmentId: string | null;
  encounterAtUtc: string;
  status: EncounterStatus;
  isComplete: boolean;
  missingSections: string[];
  sections: EncounterSection[];
  addenda: EncounterAddendum[];
  history: EncounterEvent[];
  createdAtUtc: string;
  createdByUserId: string | null;
  finalizedAtUtc: string | null;
  finalizedByUserId: string | null;
  rowVersion: string;
}

export interface EncounterSummary {
  id: string;
  appointmentId: string | null;
  encounterAtUtc: string;
  status: EncounterStatus;
  isComplete: boolean;
  entryCount: number;
  addendumCount: number;
}

/** The fields a clinician types for one entry; which apply depends on the section (see EntryForm). Empty strings are sent as blank and the server treats them as not given. */
export interface EntryInput {
  name: string;
  detail: string;
  reaction: string;
  severity: string;
  dose: string;
  frequency: string;
}

export const emptyEntry = (): EntryInput => ({ name: "", detail: "", reaction: "", severity: "", dose: "", frequency: "" });

export const entryInputOf = (e: EncounterEntry): EntryInput => ({
  name: e.name, detail: e.detail ?? "", reaction: e.reaction ?? "", severity: e.severity ?? "", dose: e.dose ?? "", frequency: e.frequency ?? "",
});

export const listEncounters = (patientId: string, signal?: AbortSignal) => request<EncounterSummary[]>(`/api/patients/${patientId}/encounters`, { signal });
export const getEncounter = (encounterId: string, signal?: AbortSignal) => request<EncounterDetail>(`/api/encounters/${encounterId}`, { signal });

/** Starts a draft encounter (201) or returns the one already started with this key (200). */
export async function startEncounter(patientId: string, idempotencyKey: string): Promise<EncounterDetail> {
  const csrfToken = await fetchCsrfToken();
  return request<EncounterDetail>(`/api/patients/${patientId}/encounters`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({}),
  });
}

export const addEntry = (encounterId: string, kind: string, input: EntryInput, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/entries`, "POST", { kind, ...input, rowVersion });
export const updateEntry = (encounterId: string, entryId: string, input: EntryInput, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/entries/${entryId}`, "PUT", { ...input, rowVersion });
export const removeEntry = (encounterId: string, entryId: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/entries/${entryId}/remove`, "POST", { rowVersion });
export const markNoneReported = (encounterId: string, kind: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/sections/${kind}/none-reported`, "POST", { rowVersion });
export const clearSectionReview = (encounterId: string, kind: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/sections/${kind}/clear-review`, "POST", { rowVersion });
export const finalizeEncounter = (encounterId: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/finalize`, "POST", { rowVersion });

/** Adds an addendum to a finalized encounter. The same key on a retry returns the first addendum instead of adding another. */
export async function addAddendum(encounterId: string, text: string, idempotencyKey: string): Promise<EncounterDetail> {
  const csrfToken = await fetchCsrfToken();
  return request<EncounterDetail>(`/api/encounters/${encounterId}/addenda`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify({ text }),
  });
}

/** The per-field messages of a 400 `validation_failed` or 409 `documentation_incomplete` response (empty for any other error). */
export function clinicalFieldErrorsOf(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError)) return {};
  const raw = err.body.fieldErrors;
  return raw && typeof raw === "object" ? (raw as Record<string, string>) : {};
}

/** True when a failed request may or may not have reached the server (no HTTP response at all): the UI keeps the same key and the typed values for a retry. */
export const isNetworkFailure = (err: unknown) => !(err instanceof ApiError);

/** A new key for one start/addendum action (reused across its retries). */
export const newKey = () => (typeof crypto !== "undefined" && "randomUUID" in crypto ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);
