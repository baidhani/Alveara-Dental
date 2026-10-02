import { FakeScheduleServer } from "./fakeScheduleServer";
import { json } from "./fakePatientServer";
import type { Appointment, AppointmentEvent, PatientFlowState } from "../services/schedulingApi";
import { practiceNow } from "../pages/calendar/calendarLayout";

/**
 * ALV-004-C01: an in-memory stand-in for the lifecycle half of the scheduling API (reschedule, cancel, no-show, notes, history, patient overlap and the
 * calendar's all-status list) on top of the booking fake. It models the response SHAPES and the refusals the drawer must explain - not the whole rule set;
 * those are proven by the backend tests and the real-backend walkthrough. Row versions are checked, so a stale change is the shared 409.
 *
 * STORY-011 adds patient flow (check-in, start-treatment, complete): the same response shapes and refusals as the backend (a move out of order is
 * 409 invalid_flow_transition, a repeat is a no-op, and cancel / no-show / reschedule are 409 appointment_in_progress once the patient has checked in).
 */
export class FakeCalendarServer extends FakeScheduleServer {
  private versions = new Map<string, number>();
  private events = new Map<string, AppointmentEvent[]>();
  /** The next lifecycle request of this kind gets a dropped connection AFTER the server applied it. */
  dropNextResponseFor: string | null = null;

  /** An appointment already on the books with a row version and a "Scheduled" history entry. */
  seed(over: Parameters<FakeScheduleServer["addAppointment"]>[0] & { status?: string; notes?: string; cancelReason?: string; flowState?: PatientFlowState }): Appointment {
    const a = this.addAppointment(over);
    a.status = over.status ?? "Scheduled";
    a.flowState = over.flowState ?? "Scheduled";
    a.notes = over.notes ?? null;
    a.cancelReason = over.cancelReason ?? null;
    this.touch(a);
    this.events.set(a.id, [{ eventType: "Scheduled", actorUserId: "u1", occurredAtUtc: "2026-10-02T09:00:00Z", detail: "Scheduled.", previousStartLocal: null, previousProviderName: null, previousOperatoryName: null }]);
    return a;
  }

  private touch(a: Appointment) {
    const v = (this.versions.get(a.id) ?? 0) + 1;
    this.versions.set(a.id, v);
    a.rowVersion = `a${v}`;
  }

  /** Test helper: someone else changes the appointment behind the user's back. */
  changeBehindTheScenes(id: string, patch: Partial<Appointment>) {
    const a = this.appointments.find((x) => x.id === id)!;
    Object.assign(a, patch);
    this.touch(a);
  }

  private log(a: Appointment, eventType: string, detail: string | null, previous?: { start: string; provider: string; operatory: string }) {
    const list = this.events.get(a.id) ?? [];
    list.push({
      eventType, actorUserId: "u1", occurredAtUtc: "2026-10-02T10:00:00Z", detail,
      previousStartLocal: previous?.start ?? null, previousProviderName: previous?.provider ?? null, previousOperatoryName: previous?.operatory ?? null,
    });
    this.events.set(a.id, list);
  }

  private conflict = (id: string) => json(409, { error: "concurrency_conflict", entityType: "Appointment", entityId: id, message: "Changed by someone else." });
  private overlaps = (a: { startLocal: string; endLocal: string }, start: string, end: string) => a.startLocal < end && start < a.endLocal;

  protected override async handleExtra(path: string, method: string, headers: Record<string, string>, body: Record<string, unknown> | null, u: URL): Promise<Response | null> {
    if (path.startsWith("/api/appointments")) {
      const injected = this.takeInjected(method, path);
      if (injected) return injected;
      const parts = path.split("/");
      const id = parts[3];
      const action = parts[4];

      if (method === "GET" && path === "/api/appointments") {
        this.scheduleCalls.push({ method, url: u.pathname + u.search, headers, body });
        const from = u.searchParams.get("from")!;
        const to = u.searchParams.get("to") ?? from;
        const provider = u.searchParams.get("providerId");
        const operatory = u.searchParams.get("operatoryId");
        const all = u.searchParams.get("includeAll") === "true";
        return json(200, this.appointments
          .filter((a) => a.startLocal.slice(0, 10) >= from && a.startLocal.slice(0, 10) <= to && (!provider || a.providerId === provider) && (!operatory || a.operatoryId === operatory) && (all || a.status === "Scheduled"))
          .sort((a, b) => a.startLocal.localeCompare(b.startLocal)));
      }
      if (method === "GET" && id && action === "history") {
        this.scheduleCalls.push({ method, url: u.pathname, headers, body });
        return this.events.has(id) ? json(200, this.events.get(id)) : json(404, { error: "appointment_not_found", message: "That appointment was not found." });
      }

      if (method === "POST" && path === "/api/appointments") {
        // patient overlap (the booking fake knows only provider and operatory)
        const start = String(body?.startLocal);
        const minutes = (body?.durationMinutes as number | null) ?? this.types.find((t) => t.id === body?.appointmentTypeId)?.defaultDurationMinutes ?? 60;
        const end = addMinutes(start, minutes);
        const clash = this.appointments.find((a) => a.status === "Scheduled" && a.patientId === body?.patientId && this.overlaps(a, start, end));
        const earlier = headers["Idempotency-Key"];
        if (clash && !(earlier && this.hasKey(earlier))) {
          this.scheduleCalls.push({ method, url: u.pathname, headers, body });
          return json(409, { error: "patient_double_booked", message: "The patient already has an appointment during that time.", conflictingAppointmentId: clash.id });
        }
        const response = await super.handleExtra(path, method, headers, body, u);
        if (response && response.status === 201) {
          const created = (await response.clone().json()) as Appointment;
          const stored = this.appointments.find((a) => a.id === created.id)!;
          stored.notes = (body?.notes as string | null) ?? null;
          stored.flowState = "Scheduled";
          this.touch(stored);
          this.events.set(stored.id, [{ eventType: "Scheduled", actorUserId: "u1", occurredAtUtc: "2026-10-02T10:00:00Z", detail: `Scheduled for ${stored.durationMinutes} minutes.`, previousStartLocal: null, previousProviderName: null, previousOperatoryName: null }]);
          return json(201, stored);
        }
        return response;
      }

      if (method === "GET" && id && !action) {
        this.scheduleCalls.push({ method, url: u.pathname, headers, body });
        const a = this.appointments.find((x) => x.id === id);
        return a ? json(200, a) : json(404, { error: "appointment_not_found", message: "That appointment was not found." });
      }

      if (id && action && ["reschedule", "cancel", "no-show", "notes", "check-in", "start-treatment", "complete"].includes(action)) {
        this.scheduleCalls.push({ method, url: u.pathname, headers, body });
        const a = this.appointments.find((x) => x.id === id);
        if (!a) return json(404, { error: "appointment_not_found", message: "That appointment was not found." });
        const response = this.lifecycle(a, action, body ?? {});
        if (this.dropNextResponseFor === action && response.status < 300) {
          this.dropNextResponseFor = null;
          throw new TypeError("Failed to fetch"); // it was applied, but the answer never arrived
        }
        return response;
      }
    }
    return super.handleExtra(path, method, headers, body, u);
  }

  private hasKey(key: string) {
    return (this as unknown as { keys: Map<string, string> }).keys.has(key);
  }

  private static readonly FLOW_TARGET: Record<string, PatientFlowState> = { "check-in": "CheckedIn", "start-treatment": "InTreatment", complete: "Completed" };
  private static readonly FLOW_EVENT: Record<string, string> = { CheckedIn: "CheckedIn", InTreatment: "TreatmentStarted", Completed: "Completed" };
  private static readonly FLOW_ORDER: PatientFlowState[] = ["Scheduled", "CheckedIn", "InTreatment", "Completed"];

  /** STORY-011. Mirrors PatientFlowRules: forward only, InTreatment may be skipped, check-in may not. */
  private flow(a: Appointment, action: string, body: Record<string, unknown>): Response {
    const target = FakeCalendarServer.FLOW_TARGET[action];
    const from = a.flowState ?? "Scheduled";
    if (a.status === "Scheduled" && from === target) return json(200, a); // already there: nothing to do, whatever the version
    if (body.rowVersion !== a.rowVersion) return this.conflict(a.id);
    if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: `Patient flow applies only to a scheduled appointment; this one is ${a.status}.` });
    const order = FakeCalendarServer.FLOW_ORDER;
    const step = order.indexOf(target) - order.indexOf(from);
    const allowed = step === 1 || (from === "CheckedIn" && target === "Completed");
    if (!allowed) return json(409, { error: "invalid_flow_transition", message: `A visit cannot go from ${from} to ${target}.` });
    a.flowState = target; a.flowChangedAtUtc = "2026-10-02T10:00:00Z";
    this.touch(a); this.log(a, FakeCalendarServer.FLOW_EVENT[target], `${from} -> ${target}`);
    return json(200, a);
  }

  private lifecycle(a: Appointment, action: string, body: Record<string, unknown>): Response {
    if (action in FakeCalendarServer.FLOW_TARGET) return this.flow(a, action, body);
    const stale = body.rowVersion !== a.rowVersion;
    const arrived = a.status === "Scheduled" && (a.flowState ?? "Scheduled") !== "Scheduled";
    if (arrived && ["cancel", "no-show", "reschedule"].includes(action) && !(stale || (action === "cancel" && !String(body.reason ?? "").trim())))
      return json(409, { error: "appointment_in_progress", message: "This appointment cannot be changed: the patient has already checked in." });
    if (action === "cancel") {
      const reason = String(body.reason ?? "").trim();
      if (!reason) return json(400, { error: "reason_required", message: "A reason for the cancellation is required." });
      if (a.status === "Cancelled") return json(200, a);
      if (stale) return this.conflict(a.id);
      if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: "Only a scheduled appointment can be cancelled." });
      a.status = "Cancelled"; a.cancelReason = reason; a.statusChangedAtUtc = "2026-10-02T10:00:00Z";
      this.touch(a); this.log(a, "Cancelled", reason);
      return json(200, a);
    }
    if (action === "no-show") {
      if (a.status === "NoShow") return json(200, a);
      if (stale) return this.conflict(a.id);
      if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: "Only a scheduled appointment can be marked as a no-show." });
      if (practiceNow("America/Chicago") < a.startLocal) return json(409, { error: "no_show_too_early", message: "An appointment can only be marked as a no-show once its start time has passed." });
      a.status = "NoShow"; a.statusChangedAtUtc = "2026-10-02T10:00:00Z";
      this.touch(a); this.log(a, "NoShow", "Marked as a no-show.");
      return json(200, a);
    }
    if (action === "notes") {
      if (stale) return this.conflict(a.id);
      const notes = String(body.notes ?? "").trim();
      if (notes.length > 1000) return json(400, { error: "invalid_notes", message: "A note can be at most 1000 characters." });
      if ((a.notes ?? "") === notes) return json(200, a);
      a.notes = notes || null;
      this.touch(a); this.log(a, "NotesChanged", notes ? "Note updated." : "Note removed.");
      return json(200, a);
    }
    // reschedule
    if (stale) return this.conflict(a.id);
    if (a.status !== "Scheduled") return json(409, { error: "appointment_not_scheduled", message: `Only a scheduled appointment can be rescheduled; this one is ${a.status}.` });
    const minutes = (body.durationMinutes as number | null) ?? a.durationMinutes;
    if (!Number.isInteger(minutes) || minutes < 5 || minutes > 480 || minutes % 5 !== 0)
      return json(400, { error: "invalid_duration", message: "Duration must be between 5 and 480 minutes, in steps of 5." });
    const start = String(body.startLocal);
    const end = addMinutes(start, minutes);
    if (start.slice(11, 16) < "08:00" || end.slice(11, 16) > "17:00" || end.slice(0, 10) !== start.slice(0, 10))
      return json(409, { error: "provider_unavailable", message: "The provider is not available at that time.", reason: "outside_working_hours" });
    const others = this.appointments.filter((x) => x.id !== a.id && x.status === "Scheduled" && this.overlaps(x, start, end));
    const providerClash = others.find((x) => x.providerId === body.providerId);
    if (providerClash) return json(409, { error: "provider_double_booked", message: "The provider already has an appointment during that time.", conflictingAppointmentId: providerClash.id });
    const operatoryClash = others.find((x) => x.operatoryId === body.operatoryId);
    if (operatoryClash) return json(409, { error: "operatory_conflict", message: "The operatory is already in use during that time.", conflictingAppointmentId: operatoryClash.id });
    const patientClash = others.find((x) => x.patientId === a.patientId);
    if (patientClash) return json(409, { error: "patient_double_booked", message: "The patient already has an appointment during that time.", conflictingAppointmentId: patientClash.id });

    const previous = { start: a.startLocal, provider: a.providerName, operatory: a.operatoryName };
    const provider = this.providers.find((p) => p.providerId === body.providerId)!;
    const operatory = this.operatories.find((o) => o.id === body.operatoryId)!;
    Object.assign(a, { providerId: provider.providerId, providerName: provider.displayName, operatoryId: operatory.id, operatoryName: operatory.name, startLocal: start, endLocal: end, durationMinutes: minutes });
    this.touch(a); this.log(a, "Rescheduled", `Rescheduled for ${minutes} minutes.`, previous);
    return json(200, a);
  }
}

function addMinutes(local: string, minutes: number): string {
  const [date, time] = local.split("T");
  const [h, m] = time.split(":").map(Number);
  const total = h * 60 + m + minutes;
  const day = new Date(`${date}T00:00:00Z`);
  day.setUTCDate(day.getUTCDate() + Math.floor(total / 1440));
  const rest = total % 1440;
  return `${day.toISOString().slice(0, 10)}T${String(Math.floor(rest / 60)).padStart(2, "0")}:${String(rest % 60).padStart(2, "0")}`;
}
