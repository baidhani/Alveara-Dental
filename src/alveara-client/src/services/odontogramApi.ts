/**
 * STORY-006: typed client for the interactive odontogram (Controllers/OdontogramController).
 *
 * - A tooth is always its FDI key ("16", "48", "55"); how it is NUMBERED on screen is a display matter (see pages/odontogram/toothNumbering.ts) and never travels to the server.
 * - The chart holds ACTIVE findings only. An empty list means nothing is recorded, never that the mouth is healthy. A withdrawn finding is kept in the finding's history.
 * - A change echoes the `rowVersion` it read (a stale one is the shared 409 concurrency conflict).
 */
import { request, requestWithCsrf } from "./authApi";

export const FINDING_STATES = ["Existing", "Diagnosed", "Planned", "Completed"] as const;
export type FindingState = (typeof FINDING_STATES)[number];
export const CONDITIONS = ["Caries", "Restoration", "Crown", "Missing", "Implant", "RootCanal"] as const;
export type Condition = (typeof CONDITIONS)[number];
export const CONDITION_LABELS: Record<string, string> = {
  Caries: "Caries", Restoration: "Restoration", Crown: "Crown", Missing: "Missing tooth", Implant: "Implant", RootCanal: "Root canal",
};
/** Conditions recorded on one surface; every other condition is about the whole tooth. */
export const SURFACE_CONDITIONS: ReadonlySet<string> = new Set(["Caries", "Restoration"]);

export interface Finding {
  id: string;
  toothKey: string;
  surface: string | null;
  condition: Condition;
  state: FindingState;
  status: "Active" | "Withdrawn";
  recordedByName: string | null;
  recordedAtUtc: string;
  updatedByName: string | null;
  updatedAtUtc: string | null;
  rowVersion: string;
}

export interface Chart {
  patientId: string;
  findings: Finding[];
}

export interface FindingVersion {
  versionNumber: number;
  changeType: "Recorded" | "StateChanged" | "Withdrawn";
  toothKey: string;
  surface: string | null;
  condition: string;
  state: string;
  status: string;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export const getChart = (patientId: string, signal?: AbortSignal) => request<Chart>(`/api/patients/${patientId}/odontogram`, { signal });
export const getFindingHistory = (findingId: string, signal?: AbortSignal) =>
  request<{ findingId: string; versions: FindingVersion[] }>(`/api/odontogram/findings/${findingId}/history`, { signal });

export interface RecordFindingInput {
  toothKey: string;
  surface: string | null;
  condition: string;
  state: string;
}

/** Each write returns the patient's whole chart as the server now holds it, so the screen replaces what it shows instead of patching it. */
export const recordFinding = (patientId: string, input: RecordFindingInput) => requestWithCsrf<Chart>(`/api/patients/${patientId}/odontogram/findings`, "POST", input);
export const changeFindingState = (findingId: string, state: string, rowVersion: string) => requestWithCsrf<Chart>(`/api/odontogram/findings/${findingId}/state`, "POST", { state, rowVersion });
export const withdrawFinding = (findingId: string, reason: string, rowVersion: string) => requestWithCsrf<Chart>(`/api/odontogram/findings/${findingId}/withdraw`, "POST", { reason, rowVersion });

export const CHANGE_LABELS: Record<string, string> = { Recorded: "Recorded", StateChanged: "State changed", Withdrawn: "Withdrawn" };
