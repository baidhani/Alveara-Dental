/**
 * STORY-015: typed client for treatment plans (Controllers/TreatmentPlansController) and the catalog call planning makes (GET /api/procedures/active).
 *
 * - A plan belongs to one patient and is never deleted: it is `Proposed` or `Withdrawn` (final). Each item names one current diagnosis of the patient and one active catalog procedure, and carries
 *   the fee of the catalog version in effect when it was proposed. `estimateLabel` says what the total is (the practice's fees, not an insurance estimate); the screen always shows it.
 * - Items are never edited: a wrong one is withdrawn with a reason and the right one added.
 * - Every change echoes the plan's `rowVersion` (a stale one is the shared 409 concurrency conflict). A refusal carries `fieldErrors`, keyed by the field to correct (`title`, `reason`,
 *   `items[0].diagnosisId`, or `diagnosisId` when one item is added).
 * - A create carries an idempotency key; the same key returns the plan already saved.
 * - Reading plans needs clinical-documentation access; changing them needs the manage-treatment-plans permission; the catalog call needs billing access, so a read-only screen must not call it.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

export interface PlanItem {
  id: string;
  itemNumber: number;
  diagnosisId: string;
  diagnosisLabel: string;
  procedureId: string;
  procedureVersionId: string;
  procedureVersionNumber: number;
  procedureCodeSystem: string;
  procedureCode: string;
  procedureDescription: string;
  toothKey: string | null;
  surface: string | null;
  fee: number;
  currency: string;
  isWithdrawn: boolean;
  withdrawnReason: string | null;
  withdrawnAtUtc: string | null;
  withdrawnByName: string | null;
  createdAtUtc: string;
  createdByName: string | null;
}

export interface TreatmentPlan {
  id: string;
  patientId: string;
  title: string;
  status: "Proposed" | "Withdrawn";
  activeItemCount: number;
  estimateTotal: number;
  currency: string;
  estimateLabel: string;
  items: PlanItem[];
  createdAtUtc: string;
  createdByName: string | null;
  updatedAtUtc: string | null;
  updatedByName: string | null;
  withdrawnReason: string | null;
  withdrawnAtUtc: string | null;
  withdrawnByName: string | null;
  rowVersion: string;
}

export interface PlanEvent {
  eventNumber: number;
  changeType: string;
  itemId: string | null;
  title: string | null;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
}

/** One procedure the catalog offers today, as planning sees it (the fields the picker shows). */
export interface PlanningProcedure {
  procedureId: string;
  versionId: string;
  versionNumber: number;
  codeSystem: string;
  code: string;
  description: string;
  category: string;
  scope: string;
  dentition: string;
  fee: number;
  currency: string;
}

export interface PlanItemInput {
  diagnosisId: string;
  procedureId: string;
  toothKey: string | null;
  surface: string | null;
}

export const listTreatmentPlans = (patientId: string, options: { includeWithdrawn?: boolean } = {}, signal?: AbortSignal) =>
  request<{ patientId: string; plans: TreatmentPlan[] }>(`/api/patients/${patientId}/treatment-plans${options.includeWithdrawn ? "?includeWithdrawn=true" : ""}`, { signal });

export const getTreatmentPlanHistory = (planId: string, signal?: AbortSignal) => request<PlanEvent[]>(`/api/treatment-plans/${planId}/history`, { signal });

export const createTreatmentPlan = (patientId: string, idempotencyKey: string, title: string, items: PlanItemInput[]) =>
  requestWithCsrf<TreatmentPlan>(`/api/patients/${patientId}/treatment-plans`, "POST", { idempotencyKey, title, items });

type Versioned = Pick<TreatmentPlan, "id" | "rowVersion">;

export const addPlanItem = (plan: Versioned, idempotencyKey: string, item: PlanItemInput) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/items`, "POST", { idempotencyKey, rowVersion: plan.rowVersion, ...item });

export const withdrawPlanItem = (plan: Versioned, itemId: string, reason: string) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/items/${itemId}/withdraw`, "POST", { rowVersion: plan.rowVersion, reason });

export const renameTreatmentPlan = (plan: Versioned, title: string) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/rename`, "POST", { rowVersion: plan.rowVersion, title });

export const withdrawTreatmentPlan = (plan: Versioned, reason: string) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/withdraw`, "POST", { rowVersion: plan.rowVersion, reason });

/** The catalog procedures that may be offered today, optionally only those that fit a tooth and surface. Needs billing access: only the authoring screen calls it. */
export function listActiveProcedures(options: { toothKey?: string; surface?: string } = {}, signal?: AbortSignal) {
  const q = new URLSearchParams();
  if (options.toothKey) q.set("toothKey", options.toothKey);
  if (options.surface) q.set("surface", options.surface);
  const qs = q.toString();
  return request<PlanningProcedure[]>(`/api/procedures/active${qs ? `?${qs}` : ""}`, { signal });
}

/** The field-by-field messages a refused save lists (empty for any other failure), keyed by the field to correct. */
export function planFieldErrorsOf(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError)) return {};
  const raw = err.body.fieldErrors;
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return {};
  return Object.fromEntries(Object.entries(raw as Record<string, unknown>).filter((e): e is [string, string] => typeof e[1] === "string"));
}
