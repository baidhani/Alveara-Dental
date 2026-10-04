import { vi } from "vitest";
import { FakeCalendarServer } from "./fakeCalendarServer";
import { json } from "./fakePatientServer";
import type { Appointment, PatientFlowState } from "../services/schedulingApi";
import type { CheckInReadiness, VisitBoard } from "../services/visitsApi";

const ORDER: PatientFlowState[] = ["Scheduled", "Confirmed", "CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut", "Completed"];
// the allowed forward moves (mirrors VisitStateMachine: the chain plus STORY-011's three shortcuts)
const EDGES: Record<PatientFlowState, PatientFlowState[]> = {
  Scheduled: ["Confirmed", "CheckedIn"], Confirmed: ["CheckedIn"], CheckedIn: ["Ready", "InTreatment", "Completed"], Ready: ["Seated"],
  Seated: ["InTreatment"], InTreatment: ["CheckedOut", "Completed"], CheckedOut: ["Completed"], Completed: [],
};
const EVENT: Record<string, string> = { Confirmed: "Confirmed", CheckedIn: "CheckedIn", Ready: "Ready", Seated: "Seated", InTreatment: "TreatmentStarted", CheckedOut: "CheckedOut", Completed: "Completed" };
const CHAIRSIDE = new Set<PatientFlowState>(["Ready", "Seated", "InTreatment", "Completed"]);
const WAITING = new Set<PatientFlowState>(["Scheduled", "Confirmed", "CheckedIn", "Ready"]);
const OPEN = new Set<PatientFlowState>(["CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut"]);
const OCCUPYING = new Set<PatientFlowState>(["Seated", "InTreatment"]);

/**
 * ALV-011-C01: an in-memory stand-in for the visit board API (board, state, assignment) on top of the calendar fake, sharing its appointment records.
 * It models the response SHAPES and the refusals the board must explain - the state chain, who may make which move (403), the stale version (shared 409),
 * a room that already holds a seated patient, a finished visit - not the whole rule set; those are proven by the backend tests and the real-backend
 * walkthrough. `serverNow` is the board's clock; `readiness` is the check-in form cue per patient.
 */
export class FakeFlowServer extends FakeCalendarServer {
  serverNow = "2030-01-14T15:00:00.000Z";
  today = "2030-01-14";
  readiness: Record<string, CheckInReadiness> = {};
  /** ALV-N011: the minimal safety indicator per patient; only carried for a caller who holds ViewSafetyIndicator, and only when there is something to show. */
  safety: Record<string, { alert: boolean; clearance: boolean }> = {};
  boardReads = 0;
  /** The next N board reads fail with a 500 (the last good board must stay on screen). */
  failBoardReads = 0;
  /** Board reads never answer (until the request is aborted) - for the timeout. */
  hangBoard = false;
  /** The next move of this kind (e.g. "Seated") is applied but its answer never arrives, like a dropped connection. */
  dropNextMoveTo: PatientFlowState | null = null;

  override install() {
    super.install();
    const inner = globalThis.fetch;
    vi.stubGlobal("fetch", vi.fn((url: string, init?: RequestInit) => {
      if (this.hangBoard && String(url).includes("/api/visits/board"))
        return new Promise<Response>((_, reject) => init?.signal?.addEventListener("abort", () => reject(new DOMException("The operation was aborted.", "AbortError"))));
      return inner(url, init);
    }));
  }

  private flowOf = (a: Appointment): PatientFlowState => a.flowState ?? "Scheduled";
  private effectiveOperatory = (a: Appointment) => a.visitOperatoryId ?? a.operatoryId;

  /** An appointment as the server's view shows it: the visit-time provider and operatory (default: as booked) and the moves that may come next. */
  view(a: Appointment): Appointment {
    const flow = this.flowOf(a);
    const utc = (local: string) => new Date(`${local}:00-06:00`).toISOString(); // the practice is on Central standard time in January
    return {
      ...a, flowState: flow, startUtc: a.startUtc || utc(a.startLocal), endUtc: a.endUtc || utc(a.endLocal),
      visitProviderId: a.visitProviderId ?? a.providerId, visitProviderName: a.visitProviderName ?? a.providerName,
      visitOperatoryId: a.visitOperatoryId ?? a.operatoryId, visitOperatoryName: a.visitOperatoryName ?? a.operatoryName,
      nextFlowStates: a.status === "Scheduled" ? EDGES[flow] : [],
    };
  }

  private indicator(patientId: string) { return this.permissions.includes("ViewSafetyIndicator") ? this.safety[patientId] ?? null : null; }

  private board(date: string): VisitBoard {
    const dayOf = (a: Appointment) => a.startLocal.slice(0, 10);
    const carried = date === this.today ? this.appointments.filter((a) => dayOf(a) < date && a.status === "Scheduled" && OPEN.has(this.flowOf(a))) : [];
    const todays = this.appointments.filter((a) => dayOf(a) === date);
    const sort = (list: Appointment[]) => [...list].sort((x, y) => x.startLocal.localeCompare(y.startLocal));
    const cue = (a: Appointment) => (a.status === "Scheduled" && WAITING.has(this.flowOf(a)) ? this.readiness[a.patientId] ?? { ready: true, requiredCount: 0, completeCount: 0, items: [] } : null);
    return {
      date, serverNowUtc: this.serverNow, states: ORDER,
      visits: [...sort(carried).map((a) => ({ appointment: this.view(a), readiness: cue(a), carriedOver: true, safety: this.indicator(a.patientId) })),
        ...sort(todays).map((a) => ({ appointment: this.view(a), readiness: cue(a), carriedOver: false, safety: this.indicator(a.patientId) }))],
    };
  }

  /** Test helper: someone else moves a visit behind the user's back (the version changes). */
  moveBehindTheScenes(id: string, flowState: PatientFlowState) {
    const a = this.appointments.find((x) => x.id === id)!;
    a.flowState = flowState;
    a.flowChangedAtUtc = this.serverNow;
    this.touch(a);
  }

  protected override async handleExtra(path: string, method: string, headers: Record<string, string>, body: Record<string, unknown> | null, u: URL): Promise<Response | null> {
    if (!path.startsWith("/api/visits")) return super.handleExtra(path, method, headers, body, u);
    const injected = this.takeInjected(method, path);
    if (injected) return injected;
    const parts = path.split("/");

    if (method === "GET" && parts[3] === "board") {
      this.boardReads++;
      if (this.failBoardReads > 0) { this.failBoardReads--; return json(500, { error: "boom" }); }
      const date = u.searchParams.get("date") ?? this.today;
      return json(200, this.board(date));
    }

    const id = parts[3];
    const action = parts[4];
    this.scheduleCalls.push({ method, url: u.pathname, headers, body });
    const a = this.appointments.find((x) => x.id === id);
    if (!a) return json(404, { error: "appointment_not_found", message: "That appointment was not found." });

    if (method === "POST" && action === "state") {
      const target = body?.target as PatientFlowState;
      const need = CHAIRSIDE.has(target) ? "UpdateChairsideFlow" : "UpdateVisitFlow";
      if (!this.permissions.includes(need)) return json(403, { error: "permission_denied", required: need });
      const response = this.transition(a, target, body ?? {});
      if (this.dropNextMoveTo === target && response.status < 300) { this.dropNextMoveTo = null; throw new TypeError("Failed to fetch"); }
      return response;
    }
    if (method === "PUT" && action === "assignment") {
      if (!this.permissions.includes("UpdateVisitFlow") && !this.permissions.includes("UpdateChairsideFlow"))
        return json(403, { error: "permission_denied", required: "UpdateVisitFlow or UpdateChairsideFlow" });
      return this.assign(a, body ?? {});
    }
    return null;
  }

  private transition(a: Appointment, target: PatientFlowState, body: Record<string, unknown>): Response {
    const from = this.flowOf(a);
    if (a.status === "Scheduled" && from === target) return json(200, this.view(a)); // already there: a quiet no-op
    if (body.rowVersion !== a.rowVersion) return this.conflict(a.id);
    if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: `Patient flow applies only to a scheduled appointment; this one is ${a.status}.` });
    if (!EDGES[from].includes(target)) return json(409, { error: "invalid_flow_transition", message: `A visit cannot go from ${from} to ${target}.` });
    if (OCCUPYING.has(target) && !OCCUPYING.has(from)) {
      const holder = this.appointments.find((x) => x.id !== a.id && x.status === "Scheduled" && OCCUPYING.has(this.flowOf(x)) && this.effectiveOperatory(x) === this.effectiveOperatory(a));
      if (holder) return json(409, { error: "operatory_occupied", message: "That operatory is occupied by a patient who is seated or in treatment.", conflictingAppointmentId: holder.id });
    }
    a.flowState = target;
    a.flowChangedAtUtc = this.serverNow;
    this.touch(a);
    this.log(a, EVENT[target], `${from} -> ${target}`);
    return json(200, this.view(a));
  }

  private assign(a: Appointment, body: Record<string, unknown>): Response {
    const provider = this.providers.find((p) => p.providerId === body.providerId);
    const operatory = this.operatories.find((o) => o.id === body.operatoryId);
    const sameProvider = body.providerId === (a.visitProviderId ?? a.providerId);
    const sameOperatory = body.operatoryId === this.effectiveOperatory(a);
    if (sameProvider && sameOperatory) return json(200, this.view(a));
    if (body.rowVersion !== a.rowVersion) return this.conflict(a.id);
    if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: `Only a scheduled appointment can be reassigned; this one is ${a.status}.` });
    if (this.flowOf(a) === "Completed") return json(409, { error: "visit_completed", message: "A completed visit can no longer be reassigned." });
    if (!provider) return json(404, { error: "provider_not_found", message: "That provider was not found." });
    if (!operatory) return json(404, { error: "operatory_not_found", message: "That operatory was not found." });
    if (!sameOperatory && OCCUPYING.has(this.flowOf(a))) {
      const holder = this.appointments.find((x) => x.id !== a.id && x.status === "Scheduled" && OCCUPYING.has(this.flowOf(x)) && this.effectiveOperatory(x) === operatory.id);
      if (holder) return json(409, { error: "operatory_occupied", message: "That operatory is occupied by a patient who is seated or in treatment.", conflictingAppointmentId: holder.id });
    }
    a.visitProviderId = provider.providerId; a.visitProviderName = provider.displayName;
    a.visitOperatoryId = operatory.id; a.visitOperatoryName = operatory.name;
    this.touch(a);
    this.log(a, "AssignmentChanged", "Provider and operatory changed.");
    return json(200, this.view(a));
  }
}
