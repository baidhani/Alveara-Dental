/**
 * ALV-008-C01: typed client for completing a selected set of one plan's procedures (Controllers/CompletionController).
 *
 * - `getCompletionReadiness` is what the review screen reads first. The server says, for each procedure, whether it can be completed (and why not), what it would do to the chart and whether it would be
 *   charged, plus the active providers and the patient's encounters to choose from. The screen shows that and never works the rules out itself.
 * - `completeProcedures` completes the selection as one operation. The request carries a save key (a retry is safe), the plan's `rowVersion` as last read, the encounter, the provider and when it was performed;
 *   it carries no charge amount (the charge is the plan's stored fee). Nothing is completed unless the whole set is.
 * - A refusal has a stable `error` code and `fieldErrors`. `items_not_completable` and `ambiguous_chart_finding` also list the procedures they are about in `items`. A stale plan is the shared 409
 *   concurrency conflict; `save_failed` (503) means nothing was completed.
 * - Completed procedures are never edited: `amendCompletedProcedure` adds a numbered correction (provider, performed time or encounter) with a reason, against the amendment last seen.
 * - Completing and correcting need the manage-treatment-plans permission; reading completed procedures needs clinical-documentation access.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";
import type { TreatmentPlan } from "./treatmentPlansApi";

export type ChartEffect = "Updated" | "AlreadyCompleted" | "NotApplicable" | "Ambiguous";
export type ChargeOutcome = "created" | "existing" | "none";

export interface ItemReadiness {
  itemId: string;
  itemNumber: number;
  procedureCode: string;
  procedureDescription: string;
  toothKey: string | null;
  surface: string | null;
  ready: boolean;
  /** `already_completed`, `withdrawn`, `not_accepted` or `ambiguous_chart_finding` when the procedure is not ready. */
  notReadyReason: string | null;
  chartEffect: ChartEffect;
  chartMatches: number;
  billable: boolean;
  fee: number;
  chargeExists: boolean;
}

export interface ProviderChoice {
  id: string;
  name: string;
  specialty: string;
}

export interface EncounterChoice {
  id: string;
  atUtc: string;
  status: string;
}

export interface CompletionReadiness {
  planId: string;
  rowVersion: string;
  items: ItemReadiness[];
  providers: ProviderChoice[];
  encounters: EncounterChoice[];
}

export interface ChargeEffectView {
  outcome: ChargeOutcome;
  entryNumber: number | null;
  amount: number | null;
}

export interface CompletedProcedureResult {
  id: string;
  planItemId: string;
  itemNumber: number;
  procedureCode: string;
  procedureDescription: string;
  toothKey: string | null;
  surface: string | null;
  encounterId: string;
  providerProfileId: string;
  performedAtUtc: string;
  planRevisionNumber: number;
  chartOutcome: string;
  toothFindingId: string | null;
  charge: ChargeEffectView | null;
}

export interface CompletionResult {
  plan: TreatmentPlan;
  setId: string;
  correlationId: string;
  replayed: boolean;
  procedures: CompletedProcedureResult[];
}

export interface CompleteProceduresInput {
  idempotencyKey: string;
  rowVersion: string;
  encounterId: string;
  providerId: string;
  performedAtUtc: string;
  itemIds: string[];
}

export interface AmendmentView {
  number: number;
  reason: string;
  providerProfileId: string | null;
  providerName: string | null;
  performedAtUtc: string | null;
  encounterId: string | null;
  encounterAtUtc: string | null;
  amendedByName: string;
  amendedAtUtc: string;
}

export interface CompletedProcedureDetail {
  id: string;
  patientId: string;
  planId: string;
  planTitle: string;
  itemNumber: number;
  planRevisionNumber: number;
  procedureCode: string;
  procedureDescription: string;
  toothKey: string | null;
  surface: string | null;
  encounterId: string;
  encounterAtUtc: string;
  providerProfileId: string;
  providerName: string;
  performedAtUtc: string;
  originalProviderProfileId: string;
  originalPerformedAtUtc: string;
  chartOutcome: string;
  charge: ChargeEffectView | null;
  finalizedByName: string;
  finalizedAtUtc: string;
  latestAmendmentNumber: number;
  amendments: AmendmentView[];
}

/** One procedure a refusal is about (`items` of an `items_not_completable` or `ambiguous_chart_finding` refusal). */
export interface RefusedItem {
  itemId: string;
  itemNumber: number;
  reason?: string;
  toothKey?: string;
  surface?: string | null;
  matches?: number;
}

export const getCompletionReadiness = (planId: string, itemIds?: string[], signal?: AbortSignal) => {
  const query = itemIds && itemIds.length > 0 ? `?${itemIds.map((i) => `itemIds=${encodeURIComponent(i)}`).join("&")}` : "";
  return request<CompletionReadiness>(`/api/treatment-plans/${planId}/completion-readiness${query}`, { signal });
};

export const completeProcedures = (planId: string, input: CompleteProceduresInput) =>
  requestWithCsrf<CompletionResult>(`/api/treatment-plans/${planId}/complete-procedures`, "POST", input);

export const listCompletedProcedures = (patientId: string, signal?: AbortSignal) =>
  request<{ patientId: string; procedures: CompletedProcedureDetail[] }>(`/api/patients/${patientId}/completed-procedures`, { signal });

export const amendCompletedProcedure = (id: string, input: { idempotencyKey: string; expectedAmendmentNumber: number; providerId?: string; performedAtUtc?: string; encounterId?: string; reason: string }) =>
  requestWithCsrf<{ procedure: CompletedProcedureDetail; amendmentNumber: number; replayed: boolean }>(`/api/completed-procedures/${id}/amend`, "POST", input);

/** The procedures a refusal lists (empty for any other failure). */
export function refusedItemsOf(err: unknown): RefusedItem[] {
  if (!(err instanceof ApiError)) return [];
  const raw = err.body.items;
  return Array.isArray(raw) ? (raw as RefusedItem[]).filter((i) => typeof i === "object" && i !== null && typeof i.itemNumber === "number") : [];
}

/** The plain-words reason a procedure cannot be completed now. */
export const notReadyWords = (reason: string | null): string =>
  reason === "already_completed" ? "Already completed."
    : reason === "withdrawn" ? "Withdrawn from the plan."
      : reason === "not_accepted" ? "Not accepted yet."
        : reason === "ambiguous_chart_finding" ? "More than one chart finding could match."
          : "Cannot be completed now.";

/** What completing a procedure will do to the chart, in words. */
export const chartEffectWords = (effect: ChartEffect, matches: number): string =>
  effect === "Updated" ? "The chart finding will be marked Completed."
    : effect === "AlreadyCompleted" ? "The chart finding is already Completed."
      : effect === "Ambiguous" ? `${matches} chart findings match; unselect this procedure or correct the diagnosis links.`
        : "No chart change.";
