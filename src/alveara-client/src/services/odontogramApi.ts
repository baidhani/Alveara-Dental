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
export const CONDITION_SCOPES = ["Surface", "WholeTooth"] as const;
export const CONDITION_DENTITIONS = ["Permanent", "Primary", "Both"] as const;
export const TOOTH_EFFECTS = ["None", "Absent", "Replacement"] as const;
export const LINK_TYPES = ["Diagnosis", "TreatmentPlan", "Procedure"] as const;
export const LINK_LABELS: Record<string, string> = { Diagnosis: "Diagnosis", TreatmentPlan: "Treatment plan", Procedure: "Procedure" };

/** One kind of finding the practice can record, from the condition catalogue. Retired ones stay listed so findings that used them still read as they did. */
export interface ConditionType {
  id: string;
  code: string;
  label: string;
  scope: "Surface" | "WholeTooth";
  appliesTo: "Permanent" | "Primary" | "Both";
  toothEffect: "None" | "Absent" | "Replacement";
  isActive: boolean;
  createdByName: string | null;
  createdAtUtc: string;
  rowVersion: string;
}

export interface ConditionTypeEvent {
  eventNumber: number;
  changeType: "Created" | "Retired" | "Reactivated";
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export interface FindingLink {
  linkType: string;
  reference: string;
  linkedByName: string | null;
  linkedAtUtc: string;
}

export interface Finding {
  id: string;
  toothKey: string;
  surface: string | null;
  /** The catalogue code; <see cref="conditionLabel"/> is how it reads. */
  condition: string;
  conditionLabel: string;
  conditionScope: "Surface" | "WholeTooth";
  state: FindingState;
  status: "Active" | "Withdrawn";
  recordedByName: string | null;
  recordedAtUtc: string;
  updatedByName: string | null;
  updatedAtUtc: string | null;
  rowVersion: string;
  links: FindingLink[];
}

export interface Chart {
  patientId: string;
  findings: Finding[];
}

export interface FindingVersion {
  versionNumber: number;
  changeType: "Recorded" | "StateChanged" | "Withdrawn" | "Linked";
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

export const CHANGE_LABELS: Record<string, string> = { Recorded: "Recorded", StateChanged: "State changed", Withdrawn: "Withdrawn", Linked: "Linked" };

// ---------- ALV-006-C01: the condition catalogue, a tooth's timeline and links ----------

export interface ToothEvent {
  findingId: string;
  condition: string;
  conditionLabel: string;
  surface: string | null;
  versionNumber: number;
  changeType: "Recorded" | "StateChanged" | "Withdrawn" | "Linked";
  state: string;
  status: string;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export interface ToothHistory {
  patientId: string;
  toothKey: string;
  events: ToothEvent[];
}

export interface ConditionTypeInput {
  code: string;
  label: string;
  scope: string;
  appliesTo: string;
  toothEffect: string;
}

export const getConditionTypes = (signal?: AbortSignal) => request<ConditionType[]>("/api/odontogram/condition-types", { signal });
export const getConditionTypeHistory = (id: string, signal?: AbortSignal) => request<ConditionTypeEvent[]>(`/api/odontogram/condition-types/${id}/history`, { signal });
export const getToothHistory = (patientId: string, toothKey: string, signal?: AbortSignal) => request<ToothHistory>(`/api/patients/${patientId}/odontogram/teeth/${toothKey}/history`, { signal });

/** Each catalogue change returns the whole catalogue as the server now holds it. */
export const createConditionType = (input: ConditionTypeInput) => requestWithCsrf<ConditionType[]>("/api/odontogram/condition-types", "POST", input);
export const retireConditionType = (id: string, reason: string, rowVersion: string) => requestWithCsrf<ConditionType[]>(`/api/odontogram/condition-types/${id}/retire`, "POST", { reason, rowVersion });
export const reactivateConditionType = (id: string, reason: string, rowVersion: string) => requestWithCsrf<ConditionType[]>(`/api/odontogram/condition-types/${id}/reactivate`, "POST", { reason: reason || undefined, rowVersion });
export const linkFinding = (findingId: string, linkType: string, reference: string) => requestWithCsrf<Chart>(`/api/odontogram/findings/${findingId}/links`, "POST", { linkType, reference });
