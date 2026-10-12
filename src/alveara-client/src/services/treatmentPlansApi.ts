/**
 * STORY-015: typed client for treatment plans (Controllers/TreatmentPlansController) and the catalog call planning makes (GET /api/procedures/active).
 *
 * - A plan belongs to one patient and is never deleted: it is `Proposed` or `Withdrawn` (final). Each item names one current diagnosis of the patient and one active catalog procedure, and carries
 *   the fee of the catalog version in effect when it was proposed. `estimateLabel` says what the total is (the practice's fees, not an insurance estimate); the screen always shows it.
 * - Items are never edited: a wrong one is withdrawn with a reason and the right one added.
 * - Every change echoes the plan's `rowVersion` (a stale one is the shared 409 concurrency conflict). A refusal carries `fieldErrors`, keyed by the field to correct (`title`, `reason`,
 *   `items[0].diagnosisId`, or `diagnosisId` when one item is added).
 * - ALV-015-C01: a plan's content is saved as numbered revisions (every change saves the next one and no earlier one is ever touched); phases group the procedures; each procedure carries the answer a patient gave
 *   (Pending, Accepted or Declined, with who entered it and who answered), whether it is scheduled and whether it is completed; the plan's `acceptanceStatus` is derived from them. A decision echoes the plan's
 *   `rowVersion` so what is recorded is what was on screen. The estimate is the practice's catalog fee, labelled as such by `estimateLabel`; nothing here is an insurance figure.
 * - A create carries an idempotency key; the same key returns the plan already saved.
 * - Reading plans needs clinical-documentation access; changing them needs the manage-treatment-plans permission; the catalog call needs billing access, so a read-only screen must not call it.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

export type Acceptance = "Pending" | "Accepted" | "Declined";

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
  phaseNumber: number | null;
  acceptance: Acceptance;
  scheduling: "Unscheduled" | "Scheduled";
  completion: "NotStarted" | "Completed";
  plannedDate: string | null;
  decisionReason: string | null;
  decidedByName: string | null;
  recordedByName: string | null;
  recordedAtUtc: string | null;
  decisionRevision: number | null;
}

export interface PlanPhase {
  phaseNumber: number;
  name: string;
  plannedVisit: string | null;
  targetDate: string | null;
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
  currentRevision: number;
  referenceCode: string | null;
  clinicalRationale: string | null;
  patientExplanation: string | null;
  phases: PlanPhase[];
  acceptanceStatus: Acceptance | "Partial";
  acceptedEstimateTotal: number;
}

export interface PlanEvent {
  eventNumber: number;
  changeType: string;
  itemId: string | null;
  title: string | null;
  reason: string | null;
  actorName: string | null;
  occurredAtUtc: string;
  revisionNumber: number | null;
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
  /** Only when adding a procedure to an existing plan: the phase it sits in. */
  phaseNumber?: number | null;
}

export interface PhaseInput { number: number | null; name: string; plannedVisit: string | null; targetDate: string | null }
export interface PlacementInput { itemId: string; phaseNumber: number | null }

/** An explicit revision. `reason` is required. Leaving a part out (undefined) keeps it; a blank rationale or explanation clears it; an empty `phases` list removes them all. */
export interface ReviseInput {
  reason: string;
  clinicalRationale?: string | null;
  patientExplanation?: string | null;
  phases?: PhaseInput[];
  placements?: PlacementInput[];
}

export interface DecisionInput { decision: "Accepted" | "Declined"; reason?: string | null; decidedByName?: string | null }

export interface RevisionSummary {
  revisionNumber: number;
  changeType: string;
  title: string;
  reason: string | null;
  actorName: string | null;
  createdAtUtc: string;
  procedureCount: number;
  estimateTotal: number;
  phaseCount: number;
}

export interface RevisionItem {
  itemId: string;
  itemNumber: number;
  procedureCode: string;
  procedureDescription: string;
  diagnosisLabel: string;
  toothKey: string | null;
  surface: string | null;
  fee: number;
  isWithdrawn: boolean;
  phaseNumber: number | null;
}

export interface RevisionDetail {
  summary: RevisionSummary;
  clinicalRationale: string | null;
  patientExplanation: string | null;
  phases: PlanPhase[];
  items: RevisionItem[];
  estimateLabel: string;
}

export interface FieldChange { from: string | null; to: string | null }
export interface PhaseChange { phaseNumber: number; from: PlanPhase; to: PlanPhase }
export interface ItemMove { item: RevisionItem; fromPhase: number | null; toPhase: number | null }

export interface RevisionComparison {
  from: RevisionSummary;
  to: RevisionSummary;
  title: FieldChange | null;
  clinicalRationale: FieldChange | null;
  patientExplanation: FieldChange | null;
  phasesAdded: PlanPhase[];
  phasesRemoved: PlanPhase[];
  phasesChanged: PhaseChange[];
  itemsAdded: RevisionItem[];
  itemsWithdrawn: RevisionItem[];
  itemsMoved: ItemMove[];
  estimateFrom: number;
  estimateTo: number;
  estimateDelta: number;
  estimateLabel: string;
}

/** What happened to one diagnosis's old free-text treatment-plan reference. `Eligible` is a preview's word for one that would be resolved. */
export type ReconciliationOutcome = "Resolved" | "Eligible" | "AlreadyResolved" | "NoMatch" | "Ambiguous" | "Skipped";

export interface ReconciliationItem {
  diagnosisId: string;
  diagnosisLabel: string;
  reference: string | null;
  outcome: ReconciliationOutcome;
  planId: string | null;
  planTitle: string | null;
  method: string | null;
  reason: string | null;
}

export interface ReconciliationResult {
  patientId: string;
  applied: boolean;
  matched: number;
  alreadyResolved: number;
  unresolved: number;
  items: ReconciliationItem[];
}

export const listTreatmentPlans = (patientId: string, options: { includeWithdrawn?: boolean } = {}, signal?: AbortSignal) =>
  request<{ patientId: string; plans: TreatmentPlan[] }>(`/api/patients/${patientId}/treatment-plans${options.includeWithdrawn ? "?includeWithdrawn=true" : ""}`, { signal });

export const getTreatmentPlanHistory = (planId: string, signal?: AbortSignal) => request<PlanEvent[]>(`/api/treatment-plans/${planId}/history`, { signal });

export const createTreatmentPlan = (patientId: string, idempotencyKey: string, title: string, items: PlanItemInput[]) =>
  requestWithCsrf<TreatmentPlan>(`/api/patients/${patientId}/treatment-plans`, "POST", { idempotencyKey, title, items });

type Versioned = Pick<TreatmentPlan, "id" | "rowVersion">;

export const addPlanItem = (plan: Versioned, idempotencyKey: string, item: PlanItemInput) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/items`, "POST", { idempotencyKey, rowVersion: plan.rowVersion, ...item });

export const revisePlan = (plan: Versioned, input: ReviseInput) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/revise`, "POST", { rowVersion: plan.rowVersion, ...input });

/** Accepts or declines one procedure. A decline needs a reason; `decidedByName` is who gave the answer (the signed-in account is recorded as who entered it). */
export const decideItem = (plan: Versioned, itemId: string, input: DecisionInput) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/items/${itemId}/decision`, "POST", { rowVersion: plan.rowVersion, ...input });

/** Accepts or declines every procedure still pending. Refused (409 partial_acceptance_conflict) when any holds the opposite answer. */
export const decidePlan = (plan: Versioned, input: DecisionInput) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/decision`, "POST", { rowVersion: plan.rowVersion, ...input });

export const scheduleItem = (planId: string, itemId: string, scheduled: boolean, plannedDate: string | null) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${planId}/items/${itemId}/schedule`, "POST", { scheduled, plannedDate });

export const completeItem = (planId: string, itemId: string) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${planId}/items/${itemId}/complete`, "POST", {});

/** STORY-008: completes every accepted, not yet completed procedure of the plan in one all-or-nothing save. Refused (409 nothing_to_complete) when none is accepted; 409 on a stale plan. */
export const completeAcceptedProcedures = (plan: Versioned) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/complete-accepted`, "POST", { rowVersion: plan.rowVersion });

/** One completed procedure in a patient's clinical history, read from the completion record of the plan it was planned in. */
export interface ClinicalHistoryEntry {
  planId: string;
  planTitle: string;
  itemId: string;
  itemNumber: number;
  procedureCodeSystem: string;
  procedureCode: string;
  procedureDescription: string;
  toothKey: string | null;
  surface: string | null;
  completedByName: string;
  completedAtUtc: string;
  planRevisionAtCompletion: number;
  /** ALV-008-C01: filled only for a procedure completed through the authoritative workflow; null for one completed the old way. */
  completedProcedureId?: string | null;
  providerName?: string | null;
  performedAtUtc?: string | null;
  encounterId?: string | null;
  chargeEntryNumber?: number | null;
  chargeAmount?: number | null;
  chartOutcome?: string | null;
}

export const getClinicalHistory = (patientId: string, signal?: AbortSignal) =>
  request<{ patientId: string; entries: ClinicalHistoryEntry[] }>(`/api/patients/${patientId}/clinical-history`, { signal });

/** Gives the plan its reference code, set once, which an old diagnosis's free-text reference can be matched to. */
export const setReferenceCode = (plan: Versioned, code: string) =>
  requestWithCsrf<TreatmentPlan>(`/api/treatment-plans/${plan.id}/reference-code`, "POST", { rowVersion: plan.rowVersion, code });

export const getRevisions = (planId: string, signal?: AbortSignal) => request<RevisionSummary[]>(`/api/treatment-plans/${planId}/revisions`, { signal });
export const getRevision = (planId: string, revision: number, signal?: AbortSignal) => request<RevisionDetail>(`/api/treatment-plans/${planId}/revisions/${revision}`, { signal });
export const compareRevisions = (planId: string, from: number, to: number, signal?: AbortSignal) =>
  request<RevisionComparison>(`/api/treatment-plans/${planId}/revisions/compare?from=${from}&to=${to}`, { signal });

/** Previews (apply false) or applies the matching of a patient's old diagnosis references to that patient's plans by reference code or id. */
export const reconcileReferences = (patientId: string, apply: boolean) =>
  requestWithCsrf<ReconciliationResult>(`/api/patients/${patientId}/treatment-plans/reconcile-references`, "POST", { apply });

/** Ties one diagnosis's reference to a plan the person chose (the same patient's plan only). */
export const resolvePlanReference = (diagnosisId: string, planId: string) =>
  requestWithCsrf<ReconciliationItem>(`/api/diagnoses/${diagnosisId}/resolve-plan-reference`, "POST", { planId });

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
