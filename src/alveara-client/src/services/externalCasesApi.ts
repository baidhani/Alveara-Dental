/**
 * ALV-009-C02: typed client for referral and dental-laboratory cases, the outside parties they go to, and a case's linked documents (Controllers/ExternalCasesController, docs/REFERRALS_AND_LAB_CASES.md).
 *
 * - A create carries an `Idempotency-Key` (the same key for a retry of the same attempt; a new one when the form changes).
 * - Every change carries the `rowVersion` the screen was showing; a stale one is 409 `case_changed` (`party_changed` for a party) and nothing is stored (`isStaleCaseChange`).
 * - Documents are linked by id, never copied; a link whose document went bad comes back flagged `broken` with the reason.
 * - Every refusal has a stable `error` code (see `caseErrorMessage`).
 */
import { ApiError, fetchCsrfToken, request, requestWithCsrf } from "./authApi";

export type CaseKind = "Referral" | "Lab";
export type CaseStatus = "Draft" | "Sent" | "InProgress" | "Received" | "Completed" | "Cancelled";
export type PartyKind = "ReferralProvider" | "LabVendor";

export const routeOf = (kind: CaseKind) => (kind === "Lab" ? "lab-cases" : "referrals");
export const kindOfRoute = (route: string | undefined): CaseKind | null => (route === "lab-cases" ? "Lab" : route === "referrals" ? "Referral" : null);

export interface ExternalCase {
  kind: CaseKind; id: string; patientId: string; partyId: string; partyName: string; partyActive: boolean;
  /** The referral's reason or the lab case's title. */
  summary: string; status: CaseStatus; nextStatuses: CaseStatus[];
  sentOn: string | null; receivedOn: string | null; dueOn: string | null; overdue: boolean;
  encounterId: string | null; toothKey: string | null; treatmentPlanItemId: string | null; material: string | null; shade: string | null; notes: string | null;
  closureNote: string | null; closedAtUtc: string | null; rowVersion: string; createdAtUtc: string; updatedAtUtc: string;
  /** The open follow-up task chasing this case in the work queue, if any. */
  openChaseTaskId: string | null;
}
export interface ExternalCaseResult { case: ExternalCase; replayed: boolean }
export interface CaseListItem { case: ExternalCase; patientName: string }
export interface CaseCounts { open: number; overdue: number; closed: number }
export interface CasePage { items: CaseListItem[]; total: number; page: number; pageSize: number; counts: CaseCounts }
export interface CaseQuery { kind?: CaseKind | ""; status?: string; overdue?: boolean; patientId?: string; partyId?: string; page?: number; pageSize?: number }

export interface ExternalParty { id: string; kind: PartyKind; name: string; contactDetails: string | null; isActive: boolean; rowVersion: string; createdAtUtc: string; updatedAtUtc: string }

export interface CaseDocument {
  documentId: string; fileName: string; category: string; documentDate: string; description: string | null; status: string; broken: boolean; brokenReason: string | null; linkedAtUtc: string; linkedByUserId: string;
}
export interface CaseEvent { id: string; kind: string; fromStatus: string | null; toStatus: string | null; documentId: string | null; actorUserId: string; actorName: string | null; occurredAtUtc: string; note: string | null }
export interface PatientCaseTimelineEntry {
  caseKind: CaseKind; caseId: string; caseSummary: string; caseStatus: CaseStatus; eventKind: string; fromStatus: string | null; toStatus: string | null; documentId: string | null;
  actorUserId: string; actorName: string | null; occurredAtUtc: string; note: string | null;
}

export interface ReferralInput { partyId: string; reason: string; dueOn: string | null; encounterId: string | null; notes: string | null }
export interface LabInput { partyId: string; title: string; dueOn: string | null; toothKey: string | null; treatmentPlanItemId: string | null; material: string | null; shade: string | null; notes: string | null }

export const newCaseKey = (): string => (crypto.randomUUID ? crypto.randomUUID() : `k-${Date.now()}-${Math.random().toString(16).slice(2)}`);

export const REASON_MAX = 500;
export const TITLE_MAX = 200;
export const NOTE_MAX = 1000;
export const MATERIAL_MAX = 100;
export const SHADE_MAX = 50;
export const PARTY_NAME_MAX = 200;
export const CONTACT_MAX = 200;

async function write<T>(url: string, method: "PUT" | "POST", body: unknown, key?: string): Promise<T> {
  const csrf = await fetchCsrfToken();
  const headers: Record<string, string> = { "Content-Type": "application/json", "X-CSRF-Token": csrf };
  if (key) headers["Idempotency-Key"] = key;
  return request<T>(url, { method, headers, body: JSON.stringify(body) });
}

export function listCases(q: CaseQuery, signal?: AbortSignal) {
  const params = new URLSearchParams();
  if (q.kind) params.set("kind", q.kind);
  if (q.status && q.status !== "open") params.set("status", q.status);
  if (q.overdue) params.set("overdue", "true");
  if (q.patientId) params.set("patientId", q.patientId);
  if (q.partyId) params.set("partyId", q.partyId);
  if (q.page && q.page > 1) params.set("page", String(q.page));
  if (q.pageSize) params.set("pageSize", String(q.pageSize));
  const text = params.toString();
  return request<CasePage>(`/api/external-cases${text ? `?${text}` : ""}`, { signal });
}
export const getCase = (kind: CaseKind, id: string, signal?: AbortSignal) => request<ExternalCase>(`/api/${routeOf(kind)}/${id}`, { signal });
export const getCaseHistory = (kind: CaseKind, id: string, signal?: AbortSignal) => request<CaseEvent[]>(`/api/${routeOf(kind)}/${id}/history`, { signal });
export const getCaseDocuments = (kind: CaseKind, id: string, signal?: AbortSignal) => request<CaseDocument[]>(`/api/${routeOf(kind)}/${id}/documents`, { signal });
export const getPatientCaseTimeline = (patientId: string, signal?: AbortSignal) => request<PatientCaseTimelineEntry[]>(`/api/patients/${patientId}/external-cases/timeline`, { signal });

export const createReferral = (patientId: string, input: ReferralInput, key: string) => write<ExternalCaseResult>(`/api/patients/${patientId}/referrals`, "POST", input, key);
export const createLabCase = (patientId: string, input: LabInput, key: string) => write<ExternalCaseResult>(`/api/patients/${patientId}/lab-cases`, "POST", input, key);
export const updateReferral = (id: string, input: ReferralInput, rowVersion: string) => write<ExternalCaseResult>(`/api/referrals/${id}`, "PUT", { ...input, rowVersion });
export const updateLabCase = (id: string, input: LabInput, rowVersion: string) => write<ExternalCaseResult>(`/api/lab-cases/${id}`, "PUT", { ...input, rowVersion });
export const changeCaseStatus = (kind: CaseKind, id: string, toStatus: CaseStatus, note: string | null, date: string | null, rowVersion: string) =>
  write<ExternalCaseResult>(`/api/${routeOf(kind)}/${id}/status`, "POST", { toStatus, note, date, rowVersion });
export const linkCaseDocument = (kind: CaseKind, id: string, documentId: string) => requestWithCsrf<{ documentId: string; linked: boolean; replayed: boolean }>(`/api/${routeOf(kind)}/${id}/documents/${documentId}`, "PUT");
export const unlinkCaseDocument = (kind: CaseKind, id: string, documentId: string) => requestWithCsrf<{ documentId: string; linked: boolean; replayed: boolean }>(`/api/${routeOf(kind)}/${id}/documents/${documentId}`, "DELETE");

export const listParties = (kind?: PartyKind, includeInactive = false, signal?: AbortSignal) => {
  const params = new URLSearchParams();
  if (kind) params.set("kind", kind);
  if (includeInactive) params.set("includeInactive", "true");
  const text = params.toString();
  return request<ExternalParty[]>(`/api/external-parties${text ? `?${text}` : ""}`, { signal });
};
export const createParty = (kind: PartyKind, name: string, contactDetails: string | null) => requestWithCsrf<ExternalParty>("/api/external-parties", "POST", { kind, name, contactDetails });
export const updateParty = (id: string, name: string, contactDetails: string | null, rowVersion: string) => requestWithCsrf<ExternalParty>(`/api/external-parties/${id}`, "PUT", { name, contactDetails, rowVersion });
export const setPartyActive = (id: string, active: boolean, rowVersion: string) => requestWithCsrf<ExternalParty>(`/api/external-parties/${id}/active`, "POST", { active, rowVersion });

const MESSAGES: Record<string, string> = {
  case_changed: "Someone else changed this case. Reload it to see the current version, then try again.",
  party_changed: "Someone else changed this party. Reload the list, then try again.",
  case_closed: "That case is already finished and cannot be changed.",
  case_not_found: "That case was not found.",
  status_invalid: "The case cannot move to that status from where it is now. Reload to see its current status.",
  note_required: "Write the reason or note this move needs.",
  note_invalid: "The note can be up to 1,000 characters of plain text.",
  date_invalid: "The date cannot be in the future or before the year 2000.",
  date_before_sent: "It cannot have come back before it was sent.",
  due_out_of_range: "The due date is too far in the past or in the future. Check the date.",
  party_required: "Choose the outside party first.",
  party_not_found: "That outside party was not found. Add it to the list first.",
  party_kind_invalid: "That party is the wrong kind for this case.",
  party_inactive: "That outside party is no longer active. Choose another.",
  party_locked: "The case has been sent, so it keeps its outside party.",
  party_exists: "A party with that name already exists.",
  reason_invalid: "Write the reason for the referral (up to 500 characters) on one line.",
  title_invalid: "Write what is being made (up to 200 characters) on one line.",
  tooth_invalid: "Choose a tooth by its two-digit number.",
  encounter_invalid: "Choose a visit of this patient.",
  plan_item_invalid: "Choose a treatment of this patient.",
  document_invalid: "Choose a document of this patient.",
  document_inactive: "That document is inactive. Make it active in the document library first.",
  key_reused: "That attempt was already used for a different case. Start the form again.",
  no_change: "Nothing was changed.",
  row_version_required: "Reload and try again.",
  permission_denied: "You do not have permission to do that.",
};

/** A message safe to show for a refused case, party or document request. */
export function caseErrorMessage(err: unknown): string {
  if (err instanceof ApiError) return MESSAGES[err.code] ?? err.message ?? "That did not work. Try again.";
  return "That did not work. Check your connection and try again.";
}

export const isStaleCaseChange = (err: unknown) => err instanceof ApiError && ["case_changed", "case_closed", "status_invalid", "party_changed", "row_version_required"].includes(err.code);
