/**
 * ALV-N005: typed client for the procedure and fee catalog (Controllers/ProceduresController).
 *
 * - A procedure's identity is its code system and code; both are fixed once it exists. A fee change is a NEW VERSION (the old one stays readable), never an edit in place.
 * - Fees are US dollars as numbers; the screen formats them, the server validates them (0 to 1,000,000.00, two decimals).
 * - Every change echoes the `rowVersion` it read (a stale one is the shared 409 concurrency conflict). Inactivating a procedure other records refer to is refused with
 *   `usage_confirmation_required` and the counts until the caller confirms with `acknowledgeUsage`.
 */
import { request, requestWithCsrf } from "./authApi";

export const CODE_SYSTEMS = ["Local", "CDT", "External"] as const;
export const CATEGORIES = ["Diagnostic", "Preventive", "Restorative", "Endodontic", "Periodontic", "Prosthodontic", "OralSurgery", "Orthodontic", "Adjunctive", "Other"] as const;
export const SCOPES = ["WholeMouth", "Arch", "Quadrant", "Tooth", "ToothSurface"] as const;
export const DENTITIONS = ["Permanent", "Primary", "Both"] as const;

export interface ProcedureVersion {
  versionId: string;
  procedureId: string;
  versionNumber: number;
  description: string;
  category: string;
  scope: string;
  dentition: string;
  fee: number;
  currency: string;
  sourceName: string | null;
  sourceVersion: string | null;
  effectiveFrom: string;
  validThrough: string | null;
  reason: string | null;
  createdByName: string | null;
  createdAtUtc: string;
}

export interface ProcedureSummary {
  id: string;
  codeSystem: string;
  code: string;
  isActive: boolean;
  /** Active, Inactive, Scheduled (no version has started yet) or Expired (past its last valid date), as of `asOf`. */
  status: string;
  asOf: string;
  currentVersionNumber: number;
  version: ProcedureVersion;
  rowVersion: string;
  createdAtUtc: string;
}

export interface ProcedureDetail {
  summary: ProcedureSummary;
  versions: ProcedureVersion[];
}

export interface ProcedureEvent {
  eventNumber: number;
  changeType: "Created" | "Revised" | "Inactivated" | "Reactivated";
  versionNumber: number | null;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

export interface ProcedureUsage {
  procedureId: string;
  count: number;
  bySource: { source: string; count: number }[];
}

/** The fields a person types. Dates are `YYYY-MM-DD`; an empty string means "not given". */
export interface ProcedureInput {
  codeSystem: string;
  code: string;
  description: string;
  category: string;
  scope: string;
  dentition: string;
  fee: number | null;
  sourceName: string;
  sourceVersion: string;
  effectiveFrom: string;
  validThrough: string;
}

export interface ProcedureFilters {
  search?: string;
  category?: string;
  codeSystem?: string;
  status?: "all" | "active" | "inactive";
}

const blank = (s: string) => (s.trim() === "" ? null : s.trim());
const body = (i: ProcedureInput) => ({
  codeSystem: i.codeSystem, code: i.code.trim(), description: i.description.trim(), category: i.category, scope: i.scope,
  dentition: i.scope === "Tooth" || i.scope === "ToothSurface" ? i.dentition : null, fee: i.fee,
  sourceName: blank(i.sourceName), sourceVersion: blank(i.sourceVersion), effectiveFrom: blank(i.effectiveFrom), validThrough: blank(i.validThrough),
});

export function listProcedures(filters: ProcedureFilters = {}, signal?: AbortSignal) {
  const q = new URLSearchParams();
  if (filters.search?.trim()) q.set("search", filters.search.trim());
  if (filters.category) q.set("category", filters.category);
  if (filters.codeSystem) q.set("codeSystem", filters.codeSystem);
  if (filters.status && filters.status !== "all") q.set("status", filters.status);
  const qs = q.toString();
  return request<ProcedureSummary[]>(`/api/procedures${qs ? `?${qs}` : ""}`, { signal });
}

export const getProcedure = (id: string, signal?: AbortSignal) => request<ProcedureDetail>(`/api/procedures/${id}`, { signal });
export const getProcedureHistory = (id: string, signal?: AbortSignal) => request<ProcedureEvent[]>(`/api/procedures/${id}/history`, { signal });
export const getProcedureUsage = (id: string, signal?: AbortSignal) => request<ProcedureUsage>(`/api/procedures/${id}/usage`, { signal });

export const createProcedure = (input: ProcedureInput) => requestWithCsrf<ProcedureDetail>("/api/procedures", "POST", body(input));
export const reviseProcedure = (id: string, input: ProcedureInput, reason: string, rowVersion: string) =>
  requestWithCsrf<ProcedureDetail>(`/api/procedures/${id}/revise`, "POST", { ...body(input), reason, rowVersion });
export const inactivateProcedure = (id: string, reason: string, acknowledgeUsage: boolean, rowVersion: string) =>
  requestWithCsrf<ProcedureDetail>(`/api/procedures/${id}/inactivate`, "POST", { reason, acknowledgeUsage, rowVersion });
export const reactivateProcedure = (id: string, reason: string, rowVersion: string) =>
  requestWithCsrf<ProcedureDetail>(`/api/procedures/${id}/reactivate`, "POST", { reason: reason || undefined, rowVersion });
