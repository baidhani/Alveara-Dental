import { vi } from "vitest";
import type {
  AppointmentRecord, BalanceAccountRecord, BalancesData, Baseline, CancellationsData, CollectionsData, CompletedProceduresData, ComparisonData, FollowUpRecord, FollowUpsData, LedgerEntryRecord, LedgerProof, MeasurementData, MetricComparison, MetricSide, MetricValue, PatientFlowData,
  PlanItemRecord, PlanStatusData, ProcedureRecord, ProductionData, ProviderActivityData, ScheduleData,
} from "../../services/reportsApi";

/**
 * A small in-memory server for the report screens' tests. It answers like the real API: the permissions, the providers and locations lists, every report with a fixed small practice behind it, and the
 * drill-downs in pages. A test changes `permissions`, `reply` (to answer a call its own way) or the fixtures, and reads `calls` to see exactly what was asked.
 */
export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
export const refusal = (status: number, error: string) => json({ error, message: "Server words." }, status);
export type Call = { method: string; url: string; path: string; params: URLSearchParams; headers: Headers; body: Record<string, unknown> | undefined };

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

export const ledgerProof = (over: Partial<LedgerProof> = {}): LedgerProof => ({
  lines: [["Charges", 3, 300], ["ChargeReversals", 1, -100], ["Payments", 2, -190], ["PaymentReversals", 1, 40], ["Refunds", 1, 20], ["RefundReversals", 0, 0], ["Adjustments", 1, 10], ["AdjustmentReversals", 0, 0], ["OpeningBalances", 1, 75], ["Unclassified", 0, 0]]
    .map(([line, entries, amount]) => ({ line: line as string, entries: entries as number, amount: amount as number })),
  linesTotal: 155, linesEntries: 10, ledgerTotal: 155, ledgerEntries: 10, reconciled: true, scope: "WholeLedger", ...over,
});

export const productionData = (over: Partial<ProductionData> = {}): ProductionData => ({
  chargeEntries: 3, grossCharges: 300, chargesReversed: 100, netProduction: 200,
  providers: [{ providerProfileId: "pa", providerName: "Dr. One", charges: 2, gross: 200, reversed: 100, net: 100 }, { providerProfileId: "pb", providerName: "Dr. Two", charges: 1, gross: 100, reversed: 0, net: 100 }],
  codes: [{ procedureCode: "LOCAL-500", charges: 1, gross: 100, reversed: 0, net: 100 }, { procedureCode: "LOCAL-501", charges: 1, gross: 100, reversed: 0, net: 100 }, { procedureCode: "LOCAL-502", charges: 1, gross: 100, reversed: 100, net: 0 }],
  ledger: ledgerProof(), ...over,
});

export const collectionsData = (over: Partial<CollectionsData> = {}): CollectionsData => ({
  paymentEntries: 2, paymentsReceived: 190, paymentsReversed: 40, refunded: 20, netCollections: 130,
  methods: [{ method: "Card", payments: 1, received: 40, reversed: 40, refunded: 0, net: 0 }, { method: "Cash", payments: 1, received: 150, reversed: 0, refunded: 20, net: 130 }],
  ledger: ledgerProof(), ...over,
});

export const balancesData = (over: Partial<BalancesData> = {}): BalancesData => ({
  atStart: { accounts: 2, accountsOwing: 2, totalOwed: 160, accountsInCredit: 0, totalCredit: 0, netBalance: 160 },
  atEnd: { accounts: 3, accountsOwing: 2, totalOwed: 330, accountsInCredit: 0, totalCredit: 0, netBalance: 330 },
  change: 170, ledgerChange: 170, ledgerEntries: 4, reconciled: true, ...over,
});

export const planStatusData = (over: Partial<PlanStatusData> = {}): PlanStatusData => ({
  plans: 3, plansProposed: 2, plansWithdrawn: 1, items: 7, reconciled: true,
  cells: [["Pending", 1, 100], ["Accepted", 4, 400], ["Declined", 1, 100], ["Withdrawn", 1, 100], ["AcceptedScheduled", 1, 100], ["AcceptedUnscheduled", 3, 300], ["AcceptedCompleted", 2, 200]]
    .map(([cell, items, estimatedFee]) => ({ cell: cell as string, items: items as number, estimatedFee: estimatedFee as number })), ...over,
});

export const proceduresData = (over: Partial<CompletedProceduresData> = {}): CompletedProceduresData => ({
  procedures: 2, estimatedFee: 200, reconciled: true,
  days: [{ date: "2030-01-14", procedures: 0 }, { date: "2030-01-15", procedures: 2 }, { date: "2030-01-16", procedures: 0 }],
  providers: [{ providerProfileId: "pa", providerName: "Dr. One", procedures: 1, estimatedFee: 100 }, { providerProfileId: "pb", providerName: "Dr. Two", procedures: 1, estimatedFee: 100 }],
  codes: [{ codeSystem: "LOCAL", code: "LOCAL-500", description: "Procedure 1", procedures: 1, estimatedFee: 100 }, { codeSystem: "LOCAL", code: "LOCAL-501", description: "Procedure 2", procedures: 1, estimatedFee: 100 }], ...over,
});

export const activityData = (over: Partial<ProviderActivityData> = {}): ProviderActivityData => ({
  providers: [
    { providerProfileId: "pb", providerName: "Dr. Patel", booked: 2, scheduled: 1, cancelled: 0, noShow: 1, visitsCompleted: 1, proceduresCompleted: 1 },
    { providerProfileId: "pa", providerName: "Dr. Rivera", booked: 3, scheduled: 2, cancelled: 1, noShow: 0, visitsCompleted: 1, proceduresCompleted: 2 },
  ],
  totals: { booked: 5, scheduled: 3, cancelled: 1, noShow: 1, visitsCompleted: 2, proceduresCompleted: 3 }, reconciled: true, ...over,
});

export const appointment = (n: number, over: Partial<AppointmentRecord> = {}): AppointmentRecord => ({
  id: `a${n}`, patientId: `p${n}`, startUtc: `2030-01-14T${String(14 + n).padStart(2, "0")}:00:00+00:00`, durationMinutes: 60, providerProfileId: "pa", operatoryId: "op1", status: "Scheduled", flowState: "Scheduled", ...over,
});

export const task = (n: number, over: Partial<FollowUpRecord> = {}): FollowUpRecord => ({
  id: `t${n}`, patientId: `p${n}`, kind: "Manual", status: "Open", priority: "Normal", dueAtUtc: `2030-01-14T${String(14 + n).padStart(2, "0")}:00:00+00:00`, closedAtUtc: null, assigneeUserId: "u1", closedBySystem: false, ...over,
});

export const ledgerEntry = (n: number, over: Partial<LedgerEntryRecord> = {}): LedgerEntryRecord => ({
  id: `l${n}`, patientId: `p${n}`, entryNumber: n, kind: "Charge", line: "Charges", amount: 100, recordedAtUtc: `2030-01-14T${String(14 + n).padStart(2, "0")}:00:00+00:00`, paymentMethod: null, procedureCode: "LOCAL-500", ...over,
});

export const planItem = (n: number, over: Partial<PlanItemRecord> = {}): PlanItemRecord => ({
  itemId: `i${n}`, planId: "plan1", patientId: `p${n}`, itemNumber: n, estimatedFee: 100, acceptance: "Accepted", scheduling: "Unscheduled", completion: "NotStarted", withdrawn: false, ...over,
});

export const procedureRecord = (n: number, over: Partial<ProcedureRecord> = {}): ProcedureRecord => ({
  id: `c${n}`, patientId: `p${n}`, planItemId: `i${n}`, providerProfileId: "pa", performedAtUtc: `2030-01-15T${String(14 + n).padStart(2, "0")}:00:00+00:00`, codeSystem: "LOCAL", code: "LOCAL-500", estimatedFee: 100, ...over,
});


const metric = (key: string, name: string, num: number, den: number): MetricValue => ({
  key, name, definitionVersion: 1, numerator: `things counted for ${name}`, denominator: `all things for ${name}`, source: "the records", lowerIsBetter: true, supportingRoute: null,
  numeratorCount: num, denominatorCount: den, rate: den === 0 ? null : Math.round((num / den) * 10000) / 10000,
});

export const NOT_MEASURABLE = [
  { key: "duplicate_entry_time", name: "Time spent entering the same information twice", reason: "The application cannot tell that two entries were the same information or how long either took." },
  { key: "administrative_time", name: "Time staff spend on nonclinical administrative work", reason: "The application does not record how long a task takes or who was waiting while it was done." },
  { key: "nonclinical_overhead_reduction", name: "Reduction in nonclinical overhead", reason: "This is a product target (a 20 to 30 percent reduction), not a measurement. It becomes a result only if a valid baseline and comparison of a measurable time metric exist." },
];

export const NO_CAUSATION = "A difference between two periods shows that the numbers differ. It does not show what caused the difference.";

export const measurementData = (over: Partial<MeasurementData> = {}): MeasurementData => ({
  asOfUtc: "2030-02-01T00:00:00+00:00",
  metrics: [
    metric("scheduling_conflicts", "Scheduling conflicts", 4, 12), metric("billing_corrections", "Billing corrections", 1, 3), metric("duplicate_merges", "Duplicate patient merges", 1, 7),
    metric("unfinalized_notes", "Clinical notes not finalized", 2, 3), metric("late_follow_ups", "Follow-ups not done on time", 2, 4), metric("cancellation_rate", "Cancellations", 2, 5), metric("no_show_rate", "No-shows", 0, 0),
  ],
  notMeasurable: NOT_MEASURABLE, note: NO_CAUSATION, ...over,
});

export const baselineRecord = (n: number, over: Partial<Baseline> = {}): Baseline => ({
  id: `b${n}`, name: `Spring ${n}`, from: "2025-03-01", to: "2025-03-31", timeZoneId: "America/Chicago", asOfUtc: "2025-04-02T12:00:00+00:00", setAtUtc: "2025-04-02T12:00:00+00:00", setByUserId: "u1",
  metrics: [{ metricKey: "scheduling_conflicts", definitionVersion: 1, numeratorCount: 2, denominatorCount: 10, rate: 0.2 }], ...over,
});

const side = (num: number, den: number, version = 1): MetricSide => ({ definitionVersion: version, numeratorCount: num, denominatorCount: den, rate: den === 0 ? null : Math.round((num / den) * 10000) / 10000 });

const comparable = (key: string, name: string, base: MetricSide, now: MetricSide): MetricComparison => {
  const absolute = Math.round((now.rate! - base.rate!) * 10000) / 10000;
  return { key, name, lowerIsBetter: true, status: "comparable", message: "The two periods can be compared.", baseline: base, current: now, absoluteChange: absolute, relativeChange: base.rate === 0 ? null : Math.round((absolute / base.rate!) * 10000) / 10000, movement: absolute < 0 ? "Lower" : absolute > 0 ? "Higher" : "Same" };
};

const refused = (key: string, name: string, status: string, message: string, base: MetricSide | null, now: MetricSide): MetricComparison =>
  ({ key, name, lowerIsBetter: true, status, message, baseline: base, current: now, absoluteChange: null, relativeChange: null, movement: null });

export const comparisonData = (over: Partial<ComparisonData> = {}): ComparisonData => ({
  baseline: baselineRecord(1), minimumSample: 30, note: NO_CAUSATION, notMeasurable: NOT_MEASURABLE,
  comparisons: [
    comparable("scheduling_conflicts", "Scheduling conflicts", side(2, 40), side(4, 40)),
    comparable("late_follow_ups", "Follow-ups not done on time", side(1, 4), side(3, 4)),
    refused("billing_corrections", "Billing corrections", "baseline_sample_too_small", "The baseline has too few records to compare with.", side(0, 0), side(1, 3)),
    refused("duplicate_merges", "Duplicate patient merges", "definition_changed", "The metric is defined differently now than when the baseline was taken, so the two numbers do not measure the same thing.", side(1, 40, 2), side(1, 7)),
  ],
  ...over,
});

export const account = (n: number, balance: number): BalanceAccountRecord => ({ accountId: `acc${n}`, patientId: `p${n}`, balance });

export class FakeReportServer {
  permissions = ["ViewOperationalReports", "ViewPatientRecords"];
  providers = [{ id: "pa", displayName: "Dr. Rivera" }, { id: "pb", displayName: "Dr. Patel" }];
  locations = [{ id: "loc1", name: "Main Office" }];
  noData = false;
  schedule = scheduleData();
  flow = flowData();
  followUps = followUpsData();
  cancellations = cancellationsData();
  production = productionData();
  collections = collectionsData();
  balances = balancesData();
  plans = planStatusData();
  procedures = proceduresData();
  activity = activityData();
  measurement = measurementData();
  baselines: Baseline[] = [];
  comparison = comparisonData();
  /** The baselines saved so far by idempotency key, so a repeat of the same save returns the same baseline. */
  private saved = new Map<string, Baseline>();
  /** The records a drill-down returns, in pages of `pageSize`. */
  appointments: AppointmentRecord[] = [appointment(1), appointment(2), appointment(3)];
  tasks: FollowUpRecord[] = [task(1), task(2)];
  ledgerEntries: LedgerEntryRecord[] = [ledgerEntry(1), ledgerEntry(2)];
  accounts: BalanceAccountRecord[] = [account(1, 200), account(2, 130)];
  planItems: PlanItemRecord[] = [planItem(1), planItem(2)];
  procedureRecords: ProcedureRecord[] = [procedureRecord(1), procedureRecord(2)];
  pageSize = 2;
  calls: Call[] = [];
  reply: (call: Call) => Response | Promise<Response> | null = () => null;

  install() {
    vi.stubGlobal("fetch", vi.fn().mockImplementation((url: string, init?: RequestInit) => {
      const u = new URL(String(url), "http://x");
      if (u.pathname === "/api/auth/permissions") return Promise.resolve(json({ username: "manager", role: "OfficeManager", permissions: this.permissions, sessionExpiresAtUtc: new Date(Date.now() + 1800000).toISOString() }));
      if (u.pathname === "/api/auth/csrf-token") return Promise.resolve(json({ token: "t" }));
      const call: Call = { method: init?.method ?? "GET", url: String(url), path: u.pathname, params: u.searchParams, headers: new Headers(init?.headers), body: init?.body ? JSON.parse(String(init.body)) : undefined };
      this.calls.push(call);
      return Promise.resolve(this.reply(call) ?? this.answer(call));
    }));
  }

  reports(path: string) { return this.calls.filter((c) => c.path === path && c.method === "GET"); }
  posts(path: string) { return this.calls.filter((c) => c.path === path && c.method === "POST"); }

  private envelope(c: Call, reportId: string, data: unknown): Response {
    const from = c.params.get("from") ?? "";
    const to = c.params.get("to") ?? "";
    const days = Math.round((Date.parse(to) - Date.parse(from)) / 86400000) + 1;
    return json({ reportId, definitionVersion: 1, filter: { from, to, days, providerIds: c.params.getAll("providerIds"), locationId: c.params.get("locationId"), timeZone: "America/Chicago" }, generatedAtUtc: "2030-01-15T18:00:00+00:00", readConsistency: "Snapshot", recordCount: this.noData ? 0 : 5, noData: this.noData, data });
  }

  /** One page of `rows`, after the row whose key is in the `idParam` parameter; the cursor is whatever `cursorOf` makes of the last row. */
  private page<R>(c: Call, rows: R[], keyOf: (r: R) => string, cursorOf: (r: R) => Record<string, string | number | null>, idParam = "afterId", extra: Record<string, unknown> = {}): Response {
    const after = c.params.get(idParam);
    const start = after ? rows.findIndex((r) => keyOf(r) === after) + 1 : 0;
    const slice = rows.slice(start, start + this.pageSize);
    const more = start + this.pageSize < rows.length;
    return json({ reportId: "x", filter: {}, total: rows.length, rows: slice, next: more ? cursorOf(slice[slice.length - 1]) : null, asOfUtc: c.params.get("asOf") ?? undefined, ...extra });
  }

  private saveBaseline(c: Call): Response {
    const key = c.headers.get("Idempotency-Key") ?? "";
    const repeat = this.saved.get(key);
    if (repeat) return json(repeat, 200);
    const body = c.body as { name: string; from: string; to: string };
    const made = baselineRecord(this.baselines.length + 10, { id: `made${this.baselines.length + 1}`, name: body.name, from: body.from, to: body.to });
    this.saved.set(key, made);
    this.baselines = [made, ...this.baselines];
    return json(made, 201);
  }

  private answer(c: Call): Response {
    switch (c.path) {
      case "/api/config/providers": return json(this.providers);
      case "/api/config/locations": return json(this.locations);
      case "/api/reports/schedule": return this.envelope(c, "schedule", this.schedule);
      case "/api/reports/patient-flow": return this.envelope(c, "patient-flow", this.flow);
      case "/api/reports/follow-ups": return this.envelope(c, "follow-ups", this.followUps);
      case "/api/reports/cancellations": return this.envelope(c, "cancellations", this.cancellations);
      case "/api/reports/production": return this.envelope(c, "production", this.production);
      case "/api/reports/collections": return this.envelope(c, "collections", this.collections);
      case "/api/reports/balances": return this.envelope(c, "balances", this.balances);
      case "/api/reports/treatment-plans": return this.envelope(c, "treatment-plans", this.plans);
      case "/api/reports/completed-procedures": return this.envelope(c, "completed-procedures", this.procedures);
      case "/api/reports/provider-activity": return this.envelope(c, "provider-activity", this.activity);
      case "/api/reports/measurement/metrics": return this.envelope(c, "measurement", this.measurement);
      case "/api/reports/measurement/comparison": return this.envelope(c, "measurement-comparison", { ...this.comparison, baseline: this.baselines.find((b) => b.id === c.params.get("baselineId")) ?? this.comparison.baseline });
      case "/api/reports/measurement/baselines": return c.method === "POST" ? this.saveBaseline(c) : json(this.baselines);
      case "/api/reports/schedule/appointments":
      case "/api/reports/patient-flow/appointments":
      case "/api/reports/cancellations/appointments":
      case "/api/reports/provider-activity/appointments": return this.page(c, this.appointments, (r) => r.id, (r) => ({ afterStart: r.startUtc, afterId: r.id }));
      case "/api/reports/follow-ups/tasks": return this.page(c, this.tasks, (r) => r.id, (r) => ({ afterDueAtUtc: r.dueAtUtc, afterId: r.id }));
      case "/api/reports/production/entries":
      case "/api/reports/collections/entries": return this.page(c, this.ledgerEntries, (r) => r.id, (r) => ({ afterRecordedAtUtc: r.recordedAtUtc, afterId: r.id }), "afterId", { totalAmount: this.ledgerEntries.reduce((s, r) => s + r.amount, 0) });
      case "/api/reports/balances/accounts": return this.page(c, this.accounts, (r) => r.accountId, (r) => ({ afterBalance: r.balance, afterAccountId: r.accountId }), "afterAccountId", { side: c.params.get("side") ?? "Owing", totalAmount: this.accounts.reduce((s, r) => s + r.balance, 0) });
      case "/api/reports/treatment-plans/items": return this.page(c, this.planItems, (r) => r.itemId, (r) => ({ afterTime: null, afterId: r.itemId }), "afterId", { totalEstimatedFee: 0 });
      case "/api/reports/completed-procedures/procedures":
      case "/api/reports/provider-activity/procedures": return this.page(c, this.procedureRecords, (r) => r.id, (r) => ({ afterTime: r.performedAtUtc, afterId: r.id }), "afterId", { totalEstimatedFee: 0 });
      default: return refusal(404, "not_found");
    }
  }
}
