import { vi } from "vitest";
import { FakeCalendarServer } from "./fakeCalendarServer";
import { json } from "./fakePatientServer";
import { practiceNow } from "../pages/calendar/calendarLayout";
import type { Appointment, PatientFlowState } from "../services/schedulingApi";
import type { AppointmentActions, CheckInReadiness, VisitAction, VisitActionCode, VisitBoard } from "../services/visitsApi";

const ORDER: PatientFlowState[] = ["Scheduled", "Confirmed", "CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut", "Completed"];
// the allowed forward moves (mirrors VisitStateMachine under ALV-N021 A2's default settings: one path, no shortcuts)
const EDGES: Record<PatientFlowState, PatientFlowState[]> = {
  Scheduled: ["Confirmed"], Confirmed: ["CheckedIn"], CheckedIn: ["Ready"], Ready: ["Seated"],
  Seated: ["InTreatment"], InTreatment: ["CheckedOut"], CheckedOut: ["Completed"], Completed: [],
};
const EVENT: Record<string, string> = { Confirmed: "Confirmed", CheckedIn: "CheckedIn", Ready: "Ready", Seated: "Seated", InTreatment: "TreatmentStarted", CheckedOut: "CheckedOut", Completed: "Completed" };
// who makes each move (briefing 5.4, by permission; the fake has no roles, so "own provider" is the chairside permission here)
const CHAIRSIDE = new Set<PatientFlowState>(["Ready", "Seated", "InTreatment", "CheckedOut"]);
const MOVE: Record<string, { code: VisitActionCode; label: string }> = {
  Confirmed: { code: "confirm", label: "Confirm" }, CheckedIn: { code: "check_in", label: "Check in" }, Ready: { code: "mark_ready", label: "Mark ready" },
  Seated: { code: "seat", label: "Seat patient" }, InTreatment: { code: "start_treatment", label: "Start treatment" },
  CheckedOut: { code: "end_treatment", label: "End treatment / ready for checkout" }, Completed: { code: "complete", label: "Complete visit" },
};
const NEXT_ACTOR: Partial<Record<PatientFlowState, string>> = {
  Scheduled: "Waiting for the front desk to confirm.", Confirmed: "Waiting for the front desk to check the patient in.",
  CheckedIn: "Waiting for chairside staff to mark the patient ready.", Ready: "Waiting for chairside staff to seat the patient.",
  Seated: "Waiting for the booked provider to start treatment.", InTreatment: "In treatment; the booked provider ends treatment.",
};
const CHECKOUT_OPEN = "Ready for checkout — the patient has not been marked as left.";
const REASSIGNABLE = new Set<PatientFlowState>(["Scheduled", "Confirmed", "CheckedIn", "Ready", "Seated"]);
const WAITING = new Set<PatientFlowState>(["Scheduled", "Confirmed", "CheckedIn", "Ready"]);
const OPEN = new Set<PatientFlowState>(["CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut"]);
const OCCUPYING = new Set<PatientFlowState>(["Seated", "InTreatment"]);

/**
 * ALV-011-C01: an in-memory stand-in for the visit board API (board, state, assignment) on top of the calendar fake, sharing its appointment records.
 * ALV-N021 A2: one path without shortcuts; each board card carries the caller's `actions` (built from this fake's permissions, with the same codes the server uses);
 * GET appointments/{id}/actions, undo check-in, keep despite blocked time; the assignment moves the BOOKING and needs a reason for a provider change.
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
  /** ALV-N021 A2: visits whose checkout still has open items (Complete is listed disabled and refused 409 checkout_incomplete). */
  checkoutOpen = new Set<string>();

  /** ALV-N021 A2: the signed-in user's actions, as GET appointments/{id}/actions returns them (by this fake's permissions). */
  actionsFor(a: Appointment): AppointmentActions {
    const flow = this.flowOf(a);
    const has = (p: string) => this.permissions.includes(p);
    const flagged = a.blockFlagged ?? false;
    const base = { appointmentId: a.id, rowVersion: a.rowVersion ?? "", status: a.status, flowState: flow, blockFlagged: false, nextActorText: null };
    if (a.status !== "Scheduled" || flow === "Completed") return { ...base, actions: [] };
    const act = (code: VisitActionCode, label: string, targetState: PatientFlowState | null = null, blocked: [string, string] | null = null, needsReason = false): VisitAction =>
      ({ code, label, enabled: blocked === null, blockedBy: blocked?.[0] ?? null, blockedText: blocked?.[1] ?? null, targetState, needsReason });
    const actions: VisitAction[] = [];
    for (const target of EDGES[flow]) {
      if (!has(CHAIRSIDE.has(target) ? "UpdateChairsideFlow" : "UpdateVisitFlow")) continue;
      const blocked: [string, string] | null =
        target === "CheckedIn" && flagged ? ["block_flag_unresolved", "The provider has blocked this time. Reschedule, reassign, cancel or keep the appointment first."]
        : target === "Completed" && this.checkoutOpen.has(a.id) ? ["checkout_incomplete", CHECKOUT_OPEN] : null;
      actions.push(act(MOVE[target].code, MOVE[target].label, target, blocked));
    }
    if ((flow === "CheckedIn" || flow === "Ready") && has("UpdateVisitFlow")) actions.push(act("undo_check_in", "Undo check-in", null, null, true));
    if ((flow === "Scheduled" || flow === "Confirmed") && flagged && has("UpdateVisitFlow")) actions.push(act("keep_despite_block", "Keep despite blocked time", null, null, true));
    if ((flow === "Scheduled" || flow === "Confirmed") && has("ManageAppointments")) {
      actions.push(act("reschedule", "Reschedule"), act("cancel", "Cancel"));
      const early = practiceNow("America/Chicago") < a.startLocal;   // the same "now" the calendar fake's no-show rule uses
      actions.push(act("no_show", "Mark no-show", null, early ? ["no_show_too_early", "An appointment can only be marked as a no-show once its start time has passed."] : null));
    }
    if (REASSIGNABLE.has(flow) && has("ReassignAppointmentProvider")) actions.push(act("reassign_provider", "Change provider", null, null, true));
    if (REASSIGNABLE.has(flow) && (has("ReassignAppointmentProvider") || has("UpdateChairsideFlow"))) actions.push(act("change_room", "Change room"));
    const next = flagged ? "Blocked time covers this appointment: the front desk must reschedule, reassign, cancel or keep it."
      : flow === "CheckedOut" ? (this.checkoutOpen.has(a.id) ? CHECKOUT_OPEN : "Ready for checkout — every item is resolved; the visit can be completed.") : NEXT_ACTOR[flow] ?? null;
    return { ...base, blockFlagged: flagged, nextActorText: next, actions };
  }

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
      visits: [...sort(carried).map((a) => ({ appointment: this.view(a), readiness: cue(a), carriedOver: true, safety: this.indicator(a.patientId), actions: this.actionsFor(a) })),
        ...sort(todays).map((a) => ({ appointment: this.view(a), readiness: cue(a), carriedOver: false, safety: this.indicator(a.patientId), actions: this.actionsFor(a) }))],
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
    // ALV-N021 A2: the per-user actions and "keep despite blocked time" live under /api/appointments
    const own = /^\/api\/appointments\/([^/]+)\/(actions|keep-despite-block)$/.exec(path);
    if (own) {
      const injected = this.takeInjected(method, path);
      if (injected) return injected;
      const a = this.appointments.find((x) => x.id === own[1]);
      if (!a) return json(404, { error: "appointment_not_found", message: "That appointment was not found." });
      if (own[2] === "actions" && method === "GET") return json(200, this.actionsFor(a));
      if (own[2] === "keep-despite-block" && method === "POST") {
        this.scheduleCalls.push({ method, url: u.pathname, headers, body });
        if (!this.permissions.includes("UpdateVisitFlow")) return json(403, { error: "visit_move_forbidden", message: "Keeping an appointment despite blocked time is front-desk work.", reason: "front_office_only" });
        if (!String(body?.reason ?? "").trim()) return json(400, { error: "reason_required", message: "Give a reason." });
        if (body?.rowVersion !== a.rowVersion) return this.conflict(a.id);
        if (!a.blockFlagged) return json(409, { error: "not_flagged", message: "This appointment is not covered by blocked time." });
        a.blockFlagged = false;
        this.touch(a);
        this.log(a, "KeptDespiteBlock", "Kept despite blocked time.");
        return json(200, this.view(a));
      }
    }
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
      if (!this.permissions.includes(need)) return json(403, { error: "visit_move_forbidden", message: "Your role may not make that move.", reason: "not_permitted" });
      const response = this.transition(a, target, body ?? {});
      if (this.dropNextMoveTo === target && response.status < 300) { this.dropNextMoveTo = null; throw new TypeError("Failed to fetch"); }
      return response;
    }
    if (method === "POST" && action === "undo-check-in") {
      if (!this.permissions.includes("UpdateVisitFlow")) return json(403, { error: "visit_move_forbidden", message: "Undoing a check-in is front-desk work.", reason: "front_office_only" });
      if (!String(body?.reason ?? "").trim()) return json(400, { error: "reason_required", message: "Give a reason." });
      if (body?.rowVersion !== a.rowVersion) return this.conflict(a.id);
      const from = this.flowOf(a);
      if (from !== "CheckedIn" && from !== "Ready") return json(409, { error: "undo_check_in_too_late", message: "A check-in can be undone only before the patient is seated." });
      a.flowState = "Confirmed";
      this.touch(a);
      this.log(a, "CheckInUndone", `${from} -> Confirmed`);
      return json(200, this.view(a));
    }
    if (method === "PUT" && action === "assignment") {
      const providerChanges = body?.providerId !== a.providerId;
      const allowed = this.permissions.includes("ReassignAppointmentProvider") || (!providerChanges && this.permissions.includes("UpdateChairsideFlow"));
      if (!allowed) return json(403, { error: "visit_move_forbidden", message: "You do not have permission to change an appointment's provider.", reason: "reassign_not_permitted" });
      if (providerChanges && !String(body?.reason ?? "").trim()) return json(400, { error: "reason_required", message: "Give a reason for changing the provider." });
      return this.assign(a, body ?? {});
    }
    return null;
  }

  private transition(a: Appointment, target: PatientFlowState, body: Record<string, unknown>): Response {
    const from = this.flowOf(a);
    if (a.status === "Scheduled" && from === target) return json(200, this.view(a)); // already there: a quiet no-op
    if (body.rowVersion !== a.rowVersion) return this.conflict(a.id);
    if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: `Patient flow applies only to a scheduled appointment; this one is ${a.status}.` });
    if (!EDGES[from].includes(target)) return json(409, { error: "illegal_transition", message: `A visit cannot go from ${from} to ${target}.` });
    if (target === "CheckedIn" && a.blockFlagged) return json(409, { error: "block_flag_unresolved", message: "The provider has blocked this time. Reschedule, reassign, cancel or keep the appointment before checking the patient in." });
    if (target === "Completed" && this.checkoutOpen.has(a.id)) return json(409, { error: "checkout_incomplete", message: CHECKOUT_OPEN });
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
    const sameProvider = body.providerId === a.providerId;     // ALV-N021 A2: the BOOKING moves (no separate "seen by")
    const sameOperatory = body.operatoryId === a.operatoryId;
    if (sameProvider && sameOperatory) return json(200, this.view(a));
    if (body.rowVersion !== a.rowVersion) return this.conflict(a.id);
    if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: `Only a scheduled appointment can be reassigned; this one is ${a.status}.` });
    if (!REASSIGNABLE.has(this.flowOf(a))) return json(409, { error: "illegal_transition", message: "The provider or room can be changed only until the patient is in treatment." });
    if (!provider) return json(404, { error: "provider_not_found", message: "That provider was not found." });
    if (!operatory) return json(404, { error: "operatory_not_found", message: "That operatory was not found." });
    if (!sameOperatory && OCCUPYING.has(this.flowOf(a))) {
      const holder = this.appointments.find((x) => x.id !== a.id && x.status === "Scheduled" && OCCUPYING.has(this.flowOf(x)) && this.effectiveOperatory(x) === operatory.id);
      if (holder) return json(409, { error: "operatory_occupied", message: "That operatory is occupied by a patient who is seated or in treatment.", conflictingAppointmentId: holder.id });
    }
    a.providerId = provider.providerId; a.providerName = provider.displayName;
    a.operatoryId = operatory.id; a.operatoryName = operatory.name;
    a.visitProviderId = undefined; a.visitProviderName = undefined; a.visitOperatoryId = undefined; a.visitOperatoryName = undefined;
    if (!sameProvider) a.blockFlagged = false;   // a different provider: their blocked time no longer covers it
    this.touch(a);
    this.log(a, sameProvider ? "RoomChanged" : "ProviderReassigned", sameProvider ? "Room changed." : "Provider and room changed.");
    return json(200, this.view(a));
  }
}
