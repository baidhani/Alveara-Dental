/**
 * ALV-007-C02: typed client for refunds, moving what a payment pays, corrections, the timeline, aging, statements and receipts (Controllers/BillingStatementsController).
 *
 * - Refunds, moves and corrections echo the account's `rowVersion` it read (a stale one is the shared 409 concurrency conflict) and carry an idempotency key; the same key returns the result already made.
 * - A statement or receipt is previewed, and only that preview can be issued: the preview's `previewToken` binds the content that was reviewed. If the content would now differ, issuing is refused as
 *   `document_preview_stale` and nothing is issued. An issued document stores no amounts; it is rebuilt exactly as issued every time it is shown, and what has happened since is `currentStatus`, apart from it.
 * - Every amount in a document is two-place text, exactly as the server built it: the screen shows it and never adds anything up.
 */
import { request, requestWithCsrf } from "./authApi";
import type { AllocationInput, BillingAccount } from "./billingApi";

export interface TimelineEvent {
  atUtc: string;
  type: string;
  number: number | null;
  summary: string;
  amount: number | null;
  source: string | null;
  actorName: string | null;
  /** A charge: the plan procedure it bills and, when it was completed in the product, the completed-procedure record behind it (the link back to the source). */
  planItemId?: string | null;
  completedProcedureId?: string | null;
}

export interface OpenCharge { number: number; procedureCode: string | null; procedureDescription: string | null; amount: string; paid: string; open: string; serviceDateUtc: string; ageDays: number; bucket: string }
export interface Aging { current: string; days31To60: string; days61To90: string; over90: string }
export interface AgingView { asOfUtc: string; currency: string; buckets: Aging; openCharges: OpenCharge[] }

/** The practice as the document shows it, kept in the issued document so a reprint shows it as it was on the day. */
export interface DocumentPractice { name: string; phone: string | null; addressLine: string | null }
export interface DocumentParty { patientId: string; name: string; isSelf: boolean }
export interface DocumentLine {
  number: number; kind: string; amount: string; balanceAfter: string; procedureCode: string | null; procedureDescription: string | null; method: string | null; reason: string | null; recordedAtUtc: string;
  reversesNumber: number | null; refundsNumber: number | null; replacesNumber: number | null;
}
export interface StatementContent {
  documentType: "Statement"; templateVersion: number; asOfUtc: string; currency: string; patient: DocumentParty; addressee: DocumentParty; lines: DocumentLine[]; openCharges: OpenCharge[];
  unapplied: { paymentNumber: number; amount: string }[]; aging: Aging; balance: string; openChargeTotal: string; unappliedTotal: string; practice?: DocumentPractice | null;
}
export interface ReceiptContent {
  documentType: "Receipt"; templateVersion: number; asOfUtc: string; currency: string; patient: DocumentParty; receivedFrom: DocumentParty; paymentNumber: number; amount: string; method: string | null;
  receivedAtUtc: string; applications: { chargeNumber: number; procedureCode: string | null; procedureDescription: string | null; amount: string }[]; unapplied: string; refundedToDate: string; balanceOwed: string; practice?: DocumentPractice | null;
}
export interface CutOffs { entryNumber: number; allocationNumber: number; releaseNumber: number; responsibilitySequence: number; asOfUtc: string }
export interface DocumentPreview { documentType: "Statement" | "Receipt"; cutOffs: CutOffs; templateVersion: number; contentHash: string; previewToken: string; statement: StatementContent | null; receipt: ReceiptContent | null }
export interface IssuedDocument {
  id: string; documentType: "Statement" | "Receipt"; documentNumber: number; paymentEntryNumber: number | null; cutOffs: CutOffs; templateVersion: number; contentHash: string; issuedAtUtc: string; issuedByName: string | null;
}
export interface IssuedDocumentDetail { document: IssuedDocument; statement: StatementContent | null; receipt: ReceiptContent | null; currentStatus: string[] }

export interface RefundInput { paymentEntryNumber: number; amount: number | null; method: string; reason: string }
export interface MoveInput { paymentEntryNumber: number; releases: AllocationInput[]; allocations: AllocationInput[]; reason: string }
export interface CorrectionInput { entryNumber: number; reason: string; amount: number | null; method?: string; note?: string | null }

const base = (patientId: string) => `/api/patients/${patientId}/billing`;

export const refundPayment = (account: BillingAccount, idempotencyKey: string, input: RefundInput) =>
  requestWithCsrf<BillingAccount>(`${base(account.patientId)}/refunds`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

export const moveAllocations = (account: BillingAccount, idempotencyKey: string, input: MoveInput) =>
  requestWithCsrf<BillingAccount>(`${base(account.patientId)}/allocations/move`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

export const correctEntry = (account: BillingAccount, idempotencyKey: string, input: CorrectionInput) =>
  requestWithCsrf<BillingAccount>(`${base(account.patientId)}/corrections`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

export const getTimeline = (patientId: string, signal?: AbortSignal) => request<TimelineEvent[]>(`${base(patientId)}/timeline`, { signal });
export const getAging = (patientId: string, signal?: AbortSignal) => request<AgingView>(`${base(patientId)}/aging`, { signal });

export const previewDocument = (patientId: string, type: "Statement" | "Receipt", paymentEntryNumber?: number, signal?: AbortSignal) =>
  request<DocumentPreview>(`${base(patientId)}/documents/preview?type=${type}${paymentEntryNumber !== undefined ? `&paymentEntryNumber=${paymentEntryNumber}` : ""}`, { signal });

export const issueDocument = (patientId: string, idempotencyKey: string, previewToken: string) =>
  requestWithCsrf<IssuedDocument>(`${base(patientId)}/documents`, "POST", { idempotencyKey, previewToken });

export const listDocuments = (patientId: string, signal?: AbortSignal) => request<IssuedDocument[]>(`${base(patientId)}/documents`, { signal });
export const getDocument = (patientId: string, documentId: string, signal?: AbortSignal) => request<IssuedDocumentDetail>(`${base(patientId)}/documents/${documentId}`, { signal });
export const documentCsvUrl = (patientId: string, documentId: string) => `${base(patientId)}/documents/${documentId}/csv`;

/** Whether a response really is the shape asked for, so a screen never draws from a body that is something else. */
export const isTimeline = (x: unknown): x is TimelineEvent[] => Array.isArray(x) && x.every((e) => typeof e === "object" && e !== null && typeof (e as TimelineEvent).type === "string");
export const isAgingView = (x: unknown): x is AgingView => typeof x === "object" && x !== null && typeof (x as AgingView).buckets === "object" && Array.isArray((x as AgingView).openCharges);
export const isDocumentPreview = (x: unknown): x is DocumentPreview => typeof x === "object" && x !== null && typeof (x as DocumentPreview).previewToken === "string";
export const isDocumentList = (x: unknown): x is IssuedDocument[] => Array.isArray(x) && x.every((d) => typeof d === "object" && d !== null && typeof (d as IssuedDocument).documentNumber === "number");
export const isDocumentDetail = (x: unknown): x is IssuedDocumentDetail => typeof x === "object" && x !== null && typeof (x as IssuedDocumentDetail).document === "object" && Array.isArray((x as IssuedDocumentDetail).currentStatus);
