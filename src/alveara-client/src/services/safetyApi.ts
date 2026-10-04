/**
 * ALV-N011: typed client for the patient-safety context, alerts and clearances (Controllers/SafetyController).
 *
 * - The context is a READ-ONLY projection: allergies and medications are read live from the clinical record, alerts are what a clinician explicitly stated (with their source), and
 *   what is not established is a `gap` - an empty list never means "none".
 * - Acknowledging an alert records that the signed-in person SAW a revision of it and never resolves it: an alert is resolved only by an explicit resolve with a reason.
 * - A change echoes the `rowVersion` it read (a stale one is the shared 409 concurrency conflict); acknowledging echoes the `revision` the person saw.
 */
import { request, requestWithCsrf } from "./authApi";

export const SEVERITIES = ["Critical", "High", "Moderate", "Low"] as const;
export type SafetySeverity = (typeof SEVERITIES)[number];
export const ALERT_CATEGORIES = ["Condition", "Pregnancy", "Anticoagulant", "AdverseReaction", "Custom"] as const;
export const CATEGORY_LABELS: Record<string, string> = {
  Allergy: "Allergy", Medication: "Current medication", Condition: "Significant condition", Pregnancy: "Pregnancy", Anticoagulant: "Anticoagulant", AdverseReaction: "Adverse reaction", Custom: "Custom alert",
};
export const CLEARANCE_KINDS = ["Medical", "Dental"] as const;

export interface SafetyEntry {
  origin: "ClinicalRecord" | "Alert";
  id: string;
  category: string;
  title: string;
  detail: string | null;
  severity: SafetySeverity | null;
  status: "Active" | "Resolved";
  source: string;
  sourceItemId: string | null;
  lastUpdatedAtUtc: string;
  lastUpdatedByName: string | null;
  needsAttention: boolean;
  attentionReason: string | null;
  acknowledgedByMe: boolean;
  acknowledgedAtUtc: string | null;
  revision: number | null;
  rowVersion: string | null;
  resolvedAtUtc: string | null;
  resolvedByName: string | null;
  resolutionReason: string | null;
}

export interface Clearance {
  id: string;
  kind: string;
  reason: string;
  requestedFrom: string | null;
  status: "Requested" | "Received" | "Resolved" | "Cancelled";
  documentReference: string | null;
  /** True once the clearance was received (or resolved) but no supporting document has been attached yet. */
  documentPending: boolean;
  requestedAtUtc: string;
  requestedByName: string | null;
  receivedAtUtc: string | null;
  receivedByName: string | null;
  receivedNote: string | null;
  closedAtUtc: string | null;
  closedByName: string | null;
  closingReason: string | null;
  updatedAtUtc: string | null;
  rowVersion: string;
}

export interface SafetyGap {
  section: string;
  status: string;
  message: string;
}

export interface SafetySummary {
  patientId: string;
  asOfUtc: string;
  activeAlertCount: number;
  activeAllergyCount: number;
  currentMedicationCount: number;
  highestSeverity: SafetySeverity | null;
  openClearanceCount: number;
  unacknowledgedAlertCount: number;
  needsAttentionCount: number;
  gaps: SafetyGap[];
}

export interface SafetyContext {
  patientId: string;
  asOfUtc: string;
  summary: SafetySummary;
  entries: SafetyEntry[];
  resolved: SafetyEntry[];
  clearances: Clearance[];
  gaps: SafetyGap[];
}

/** The minimal indicator the shared live board may carry: two booleans, nothing else. */
export interface SafetyIndicator {
  alert: boolean;
  clearance: boolean;
}

export interface AlertVersion {
  versionNumber: number;
  changeType: "Created" | "Changed" | "Resolved" | "Reopened";
  category: string;
  title: string;
  detail: string | null;
  severity: string;
  sourceNote: string;
  status: string;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export interface ClearanceVersion {
  versionNumber: number;
  changeType: "Requested" | "Received" | "DocumentAttached" | "Resolved" | "Cancelled";
  kind: string;
  reason: string;
  requestedFrom: string | null;
  status: string;
  documentReference: string | null;
  note: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export interface AlertInput {
  category: string;
  title: string;
  detail: string;
  severity: string;
  sourceNote: string;
}

export const CHANGE_LABELS: Record<string, string> = {
  Created: "Created", Changed: "Changed", Resolved: "Resolved", Reopened: "Reopened", Requested: "Requested", Received: "Received", DocumentAttached: "Document attached", Cancelled: "Cancelled",
};

export const getSafety = (patientId: string, signal?: AbortSignal) => request<SafetyContext>(`/api/patients/${patientId}/safety`, { signal });
export const getSafetySummary = (patientId: string, signal?: AbortSignal) => request<SafetySummary>(`/api/patients/${patientId}/safety/summary`, { signal });
export const getAlertHistory = (alertId: string, signal?: AbortSignal) => request<{ alertId: string; versions: AlertVersion[] }>(`/api/safety/alerts/${alertId}/history`, { signal });
export const getClearanceHistory = (id: string, signal?: AbortSignal) => request<{ clearanceId: string; versions: ClearanceVersion[] }>(`/api/safety/clearances/${id}/history`, { signal });

export const createAlert = (patientId: string, input: AlertInput) => requestWithCsrf<SafetyContext>(`/api/patients/${patientId}/safety/alerts`, "POST", input);
export const updateAlert = (alertId: string, input: Omit<AlertInput, "category">, rowVersion: string, reason?: string) =>
  requestWithCsrf<SafetyContext>(`/api/safety/alerts/${alertId}`, "PUT", { ...input, rowVersion, reason: reason || undefined });
export const resolveAlert = (alertId: string, reason: string, rowVersion: string) => requestWithCsrf<SafetyContext>(`/api/safety/alerts/${alertId}/resolve`, "POST", { reason, rowVersion });
export const reopenAlert = (alertId: string, reason: string, rowVersion: string) => requestWithCsrf<SafetyContext>(`/api/safety/alerts/${alertId}/reopen`, "POST", { reason, rowVersion });
export const acknowledgeAlert = (alertId: string, revision: number) => requestWithCsrf<SafetyContext>(`/api/safety/alerts/${alertId}/acknowledge`, "POST", { revision });

export const requestClearance = (patientId: string, kind: string, reason: string, requestedFrom: string) =>
  requestWithCsrf<SafetyContext>(`/api/patients/${patientId}/safety/clearances`, "POST", { kind, reason, requestedFrom });
export const receiveClearance = (id: string, note: string, documentReference: string, rowVersion: string) =>
  requestWithCsrf<SafetyContext>(`/api/safety/clearances/${id}/receive`, "POST", { note, documentReference, rowVersion });
export const attachClearanceDocument = (id: string, documentReference: string, rowVersion: string) =>
  requestWithCsrf<SafetyContext>(`/api/safety/clearances/${id}/document`, "POST", { documentReference, rowVersion });
export const resolveClearance = (id: string, reason: string, rowVersion: string) => requestWithCsrf<SafetyContext>(`/api/safety/clearances/${id}/resolve`, "POST", { reason, rowVersion });
export const cancelClearance = (id: string, reason: string, rowVersion: string) => requestWithCsrf<SafetyContext>(`/api/safety/clearances/${id}/cancel`, "POST", { reason, rowVersion });

/** Tells every mounted safety strip (the patient header, an open encounter) to re-read, after a change in the safety panel. */
export const SAFETY_CHANGED_EVENT = "alveara:safety-changed";
export const announceSafetyChanged = (patientId: string) => window.dispatchEvent(new CustomEvent(SAFETY_CHANGED_EVENT, { detail: { patientId } }));
