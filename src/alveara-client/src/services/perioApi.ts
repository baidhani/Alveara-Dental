/**
 * STORY-012: typed client for periodontal charting (Controllers/PerioController).
 *
 * - A tooth is always its FDI key ("16", "48"); how it is NUMBERED on screen is a display matter (see pages/odontogram/toothNumbering.ts) and never travels to the server.
 * - Every field of a reading is required. A saved chart is never edited: a correction is a new chart, saved under a new key.
 * - The same key with the same readings returns the chart already saved (a retry is safe); the same key with different readings is a 409.
 * - An empty history means nothing has been charted, never that the gums are healthy.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

export interface PerioReadingInput {
  toothKey: string;
  site: string;
  probingDepthMm: number;
  recessionMm: number;
  bleeding: boolean;
  /** Pus and plaque at the site (ALV-012-C01). Left out or null means not assessed, which is different from false. */
  suppuration?: boolean | null;
  plaque?: boolean | null;
}

export interface PerioReading extends PerioReadingInput {
  /** Derived by the server (probing depth + recession); never entered. */
  attachmentLossMm: number;
}

export interface PerioChart {
  id: string;
  patientId: string;
  recordedAtUtc: string;
  recordedByName: string;
  readingCount: number;
  readings: PerioReading[];
  /** Whole-tooth records and links (ALV-012-C01); absent on charts saved before they existed. */
  teeth?: PerioTooth[];
  links?: PerioLink[];
}

export interface PerioHistory {
  patientId: string;
  exams: PerioChart[];
}

/** One entry the server says needs correcting: which tooth and site, which field, a stable code and a message safe to show. */
export interface PerioProblem {
  toothKey: string | null;
  site: string | null;
  field: string;
  code: string;
  message: string;
}

export const getPerioCharts = (patientId: string, signal?: AbortSignal) => request<PerioHistory>(`/api/patients/${patientId}/periodontal/charts`, { signal });

export const savePerioChart = (patientId: string, idempotencyKey: string, readings: PerioReadingInput[]) =>
  requestWithCsrf<PerioChart>(`/api/patients/${patientId}/periodontal/charts`, "POST", { idempotencyKey, readings });

/** The entries a refused save lists (empty for any other failure), so the screen can point at each one. */
export function perioProblemsOf(err: unknown): PerioProblem[] {
  if (!(err instanceof ApiError)) return [];
  const raw = err.body.problems;
  return Array.isArray(raw) ? (raw as PerioProblem[]) : [];
}

// ---------- ALV-012-C01: chart sessions, whole-tooth records, links and comparison ----------

/** What a chart records about a whole tooth. Mobility and furcation are grades 0 to 3 (null = not assessed); furcation only applies to multi-rooted teeth. */
export interface PerioTooth { toothKey: string; mobility: number | null; furcation: number | null; excluded: boolean }
export interface PerioToothInput { toothKey: string; mobility?: number | null; furcation?: number | null; excluded?: boolean }
export interface PerioLink { linkType: string; reference: string; linkedByName: string | null; linkedAtUtc: string }
export const PERIO_LINK_TYPES = ["Diagnosis", "TreatmentPlan", "Encounter", "HistoryEntry"] as const;
export const PERIO_LINK_LABELS: Record<string, string> = { Diagnosis: "Diagnosis", TreatmentPlan: "Treatment plan", Encounter: "Encounter", HistoryEntry: "History entry" };

/**
 * A chart being entered. `rowVersion` is echoed with every save, finalize and abandon so a stale edit is the shared 409 conflict. `absentTeeth` are teeth the odontogram records as missing: they are
 * skipped by `next` and cannot be charted unless marked not charted. `next` is the first site in the documented sweep with no reading yet, or null when every chartable site has one.
 */
export interface PerioSession {
  id: string;
  patientId: string;
  status: "Draft" | "Finalized" | "Abandoned";
  startedAtUtc: string;
  startedByName: string;
  updatedAtUtc: string | null;
  updatedByName: string | null;
  closedAtUtc: string | null;
  examId: string | null;
  rowVersion: string;
  readings: PerioReading[];
  teeth: PerioTooth[];
  absentTeeth: string[];
  next: { toothKey: string; site: string } | null;
  chartableSites: number;
}

/** One save into a draft: sites and whole-tooth records to set, sites and teeth to clear. Applied together or not at all. */
export interface PerioEntryBatch {
  readings?: PerioReadingInput[];
  teeth?: PerioToothInput[];
  clearSites?: { toothKey: string; site: string }[];
  clearTeeth?: string[];
}

export const getCurrentPerioSession = (patientId: string, signal?: AbortSignal) => request<{ session: PerioSession | null }>(`/api/patients/${patientId}/periodontal/session`, { signal });
/** The patient's open draft, started if there is none (the same draft however many times it is asked). */
export const startPerioSession = (patientId: string) => requestWithCsrf<PerioSession>(`/api/patients/${patientId}/periodontal/sessions`, "POST");
export const getPerioSession = (sessionId: string, signal?: AbortSignal) => request<PerioSession>(`/api/periodontal/sessions/${sessionId}`, { signal });
export const savePerioEntries = (session: Pick<PerioSession, "id" | "rowVersion">, batch: PerioEntryBatch) =>
  requestWithCsrf<PerioSession>(`/api/periodontal/sessions/${session.id}/entries`, "POST", { rowVersion: session.rowVersion, ...batch });
export const finalizePerioSession = (session: Pick<PerioSession, "id" | "rowVersion">) =>
  requestWithCsrf<PerioChart>(`/api/periodontal/sessions/${session.id}/finalize`, "POST", { rowVersion: session.rowVersion });
export const abandonPerioSession = (session: Pick<PerioSession, "id" | "rowVersion">) =>
  requestWithCsrf<PerioSession>(`/api/periodontal/sessions/${session.id}/abandon`, "POST", { rowVersion: session.rowVersion });
export const getPerioChart = (examId: string, signal?: AbortSignal) => request<PerioChart>(`/api/periodontal/charts/${examId}`, { signal });
export const linkPerioChart = (examId: string, linkType: string, reference: string) => requestWithCsrf<PerioChart>(`/api/periodontal/charts/${examId}/links`, "POST", { linkType, reference });

export interface PerioFigures {
  sites: number; teeth: number; meanDepthMm: number; meanAttachmentLossMm: number; bleedingPercent: number; deepSites: number; veryDeepSites: number; suppurationSites: number; plaquePercent: number | null;
}
export type SiteTrend = "Improved" | "Worsened" | "Unchanged" | "OnlyPrevious" | "OnlyCurrent";
export interface PerioSiteChange {
  toothKey: string; site: string; previousDepthMm: number | null; currentDepthMm: number | null; depthChangeMm: number | null;
  previousAttachmentLossMm: number | null; currentAttachmentLossMm: number | null; attachmentLossChangeMm: number | null;
  previousBleeding: boolean | null; currentBleeding: boolean | null; trend: SiteTrend; currentCues: string[];
}
export interface PerioToothChange {
  toothKey: string; previousState: string; currentState: string; previousMobility: number | null; currentMobility: number | null; previousFurcation: number | null; currentFurcation: number | null;
}
/** Two charts compared. The counts cover sites in both charts; sites in only one are listed and counted separately, never dropped. */
export interface PerioComparison {
  previous: PerioFigures; current: PerioFigures; matchedSites: number; improved: number; worsened: number; unchanged: number; onlyPrevious: number; onlyCurrent: number; meanDepthChangeMm: number;
  sites: PerioSiteChange[]; teeth: PerioToothChange[];
}
/** Compares a saved chart or the draft being entered with an earlier chart (by default the latest finalized one before it). Exactly one of the two `current` ids. */
export const comparePerio = (patientId: string, current: { examId: string } | { sessionId: string }, previousExamId?: string, signal?: AbortSignal) => {
  const q = new URLSearchParams("examId" in current ? { currentExamId: current.examId } : { currentSessionId: current.sessionId });
  if (previousExamId) q.set("previousExamId", previousExamId);
  return request<PerioComparison>(`/api/patients/${patientId}/periodontal/comparison?${q}`, { signal });
};
