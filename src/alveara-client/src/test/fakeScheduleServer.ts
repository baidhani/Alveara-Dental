import { FakePatientServer, json } from "./fakePatientServer";
import type { Call } from "./fakePatientServer";
import type { Appointment } from "../services/schedulingApi";

/**
 * STORY-004: an in-memory stand-in for the scheduling API on top of the fake patient server. It models the response SHAPES (same as
 * Controllers/AppointmentsController) and the refusals the UI must explain (provider double-booked, operatory in use, provider unavailable,
 * invalid duration), the idempotent retry (the same key returns the first appointment), and `dropNextBookResponse`, which stores the booking and
 * then fails the response like a dropped connection. The real rules are proven by the backend tests and the real-backend walkthrough; this exists
 * so the UI's handling of each outcome can be tested in isolation.
 */
export class FakeScheduleServer extends FakePatientServer {
  appointments: Appointment[] = [];
  scheduleCalls: Call[] = [];
  dropNextBookResponse = false;
  private keys = new Map<string, string>();
  private n = 0;
  private injectedSchedule = new Map<string, Response[]>();

  readonly providers = [
    { providerId: "prov-a", displayName: "Dr. Rivera", specialty: "General", weeklyAvailability: [] },
    { providerId: "prov-b", displayName: "Dr. Patel", specialty: "General", weeklyAvailability: [] },
  ];
  readonly operatories = [{ id: "op-1", name: "Op 1" }, { id: "op-2", name: "Op 2" }];
  readonly types = [{ id: "type-60", name: "Exam", defaultDurationMinutes: 60 }, { id: "type-30", name: "Quick check", defaultDurationMinutes: 30 }];

  constructor() {
    super();
    this.permissions = ["ViewPatientRecords", "RegisterPatients", "EditPatients", "ViewSchedule", "ManageAppointments"];
  }

  failSchedule(methodAndPath: string, response: Response) {
    this.injectedSchedule.set(methodAndPath, [...(this.injectedSchedule.get(methodAndPath) ?? []), response]);
  }

  /** The next canned response for this request, if a test queued one (used by this fake and by fakes built on it). */
  protected takeInjected(method: string, path: string): Response | null {
    for (const [key, queue] of this.injectedSchedule) {
      const [m, prefix] = key.split(" ");
      if (m === method && path.startsWith(prefix) && queue.length > 0) return queue.shift()!;
    }
    return null;
  }

  callsToSchedule(method: string, pathPrefix: string) {
    return this.scheduleCalls.filter((c) => c.method === method && new URL(c.url, "http://x").pathname.startsWith(pathPrefix));
  }

  /** Test helper: an appointment already on the books. */
  addAppointment(over: { id?: string; provider?: string; operatory?: string; start: string; minutes?: number; patientName?: string }): Appointment {
    const provider = this.providers.find((p) => p.providerId === (over.provider ?? "prov-a"))!;
    const operatory = this.operatories.find((o) => o.id === (over.operatory ?? "op-1"))!;
    const minutes = over.minutes ?? 60;
    const a: Appointment = {
      id: over.id ?? `appt-${++this.n}`, patientId: "p-x", patientName: over.patientName ?? "Pat Existing", providerId: provider.providerId, providerName: provider.displayName,
      operatoryId: operatory.id, operatoryName: operatory.name, appointmentTypeId: "type-60", appointmentTypeName: "Exam", startUtc: "", endUtc: "",
      startLocal: over.start, endLocal: addMinutes(over.start, minutes), durationMinutes: minutes, status: "Scheduled", createdAtUtc: "2026-10-02T09:00:00Z",
    };
    this.appointments.push(a);
    return a;
  }

  protected override async handleExtra(path: string, method: string, headers: Record<string, string>, body: Record<string, unknown> | null, u: URL): Promise<Response | null> {
    if (path === "/api/config/scheduling") {
      return json(200, { timeZoneId: "America/Chicago", activeLocationId: "loc", activeLocationName: "Main Office", providers: this.providers, operatories: this.operatories, appointmentTypes: this.types });
    }
    if (!path.startsWith("/api/appointments")) return null;
    this.scheduleCalls.push({ method, url: u.pathname + u.search, headers, body });
    const injected = this.takeInjected(method, path);
    if (injected) return injected;

    if (method === "GET" && path === "/api/appointments") {
      const from = u.searchParams.get("from")!;
      const to = u.searchParams.get("to") ?? from;
      const provider = u.searchParams.get("providerId");
      return json(200, this.appointments
        .filter((a) => a.startLocal.slice(0, 10) >= from && a.startLocal.slice(0, 10) <= to && (!provider || a.providerId === provider))
        .sort((a, b) => a.startLocal.localeCompare(b.startLocal)));
    }
    const id = path.split("/")[3];
    if (method === "GET" && id) {
      const a = this.appointments.find((x) => x.id === id);
      return a ? json(200, a) : json(404, { error: "appointment_not_found", message: "That appointment was not found." });
    }
    if (method === "POST") return this.book(headers, body!);
    return null;
  }

  private book(headers: Record<string, string>, body: Record<string, unknown>): Response {
    const key = headers["Idempotency-Key"];
    if (!key || key.length < 8) return json(400, { error: "idempotency_key_required", message: "Scheduling needs an Idempotency-Key." });
    const earlier = this.keys.get(key);
    if (earlier) return json(200, this.appointments.find((a) => a.id === earlier));

    const type = this.types.find((t) => t.id === body.appointmentTypeId)!;
    const minutes = (body.durationMinutes as number | null) ?? type.defaultDurationMinutes;
    if (!Number.isInteger(minutes) || minutes < 5 || minutes > 480 || minutes % 5 !== 0)
      return json(400, { error: "invalid_duration", message: "Duration must be between 5 and 480 minutes, in steps of 5." });
    const start = String(body.startLocal);
    const end = addMinutes(start, minutes);
    const provider = this.providers.find((p) => p.providerId === body.providerId)!;
    const operatory = this.operatories.find((o) => o.id === body.operatoryId)!;

    if (start.slice(11, 16) < "08:00" || end.slice(0, 10) !== start.slice(0, 10) || end.slice(11, 16) > "17:00")
      return json(409, { error: "provider_unavailable", message: "The provider is not available at that time.", reason: "outside_working_hours" });
    const overlaps = (a: Appointment) => a.startLocal < end && start < a.endLocal;
    const providerClash = this.appointments.find((a) => a.providerId === provider.providerId && overlaps(a));
    if (providerClash) return json(409, { error: "provider_double_booked", message: "The provider already has an appointment during that time.", conflictingAppointmentId: providerClash.id });
    const operatoryClash = this.appointments.find((a) => a.operatoryId === operatory.id && overlaps(a));
    if (operatoryClash) return json(409, { error: "operatory_conflict", message: "The operatory is already in use during that time.", conflictingAppointmentId: operatoryClash.id });

    const patient = this.patients.get(String(body.patientId))!;
    const a: Appointment = {
      id: `appt-${++this.n}`, patientId: patient.id, patientName: `${patient.firstName} ${patient.lastName}`, providerId: provider.providerId, providerName: provider.displayName,
      operatoryId: operatory.id, operatoryName: operatory.name, appointmentTypeId: type.id, appointmentTypeName: type.name, startUtc: "", endUtc: "",
      startLocal: start, endLocal: end, durationMinutes: minutes, status: "Scheduled", createdAtUtc: "2026-10-02T09:00:00Z",
    };
    this.appointments.push(a);
    this.keys.set(key, a.id);
    if (this.dropNextBookResponse) {
      this.dropNextBookResponse = false;
      throw new TypeError("Failed to fetch"); // the booking was stored, but the response never arrived
    }
    return json(201, a);
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
