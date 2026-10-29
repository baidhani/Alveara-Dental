import { vi } from "vitest";
import type { AppointmentRecord, CancellationsData, FollowUpRecord, FollowUpsData, PatientFlowData, ScheduleData } from "../../services/reportsApi";

/**
 * A small in-memory server for the report screens' tests. It answers like the real API: the permissions, the providers and locations lists, every report with a fixed small practice behind it, and the
 * drill-downs in pages. A test changes `permissions`, `reply` (to answer a call its own way) or the fixtures, and reads `calls` to see exactly what was asked.
 */
export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
export const refusal = (status: number, error: string) => json({ error, message: "Server words." }, status);
export type Call = { method: string; url: string; path: string; params: URLSearchParams };

const counts = (appointments: number, scheduled: number, cancelled: number, noShow: number, bookedMinutes: number) => ({ appointments, scheduled, cancelled, noShow, bookedMinutes });

export const scheduleData = (): ScheduleData => ({
  totals: counts(5, 3, 1, 1, 150), rescheduleEvents: 1,
  days: [{ date: "2030-01-14", counts: counts(2, 1, 1, 0, 60) }, { date: "2030-01-15", counts: counts(2, 1, 0, 1, 30) }, { date: "2030-01-16", counts: counts(1, 1, 0, 0, 60) }, { date: "2030-01-17", counts: counts(0, 0, 0, 0, 0) }],
  providers: [{ providerProfileId: "pb", providerName: "Dr. Patel", counts: counts(1, 1, 0, 0, 60) }, { providerProfileId: "pa", providerName: "Dr. Rivera", counts: counts(4, 2, 1, 1, 90) }],
});

export const flowData = (over: Partial<PatientFlowData> = {}): PatientFlowData => ({
  appointments: 5, cancelled: 1, noShow: 1,
  byState: ["Scheduled", "Confirmed", "CheckedIn", "Ready", "Seated", "InTreatment", "CheckedOut", "Completed"].map((state, i) => ({ state, visits: [1, 0, 1, 0, 0, 1, 0, 0][i] })),
  transitions: [{ event: "Confirmed", count: 1 }, { event: "CheckedIn", count: 2 }, { event: "TreatmentStarted", count: 1 }],
  pastNotCompleted: 0, reconciled: true, ...over,
});

const followCounts = (due: number, completed: number, cancelled: number, closedBySystem: number, openNotYetDue: number, openOverdue: number, completedLate: number, openUnassigned: number) =>
  ({ due, completed, cancelled, closedBySystem, openNotYetDue, openOverdue, completedLate, openUnassigned });

export const followUpsData = (over: Partial<FollowUpsData> = {}): FollowUpsData => ({
  asOfUtc: "2030-01-15T18:00:00+00:00", totals: followCounts(7, 2, 1, 1, 2, 1, 1, 1),
  kinds: [{ kind: "Manual", counts: followCounts(5, 2, 1, 0, 1, 1, 1, 0) }, { kind: "Recall", counts: followCounts(2, 0, 0, 1, 1, 0, 0, 1) }, { kind: "LabCaseDue", counts: followCounts(0, 0, 0, 0, 0, 0, 0, 0) }],
  reconciled: true, ...over,
});

const rates = (booked: number, cancelled: number, noShow: number) => ({ booked, cancelled, noShow, cancellationRate: booked ? Math.round((cancelled / booked) * 10000) / 10000 : null, noShowRate: booked ? Math.round((noShow / booked) * 10000) / 10000 : null });

export const cancellationsData = (over: Partial<CancellationsData> = {}): CancellationsData => ({
  totals: rates(7, 4, 1),
  leadTimes: [{ bucket: "LessThan24h", cancelled: 1 }, { bucket: "Within7Days", cancelled: 1 }, { bucket: "MoreThan7Days", cancelled: 1 }, { bucket: "Unknown", cancelled: 1 }],
  providers: [{ providerProfileId: "pb", providerName: "Dr. Patel", counts: rates(3, 2, 0) }, { providerProfileId: "pa", providerName: "Dr. Rivera", counts: rates(4, 2, 1) }],
  types: [{ appointmentTypeId: "t1", typeName: "Exam", counts: rates(4, 2, 0) }, { appointmentTypeId: "t2", typeName: "Quick check", counts: rates(3, 2, 1) }],
  reconciled: true, ...over,
});

export const appointment = (n: number, over: Partial<AppointmentRecord> = {}): AppointmentRecord => ({
  id: `a${n}`, patientId: `p${n}`, startUtc: `2030-01-14T${String(14 + n).padStart(2, "0")}:00:00+00:00`, durationMinutes: 60, providerProfileId: "pa", operatoryId: "op1", status: "Scheduled", flowState: "Scheduled", ...over,
});

export const task = (n: number, over: Partial<FollowUpRecord> = {}): FollowUpRecord => ({
  id: `t${n}`, patientId: `p${n}`, kind: "Manual", status: "Open", priority: "Normal", dueAtUtc: `2030-01-14T${String(14 + n).padStart(2, "0")}:00:00+00:00`, closedAtUtc: null, assigneeUserId: "u1", closedBySystem: false, ...over,
});

export class FakeReportServer {
  permissions = ["ViewOperationalReports", "ViewPatientRecords"];
  providers = [{ id: "pa", displayName: "Dr. Rivera" }, { id: "pb", displayName: "Dr. Patel" }];
  locations = [{ id: "loc1", name: "Main Office" }];
  noData = false;
  schedule = scheduleData();
  flow = flowData();
  followUps = followUpsData();
  cancellations = cancellationsData();
  /** The records a drill-down returns, in pages of `pageSize`. */
  appointments: AppointmentRecord[] = [appointment(1), appointment(2), appointment(3)];
  tasks: FollowUpRecord[] = [task(1), task(2)];
  pageSize = 2;
  calls: Call[] = [];
  reply: (call: Call) => Response | Promise<Response> | null = () => null;

  install() {
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string) => {
      const u = new URL(String(url), "http://x");
      if (u.pathname === "/api/auth/permissions") return Promise.resolve(json({ username: "manager", role: "OfficeManager", permissions: this.permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
      const call = { method: "GET", url: String(url), path: u.pathname, params: u.searchParams };
      this.calls.push(call);
      return Promise.resolve(this.reply(call) ?? this.answer(call));
    }));
  }

  reports(path: string) { return this.calls.filter((c) => c.path === path); }

  private envelope(c: Call, reportId: string, data: unknown): Response {
    const from = c.params.get("from") ?? "";
    const to = c.params.get("to") ?? "";
    const days = Math.round((Date.parse(to) - Date.parse(from)) / 86400000) + 1;
    return json({ reportId, definitionVersion: 1, filter: { from, to, days, providerIds: c.params.getAll("providerIds"), locationId: c.params.get("locationId"), timeZone: "America/Chicago" }, generatedAtUtc: "2030-01-15T18:00:00+00:00", readConsistency: "Snapshot", recordCount: this.noData ? 0 : 5, noData: this.noData, data });
  }

  private page<R extends { id: string }>(c: Call, rows: R[], cursorOf: (r: R) => Record<string, string>): Response {
    const afterId = c.params.get("afterId");
    const start = afterId ? rows.findIndex((r) => r.id === afterId) + 1 : 0;
    const slice = rows.slice(start, start + this.pageSize);
    const more = start + this.pageSize < rows.length;
    return json({ reportId: "x", filter: {}, total: rows.length, rows: slice, next: more ? cursorOf(slice[slice.length - 1]) : null, asOfUtc: c.params.get("asOf") ?? undefined });
  }

  private answer(c: Call): Response {
    switch (c.path) {
      case "/api/config/providers": return json(this.providers);
      case "/api/config/locations": return json(this.locations);
      case "/api/reports/schedule": return this.envelope(c, "schedule", this.schedule);
      case "/api/reports/patient-flow": return this.envelope(c, "patient-flow", this.flow);
      case "/api/reports/follow-ups": return this.envelope(c, "follow-ups", this.followUps);
      case "/api/reports/cancellations": return this.envelope(c, "cancellations", this.cancellations);
      case "/api/reports/schedule/appointments":
      case "/api/reports/patient-flow/appointments":
      case "/api/reports/cancellations/appointments": return this.page(c, this.appointments, (r) => ({ afterStart: r.startUtc, afterId: r.id }));
      case "/api/reports/follow-ups/tasks": return this.page(c, this.tasks, (r) => ({ afterDueAtUtc: r.dueAtUtc, afterId: r.id }));
      default: return refusal(404, "not_found");
    }
  }
}
