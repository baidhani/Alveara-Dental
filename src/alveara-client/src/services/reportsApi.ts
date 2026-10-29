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
  route: "schedule/appointments" | "patient-flow/appointments" | "cancellations/appointments";
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
