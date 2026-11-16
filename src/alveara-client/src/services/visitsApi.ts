/**
 * ALV-011-C01: typed client for the live visit board and the visit's state chain (Controllers/VisitsController). An appointment's id is its visit's id.
 * Every change carries the row version the caller last read; a stale one is the shared 409 concurrency conflict.
 */
import type { SafetyIndicator } from "./safetyApi";
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
  /**
   * ALV-N011: the MINIMAL patient-safety indicator - whether the patient has something active and whether a clearance is open, nothing else. Present only for callers who hold
   * the board-indicator permission and only when there is something to show; absent means nothing is on file (never "unknown").
   */
  safety?: SafetyIndicator | null;
  /** ALV-N021 A2: what the signed-in user may do to this visit now (same as GET appointments/{id}/actions). The board draws its buttons from this only. */
  actions?: AppointmentActions | null;
}

/** ALV-N021 A2: stable action codes from the server (VisitActionCodes). */
export type VisitActionCode =
  | "confirm" | "check_in" | "undo_check_in" | "mark_ready" | "seat" | "start_treatment" | "end_treatment" | "complete"
  | "reschedule" | "cancel" | "no_show" | "reassign_provider" | "change_room" | "keep_despite_block"
  | "rebook" | "open_patient";   // ALV-N021 A2: offered on a cancelled or no-show appointment to the people who book

/**
 * One action the user may take now. `enabled` false: the user holds the authority but a rule blocks it at the moment; `blockedBy` is the server's error code and
 * `blockedText` says why in words. Actions the user has no authority for are not listed at all. `targetState` is set for moves along the visit path.
 */
export interface VisitAction {
  code: VisitActionCode;
  label: string;
  enabled: boolean;
  blockedBy: string | null;
  blockedText: string | null;
  targetState: PatientFlowState | null;
  needsReason: boolean;
}

export interface AppointmentActions {
  appointmentId: string;
  rowVersion: string;
  status: string;
  flowState: PatientFlowState;
  blockFlagged: boolean;
  /** Who the visit waits for, in words (roles, never the patient); announced through the live region after a change. */
  nextActorText: string | null;
  actions: VisitAction[];
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

/** ALV-N021 A2 (OD-13 to OD-16): changes the BOOKED provider and/or room; a provider change needs a reason. */
export const assignVisit = (id: string, providerId: string, operatoryId: string, rowVersion: string, reason?: string) =>
  requestWithCsrf<Appointment>(`/api/visits/${id}/assignment`, "PUT", { providerId, operatoryId, rowVersion, ...(reason ? { reason } : {}) });

export const getAppointmentActions = (id: string, signal?: AbortSignal) => request<AppointmentActions>(`/api/appointments/${id}/actions`, { signal });

/** ALV-N021 A2 (OD-11): back to the state before check-in; reason required. */
export const undoCheckIn = (id: string, reason: string, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/visits/${id}/undo-check-in`, "POST", { reason, rowVersion });

/** ALV-N021 A2 (OD-18): keep an appointment that blocked time covers; reason required. */
export const keepDespiteBlock = (id: string, reason: string, rowVersion: string) =>
  requestWithCsrf<Appointment>(`/api/appointments/${id}/keep-despite-block`, "POST", { reason, rowVersion });
