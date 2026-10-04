/**
 * ALV-005-C01: typed client for an encounter's notes, vital signs, note template and signing (Controllers/ClinicalNotesController).
 *
 * - Every change echoes the encounter `rowVersion` it read; a stale one comes back as the shared 409 concurrency-conflict shape.
 * - Recording vitals sends an `Idempotency-Key`: the caller makes one key when the action begins and reuses it on every retry of that same reading, so a dropped
 *   connection can never record it twice (the server replays the first result).
 * - Note templates are owned by the clinical-documentation domain; configuring them needs a separate permission (ManageClinicalTemplates).
 */
import { fetchCsrfToken, request, requestWithCsrf } from "./authApi";
import type { EncounterDetail } from "./clinicalApi";

/** The note sections, in the order the server presents them (SOAP, then progress and treatment notes). */
export const NOTE_SECTIONS = ["Subjective", "Objective", "Assessment", "Plan", "Progress", "Treatment"] as const;
export type NoteSection = (typeof NOTE_SECTIONS)[number];
export const NOTE_LABELS: Record<string, string> = {
  Subjective: "Subjective", Objective: "Objective", Assessment: "Assessment", Plan: "Plan", Progress: "Progress note", Treatment: "Treatment note",
};
/** What an addendum can say it amends, with the words shown to the clinician. */
export const AMENDABLE_LABELS: Record<string, string> = {
  MedicalHistory: "Medical history", DentalHistory: "Dental history", Allergy: "Allergies", Medication: "Medications", ...NOTE_LABELS, Vitals: "Vital signs",
};

export const saveNote = (encounterId: string, section: string, body: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/notes/${section}`, "PUT", { body, rowVersion });
export const applyTemplate = (encounterId: string, templateId: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/template`, "POST", { templateId, rowVersion });
export const signEncounter = (encounterId: string, rowVersion: string) => requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/sign`, "POST", { rowVersion });
export const unsignEncounter = (encounterId: string, rowVersion: string) => requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/unsign`, "POST", { rowVersion });

/** What the vitals form holds: every value as typed (blank = not measured). Converted to numbers on send. */
export interface VitalsInput {
  measuredAt: string;
  systolicMmHg: string;
  diastolicMmHg: string;
  pulseBpm: string;
  respirationsPerMinute: string;
  temperatureC: string;
  oxygenSaturationPercent: string;
  weightKg: string;
  heightCm: string;
  note: string;
}

export const emptyVitals = (): VitalsInput => ({
  measuredAt: "", systolicMmHg: "", diastolicMmHg: "", pulseBpm: "", respirationsPerMinute: "", temperatureC: "", oxygenSaturationPercent: "", weightKg: "", heightCm: "", note: "",
});

const NUMERIC = ["systolicMmHg", "diastolicMmHg", "pulseBpm", "respirationsPerMinute", "temperatureC", "oxygenSaturationPercent", "weightKg", "heightCm"] as const;

/** The JSON for a vitals request. A blank value is left out; a value that is not a number is sent as NaN-free text the server will refuse, never silently dropped. */
export function vitalsPayload(input: VitalsInput, rowVersion: string): Record<string, unknown> {
  const body: Record<string, unknown> = { rowVersion };
  for (const key of NUMERIC) {
    const raw = input[key].trim();
    if (raw !== "") body[key] = Number(raw);
  }
  if (input.note.trim() !== "") body.note = input.note.trim();
  if (input.measuredAt.trim() !== "") body.measuredAtUtc = new Date(input.measuredAt).toISOString();
  return body;
}

export async function recordVitals(encounterId: string, input: VitalsInput, rowVersion: string, idempotencyKey: string): Promise<EncounterDetail> {
  const csrfToken = await fetchCsrfToken();
  return request<EncounterDetail>(`/api/encounters/${encounterId}/vitals`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(vitalsPayload(input, rowVersion)),
  });
}

export const voidVitals = (encounterId: string, vitalsId: string, reason: string, rowVersion: string) =>
  requestWithCsrf<EncounterDetail>(`/api/encounters/${encounterId}/vitals/${vitalsId}/void`, "POST", { reason, rowVersion });

// ---------- note templates ----------

export interface TemplateSection {
  section: string;
  required: boolean;
  starterText: string | null;
}

export interface NoteTemplate {
  id: string;
  name: string;
  description: string | null;
  isActive: boolean;
  sections: TemplateSection[];
  updatedAtUtc: string | null;
  updatedByName: string | null;
  rowVersion: string;
}

export interface TemplateInput {
  name: string;
  description: string;
  sections: { section: string; required: boolean; starterText: string }[];
}

export const listTemplates = (includeInactive = false, signal?: AbortSignal) =>
  request<NoteTemplate[]>(`/api/clinical/templates${includeInactive ? "?includeInactive=true" : ""}`, { signal });
export const createTemplate = (input: TemplateInput) => requestWithCsrf<NoteTemplate>("/api/clinical/templates", "POST", input);
export const updateTemplate = (id: string, input: TemplateInput, rowVersion: string) => requestWithCsrf<NoteTemplate>(`/api/clinical/templates/${id}`, "PUT", { ...input, rowVersion });
export const setTemplateActive = (id: string, active: boolean, rowVersion: string) => requestWithCsrf<NoteTemplate>(`/api/clinical/templates/${id}/active`, "POST", { active, rowVersion });
