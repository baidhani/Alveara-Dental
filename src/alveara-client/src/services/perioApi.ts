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
