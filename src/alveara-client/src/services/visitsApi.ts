/**
 * ALV-011-C01: typed client for the live visit board and the visit's state chain (Controllers/VisitsController). An appointment's id is its visit's id.
 * Every change carries the row version the caller last read; a stale one is the shared 409 concurrency conflict.
 */
import { request, requestWithCsrf } from "./authApi";
import type { Appointment, PatientFlowState } from "./schedulingApi";

export type ReadinessStatus = "Complete" | "InProgress" | "SignedEarlierVersion" | "Missing";

export interface ReadinessItem {
  templateId: string;
  templateKey: string;
  title: string;
  category: string;
  status: ReadinessStatus;
  currentVersionNumber: number;
  signedVersionNumber: number | null;
}

/** Which forms the practice requires at check-in and how many are really complete. `requiredCount` 0 means nothing is required (not that something was completed). */
export interface CheckInReadiness {
  ready: boolean;
  requiredCount: number;
  completeCount: number;
  items: ReadinessItem[];
}

export interface VisitCard {
  appointment: Appointment;
  /** Present only while the patient has not yet been seen, and only for callers allowed to see form status. */
  readiness: CheckInReadiness | null;
  /** A visit from an earlier day that is still open (it still holds its room). */
  carriedOver: boolean;
}

export interface VisitBoard {
  date: string;
  /** The server's clock, so elapsed-time figures never depend on the browser's clock. */
  serverNowUtc: string;
  states: PatientFlowState[];
  visits: VisitCard[];
}

export const getVisitBoard = (date?: string, signal?: AbortSignal) =>
  request<VisitBoard>(`/api/visits/board${date ? `?date=${encodeURIComponent(date)}` : ""}`, { signal });

export const moveVisit = (id: string, target: PatientFlowState, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/visits/${id}/state`, "POST", { target, rowVersion });

export const assignVisit = (id: string, providerId: string, operatoryId: string, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/visits/${id}/assignment`, "PUT", { providerId, operatoryId, rowVersion });
