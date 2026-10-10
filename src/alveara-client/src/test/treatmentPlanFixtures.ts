import { vi } from "vitest";
import type { Diagnosis } from "../services/diagnosisApi";
import type { PatientDetail } from "../services/patientsApi";
import type { PlanItem, PlanningProcedure, TreatmentPlan } from "../services/treatmentPlansApi";

/**
 * Shared by the treatment plan screen tests: a patient, plans, procedures and diagnoses as the API returns them, and a scripted `fetch` that answers the plan, diagnosis and catalog calls from them and records
 * every call. `respond` lets a test answer one route its own way (a refusal, a conflict). Everything here is fake data.
 */
export const P = "aaaaaaaa-0000-0000-0000-000000000001";
export const patient = { id: P } as PatientDetail;
export const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });

export const diagnosis = (over: Partial<Diagnosis> = {}): Diagnosis => ({
  id: "dx1", patientId: P, encounterId: "e1", encounterAtUtc: "2030-01-01T10:00:00Z", label: "Chronic periodontitis", toothKey: null, notes: null, treatmentPlanReference: null, treatmentPlanReferenceState: null,
  status: "Active", recordedByName: "Dr. Okafor", recordedAtUtc: "2030-01-01T10:00:00Z", updatedByName: null, updatedAtUtc: null, withdrawnByName: null, withdrawnAtUtc: null, withdrawnReason: null, rowVersion: "D1", ...over,
});
export const procedure = (over: Partial<PlanningProcedure> = {}): PlanningProcedure => ({
  procedureId: "pr1", versionId: "v1", versionNumber: 1, codeSystem: "Local", code: "PERIO-1", description: "Periodontal maintenance", category: "Periodontic", scope: "WholeMouth", dentition: "Both", fee: 120, currency: "USD", ...over,
});
export const item = (over: Partial<PlanItem> = {}): PlanItem => ({
  id: "it1", itemNumber: 1, diagnosisId: "dx1", diagnosisLabel: "Chronic periodontitis", procedureId: "pr1", procedureVersionId: "v1", procedureVersionNumber: 1, procedureCodeSystem: "Local", procedureCode: "PERIO-1",
  procedureDescription: "Periodontal maintenance", toothKey: null, surface: null, fee: 120, currency: "USD", isWithdrawn: false, withdrawnReason: null, withdrawnAtUtc: null, withdrawnByName: null,
  createdAtUtc: "2030-01-02T10:00:00Z", createdByName: "Dr. Okafor", phaseNumber: null, acceptance: "Pending", scheduling: "Unscheduled", completion: "NotStarted", plannedDate: null,
  decisionReason: null, decidedByName: null, recordedByName: null, recordedAtUtc: null, decisionRevision: null, ...over,
});
export const plan = (over: Partial<TreatmentPlan> = {}): TreatmentPlan => ({
  id: "pl1", patientId: P, title: "Gum health plan", status: "Proposed", activeItemCount: 1, estimateTotal: 120, currency: "USD",
  estimateLabel: "Practice fee estimate: the practice's catalog fee when each item was proposed. It is not an insurance estimate and not a guaranteed patient cost.", items: [item()],
  createdAtUtc: "2030-01-02T10:00:00Z", createdByName: "Dr. Okafor", updatedAtUtc: null, updatedByName: null, withdrawnReason: null, withdrawnAtUtc: null, withdrawnByName: null, rowVersion: "R1",
  currentRevision: 1, referenceCode: null, clinicalRationale: null, patientExplanation: null, phases: [], acceptanceStatus: "Pending", acceptedEstimateTotal: 0, ...over,
});

export interface Api {
  plans: TreatmentPlan[];
  diagnoses: Diagnosis[];
  procedures: PlanningProcedure[];
  calls: { url: string; method: string; body: Record<string, unknown> | null }[];
  respond: Record<string, (body: Record<string, unknown> | null) => Response>;
}

export function stubApi(over: Partial<Api> = {}): Api {
  const api: Api = { plans: [plan()], diagnoses: [diagnosis()], procedures: [procedure(), procedure({ procedureId: "pr2", code: "FILL-1", description: "Filling", scope: "ToothSurface", fee: 80 })], calls: [], respond: {}, ...over };
  vi.stubGlobal("fetch", vi.fn().mockImplementation((url: RequestInfo | URL, init?: RequestInit) => {
    const u = String(url);
    const method = init?.method ?? "GET";
    const body = init?.body ? (JSON.parse(init.body as string) as Record<string, unknown>) : null;
    api.calls.push({ url: u, method, body });
    for (const [fragment, handler] of Object.entries(api.respond)) if (u.includes(fragment)) return Promise.resolve(handler(body));
    if (u.includes("/api/auth/csrf-token")) return Promise.resolve(json({ token: "csrf" }));
    if (u.includes("/api/procedures/active")) return Promise.resolve(json(api.procedures));
    if (u.includes("/diagnoses")) return Promise.resolve(json({ patientId: P, diagnoses: api.diagnoses }));
    if (u.includes("/treatment-plans") && method === "GET") return Promise.resolve(json({ patientId: P, plans: api.plans }));
    if (method === "POST") return Promise.resolve(json(api.plans[0] ?? plan()));
    return Promise.resolve(json({}));
  }));
  return api;
}

