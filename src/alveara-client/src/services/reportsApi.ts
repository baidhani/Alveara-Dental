/**
 * ALV-N006: typed client for the reports (Controllers/ReportsController and its siblings). Reads only. Every report takes a practice-local date range (`from`, `to`, both `yyyy-MM-dd`) and, where it is
 * meaningful, booked providers and a location; a filter a report cannot honestly apply is refused by the server (`filter_not_supported`) and the screens never send it. Every number can be listed: a drill-down
 * returns the supporting records (ids, times and statuses, never a name or a note). Every refusal has a stable `error` code (see `reportErrorMessage`).
 */
import { ApiError, request } from "./authApi";

export interface ReportQuery {
  from: string;
  to: string;
  providerIds: string[];
  locationId: string | null;
}

export interface ReportFilterView {
  from: string;
  to: string;
  days: number;
  providerIds: string[];
  locationId: string | null;
  timeZone: string;
}

/** What every report comes in: the filter as the server understood it, when it was made, how the database was read, and whether anything matched. */
export interface ReportEnvelope<T> {
  reportId: string;
  definitionVersion: number;
  filter: ReportFilterView;
  generatedAtUtc: string;
  readConsistency: "Snapshot" | "TableByTable";
  recordCount: number;
  noData: boolean;
  data: T;
}

export interface Page<TRow, TCursor> {
  reportId: string;
  filter: ReportFilterView;
  total: number;
  rows: TRow[];
  next: TCursor | null;
}

// ---------- operational ----------

export interface ScheduleCounts { appointments: number; scheduled: number; cancelled: number; noShow: number; bookedMinutes: number }
export interface ScheduleData {
  totals: ScheduleCounts;
  rescheduleEvents: number;
  days: { date: string; counts: ScheduleCounts }[];
  providers: { providerProfileId: string; providerName: string; counts: ScheduleCounts }[];
}

export interface PatientFlowData {
  appointments: number;
  cancelled: number;
  noShow: number;
  byState: { state: string; visits: number }[];
  transitions: { event: string; count: number }[];
  pastNotCompleted: number;
  reconciled: boolean;
}

export interface FollowUpCounts { due: number; completed: number; cancelled: number; closedBySystem: number; openNotYetDue: number; openOverdue: number; completedLate: number; openUnassigned: number }
export interface FollowUpsData { asOfUtc: string; totals: FollowUpCounts; kinds: { kind: string; counts: FollowUpCounts }[]; reconciled: boolean }

export interface CancellationCounts { booked: number; cancelled: number; noShow: number; cancellationRate: number | null; noShowRate: number | null }
export interface CancellationsData {
  totals: CancellationCounts;
  leadTimes: { bucket: string; cancelled: number }[];
  providers: { providerProfileId: string; providerName: string; counts: CancellationCounts }[];
  types: { appointmentTypeId: string; typeName: string; counts: CancellationCounts }[];
  reconciled: boolean;
}

export interface AppointmentRecord { id: string; patientId: string; startUtc: string; durationMinutes: number; providerProfileId: string; operatoryId: string; status: string; flowState: string }
export interface AppointmentCursor { afterStart: string; afterId: string }
export type AppointmentPage = Page<AppointmentRecord, AppointmentCursor>;

export interface FollowUpRecord { id: string; patientId: string; kind: string; status: string; priority: string; dueAtUtc: string; closedAtUtc: string | null; assigneeUserId: string | null; closedBySystem: boolean }
export interface FollowUpCursor { afterDueAtUtc: string; afterId: string }
export type FollowUpPage = Page<FollowUpRecord, FollowUpCursor> & { asOfUtc: string };

// ---------- financial ----------

/** One line of the ledger for the period. `amount` is the effect on what patients owe: charges are positive, payments negative. */
export interface LedgerLine { line: string; entries: number; amount: number }
/** The proof that the lines add up to the ledger itself: an independent sum of the same entries, compared in amount and in count. */
export interface LedgerProof { lines: LedgerLine[]; linesTotal: number; linesEntries: number; ledgerTotal: number; ledgerEntries: number; reconciled: boolean; scope: "WholeLedger" | "ProviderProduction" }

export interface ProductionData {
  chargeEntries: number; grossCharges: number; chargesReversed: number; netProduction: number;
  providers: { providerProfileId: string | null; providerName: string; charges: number; gross: number; reversed: number; net: number }[];
  codes: { procedureCode: string; charges: number; gross: number; reversed: number; net: number }[];
  ledger: LedgerProof;
}

export interface CollectionsData {
  paymentEntries: number; paymentsReceived: number; paymentsReversed: number; refunded: number; netCollections: number;
  methods: { method: string; payments: number; received: number; reversed: number; refunded: number; net: number }[];
  ledger: LedgerProof;
}

export interface BalanceSnapshot { accounts: number; accountsOwing: number; totalOwed: number; accountsInCredit: number; totalCredit: number; netBalance: number }
export interface BalancesData { atStart: BalanceSnapshot; atEnd: BalanceSnapshot; change: number; ledgerChange: number; ledgerEntries: number; reconciled: boolean }

export interface LedgerEntryRecord { id: string; patientId: string; entryNumber: number; kind: string; line: string; amount: number; recordedAtUtc: string; paymentMethod: string | null; procedureCode: string | null }
export interface LedgerCursor { afterRecordedAtUtc: string; afterId: string }
export type LedgerPage = Page<LedgerEntryRecord, LedgerCursor> & { totalAmount: number };

export interface BalanceAccountRecord { accountId: string; patientId: string; balance: number }
export interface BalanceCursor { afterBalance: number; afterAccountId: string }
export type BalancePage = Page<BalanceAccountRecord, BalanceCursor> & { side: string; totalAmount: number };

// ---------- clinical ----------

export interface PlanStatusData { plans: number; plansProposed: number; plansWithdrawn: number; items: number; cells: { cell: string; items: number; estimatedFee: number }[]; reconciled: boolean }

export interface CompletedProceduresData {
  procedures: number; estimatedFee: number; reconciled: boolean;
  days: { date: string; procedures: number }[];
  providers: { providerProfileId: string; providerName: string; procedures: number; estimatedFee: number }[];
  codes: { codeSystem: string; code: string; description: string; procedures: number; estimatedFee: number }[];
}

export interface ProviderActivityRow { providerProfileId: string; providerName: string; booked: number; scheduled: number; cancelled: number; noShow: number; visitsCompleted: number; proceduresCompleted: number }
export interface ProviderActivityData { providers: ProviderActivityRow[]; totals: Omit<ProviderActivityRow, "providerProfileId" | "providerName">; reconciled: boolean }

export interface PlanItemRecord { itemId: string; planId: string; patientId: string; itemNumber: number; estimatedFee: number; acceptance: string; scheduling: string; completion: string; withdrawn: boolean }
export interface ProcedureRecord { id: string; patientId: string; planItemId: string; providerProfileId: string; performedAtUtc: string; codeSystem: string; code: string; estimatedFee: number }
export interface ClinicalCursor { afterTime: string | null; afterId: string }
export type PlanItemPage = Page<PlanItemRecord, ClinicalCursor> & { totalEstimatedFee: number };
export type ProcedurePage = Page<ProcedureRecord, ClinicalCursor> & { totalEstimatedFee: number };

// ---------- calls ----------

const BASE = "/api/reports";

function params(query: ReportQuery, extra: Record<string, string | number | null | undefined> = {}): string {
  const p = new URLSearchParams({ from: query.from, to: query.to });
  for (const id of query.providerIds) p.append("providerIds", id);
  if (query.locationId) p.set("locationId", query.locationId);
  for (const [key, value] of Object.entries(extra)) if (value !== null && value !== undefined && value !== "") p.set(key, String(value));
  return p.toString();
}

export const getSchedule = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<ScheduleData>>(`${BASE}/schedule?${params(q)}`, { signal });
export const getPatientFlow = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<PatientFlowData>>(`${BASE}/patient-flow?${params(q)}`, { signal });
export const getFollowUps = (q: ReportQuery, asOf?: string, signal?: AbortSignal) => request<ReportEnvelope<FollowUpsData>>(`${BASE}/follow-ups?${params(q, { asOf })}`, { signal });
export const getCancellations = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<CancellationsData>>(`${BASE}/cancellations?${params(q)}`, { signal });

export interface AppointmentDrill {
  route: "schedule/appointments" | "patient-flow/appointments" | "cancellations/appointments" | "provider-activity/appointments";
  status?: string; date?: string; flowState?: string; providerId?: string; measure?: string; appointmentTypeId?: string; leadTime?: string;
  after?: AppointmentCursor | null;
}

export function getAppointments(q: ReportQuery, drill: AppointmentDrill, signal?: AbortSignal) {
  const { route, after, ...criteria } = drill;
  return request<AppointmentPage>(`${BASE}/${route}?${params(q, { ...criteria, afterStart: after?.afterStart, afterId: after?.afterId })}`, { signal });
}

export interface FollowUpDrill { kind?: string; outcome?: string; asOf?: string; after?: FollowUpCursor | null }

export function getFollowUpTasks(q: ReportQuery, drill: FollowUpDrill, signal?: AbortSignal) {
  const { after, ...criteria } = drill;
  return request<FollowUpPage>(`${BASE}/follow-ups/tasks?${params(q, { ...criteria, afterDueAtUtc: after?.afterDueAtUtc, afterId: after?.afterId })}`, { signal });
}


export const getProduction = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<ProductionData>>(`${BASE}/production?${params(q)}`, { signal });
export const getCollections = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<CollectionsData>>(`${BASE}/collections?${params(q)}`, { signal });
export const getBalances = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<BalancesData>>(`${BASE}/balances?${params(q)}`, { signal });
export const getTreatmentPlans = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<PlanStatusData>>(`${BASE}/treatment-plans?${params(q)}`, { signal });
export const getCompletedProcedures = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<CompletedProceduresData>>(`${BASE}/completed-procedures?${params(q)}`, { signal });
export const getProviderActivity = (q: ReportQuery, signal?: AbortSignal) => request<ReportEnvelope<ProviderActivityData>>(`${BASE}/provider-activity?${params(q)}`, { signal });

export interface LedgerDrill { route: "production/entries" | "collections/entries"; line?: string; providerId?: string; method?: string; after?: LedgerCursor | null }

export function getLedgerEntries(q: ReportQuery, drill: LedgerDrill, signal?: AbortSignal) {
  const { route, after, ...criteria } = drill;
  return request<LedgerPage>(`${BASE}/${route}?${params(q, { ...criteria, afterRecordedAtUtc: after?.afterRecordedAtUtc, afterId: after?.afterId })}`, { signal });
}

export function getBalanceAccounts(q: ReportQuery, drill: { side?: string; after?: BalanceCursor | null }, signal?: AbortSignal) {
  return request<BalancePage>(`${BASE}/balances/accounts?${params(q, { side: drill.side, afterBalance: drill.after?.afterBalance, afterAccountId: drill.after?.afterAccountId })}`, { signal });
}

export function getPlanItems(q: ReportQuery, drill: { cell?: string; after?: ClinicalCursor | null }, signal?: AbortSignal) {
  return request<PlanItemPage>(`${BASE}/treatment-plans/items?${params(q, { cell: drill.cell, afterId: drill.after?.afterId })}`, { signal });
}

export interface ProcedureDrill { route?: "completed-procedures/procedures" | "provider-activity/procedures"; providerId?: string; codeSystem?: string; code?: string; date?: string; after?: ClinicalCursor | null }

export function getProcedures(q: ReportQuery, drill: ProcedureDrill, signal?: AbortSignal) {
  const { route = "completed-procedures/procedures", after, ...criteria } = drill;
  return request<ProcedurePage>(`${BASE}/${route}?${params(q, { ...criteria, afterPerformedAtUtc: after?.afterTime, afterId: after?.afterId })}`, { signal });
}

// ---------- words ----------

const MESSAGES: Record<string, string> = {
  dates_required: "Choose a first and a last date.",
  invalid_date: "A date is not valid. Dates are written year-month-day.",
  invalid_range: "The first date must not be after the last date.",
  range_too_large: "That range is longer than a report can cover. Choose a shorter one.",
  too_many_providers: "Too many providers are chosen. Choose fewer.",
  unknown_provider: "One of the chosen providers no longer exists.",
  unknown_location: "The chosen location no longer exists.",
  filter_not_supported: "This report cannot use one of the filters that is set. Clear it, or choose another report.",
  report_too_large: "That range holds too many records for one report. Choose a shorter range or fewer providers.",
  audit_unavailable: "The report could not be recorded, so it was not produced. Try again in a moment.",
  invalid_limit: "The page size is not valid.",
  invalid_cursor: "The page position is not valid. Reload the list.",
  invalid_line: "That part of the ledger does not belong to this report.",
  invalid_cell: "That group of plan items does not exist.",
  invalid_measure: "That measure does not exist.",
  invalid_side: "The side must be owing or credit.",
};

export function reportErrorMessage(e: unknown): string {
  if (e instanceof ApiError) {
    if (e.status === 403) return "You do not have access to this report.";
    if (e.status === 0) return "The server could not be reached. Check your connection and try again.";
    return MESSAGES[e.code] ?? e.message ?? "The report could not be produced.";
  }
  return "The report could not be produced.";
}

export const wasCancelled = (e: unknown) => e instanceof DOMException && e.name === "AbortError";
