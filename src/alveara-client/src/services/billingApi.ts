/**
 * STORY-007 and ALV-007-C01: typed client for a patient's billing account (Controllers/BillingController).
 *
 * - The balance owed is computed by the server from the account's entries every time; the screen shows it and never adds anything up itself.
 * - A charge names a completed plan procedure and nothing else: the amount is the fee the plan copied, never typed.
 * - A payment, an application of a payment, an adjustment and a reversal echo the account's `rowVersion` it read (a stale one is the shared 409 concurrency conflict). Every change carries an idempotency key; the
 *   same key returns the entry already made.
 * - Amounts are US dollars as numbers. An entry's `amount` is signed as stored (a payment is negative); `balanceAfter` is the balance owed once it was recorded. An entry is shown exactly as recorded and never
 *   changes; what it stands for now (reversed, paid, open, applied, unapplied) is in `standing`, which is derived from the history and does.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

/** The four kinds of payment the ledger reports on. The practice's own methods each map to one of these. */
export const PAYMENT_METHODS = ["Cash", "Check", "Card", "Other"] as const;

export interface BillingEntry {
  entryNumber: number;
  kind: "Charge" | "Payment" | "Adjustment" | "Reversal" | "Refund";
  amount: number;
  balanceAfter: number;
  planItemId: string | null;
  procedureCode: string | null;
  procedureDescription: string | null;
  paymentMethod: string | null;
  reason: string | null;
  recordedAtUtc: string;
  recordedByName: string | null;
  /** Reversal only: the number of the entry it undoes. */
  reversesEntryNumber?: number | null;
  /** Payment only: the practice's name for the method used. */
  paymentMethodName?: string | null;
  /** Payment only: who paid, when that is not the patient. */
  paidByName?: string | null;
  /** Refund only: the number of the payment it hands back (ALV-007-C02). */
  refundsEntryNumber?: number | null;
  /** A payment or adjustment made as the replacement of a corrected entry: the number of the entry it replaces. */
  replacesEntryNumber?: number | null;
}

/** What an entry stands for now. Parallel to the entries. */
export interface BillingEntryStanding {
  entryNumber: number;
  isReversed: boolean;
  reversedByEntryNumber: number | null;
  paidAmount: number | null;
  openAmount: number | null;
  appliedAmount: number | null;
  unappliedAmount: number | null;
  /** Payment only: what has been refunded of it, and what can still be (zero once it is reversed). */
  refundedAmount?: number | null;
  refundableAmount?: number | null;
}

export interface BillingAllocation {
  allocationNumber: number;
  paymentEntryNumber: number;
  chargeEntryNumber: number;
  amount: number;
  /** True when the payment has been reversed, so the allocation no longer counts (it is kept, not deleted). */
  released: boolean;
  allocatedAtUtc: string;
  allocatedByName: string | null;
  /** How much of this allocation has been given back by a refund or a move; the allocation itself is never changed. */
  releasedAmount?: number | null;
}

export interface BillingAccount {
  patientId: string;
  hasAccount: boolean;
  balanceOwed: number;
  currency: string;
  /** Null until something has been billed; a payment, an application, an adjustment or a reversal must send it back. */
  rowVersion: string | null;
  entries: BillingEntry[];
  allocations?: BillingAllocation[];
  /** What payments hold that is not applied to any charge. */
  unappliedTotal?: number;
  /** What is left to pay on charges that have not been reversed. */
  openChargeTotal?: number;
  standing?: BillingEntryStanding[];
}

export interface BillableProcedure {
  planItemId: string;
  procedureCode: string;
  description: string;
  toothKey: string | null;
  surface: string | null;
  fee: number;
  currency: string;
}

export interface PaymentMethod {
  id: string;
  name: string;
  kind: string;
  isBuiltIn: boolean;
  isActive: boolean;
  rowVersion: string;
}

export interface ResponsibilityItem {
  sequence: number;
  responsiblePatientId: string;
  responsibleName: string;
  isSelf: boolean;
  effectiveFrom: string;
  reason: string | null;
  setAtUtc: string;
  setByName: string | null;
}

/** Who is responsible for the account now: Recorded (billing recorded it), RegisteredGuarantor (the patient's registered guarantor) or Self. */
export interface Responsibility {
  patientId: string;
  responsiblePatientId: string;
  responsibleName: string;
  isSelf: boolean;
  source: "Recorded" | "RegisteredGuarantor" | "Self";
  effectiveFrom: string | null;
  rowVersion: string | null;
  history: ResponsibilityItem[];
}

export interface GuarantorAccount {
  patientId: string;
  patientName: string;
  isGuarantor: boolean;
  balanceOwed: number;
  openChargeTotal: number;
  unappliedTotal: number;
}

export interface GuarantorSummary {
  guarantorPatientId: string;
  guarantorName: string;
  currency: string;
  totalOwed: number;
  accounts: GuarantorAccount[];
}

export interface AllocationInput { chargeEntryNumber: number; amount: number }
export interface PaymentInput { amount: number | null; method: string; note: string | null; allocations?: AllocationInput[]; paidByPatientId?: string | null }
export interface AdjustmentInput { amount: number | null; reason: string }

export const getBillingAccount = (patientId: string, signal?: AbortSignal) => request<BillingAccount>(`/api/patients/${patientId}/billing`, { signal });

export const listBillableProcedures = (patientId: string, signal?: AbortSignal) => request<BillableProcedure[]>(`/api/patients/${patientId}/billing/billable-procedures`, { signal });

export const recordCharge = (patientId: string, planItemId: string, idempotencyKey: string) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${patientId}/billing/charges`, "POST", { planItemId, idempotencyKey });

export const recordPayment = (account: BillingAccount, idempotencyKey: string, input: PaymentInput) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${account.patientId}/billing/payments`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

export const applyPayment = (account: BillingAccount, idempotencyKey: string, paymentEntryNumber: number, allocations?: AllocationInput[]) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${account.patientId}/billing/payments/apply`, "POST", { idempotencyKey, rowVersion: account.rowVersion, paymentEntryNumber, ...(allocations ? { allocations } : {}) });

export const recordAdjustment = (account: BillingAccount, idempotencyKey: string, input: AdjustmentInput) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${account.patientId}/billing/adjustments`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

export const reverseEntry = (account: BillingAccount, idempotencyKey: string, entryNumber: number, reason: string) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${account.patientId}/billing/reversals`, "POST", { idempotencyKey, rowVersion: account.rowVersion, entryNumber, reason });

export const listPaymentMethods = (includeInactive = false, signal?: AbortSignal) => request<PaymentMethod[]>(`/api/billing/payment-methods?includeInactive=${includeInactive}`, { signal });

export const addPaymentMethod = (name: string, kind: string) => requestWithCsrf<PaymentMethod>("/api/billing/payment-methods", "POST", { name, kind });

export const setPaymentMethodActive = (method: PaymentMethod, active: boolean) =>
  requestWithCsrf<PaymentMethod>(`/api/billing/payment-methods/${method.id}/active`, "POST", { active, rowVersion: method.rowVersion });

export const getResponsibility = (patientId: string, signal?: AbortSignal) => request<Responsibility>(`/api/patients/${patientId}/billing/responsibility`, { signal });

export const recordResponsibility = (patientId: string, rowVersion: string | null, responsiblePatientId: string, reason: string | null) =>
  requestWithCsrf<Responsibility>(`/api/patients/${patientId}/billing/responsibility`, "POST", { responsiblePatientId, reason, rowVersion });

export const getGuarantorSummary = (patientId: string, signal?: AbortSignal) => request<GuarantorSummary>(`/api/patients/${patientId}/billing/guarantor-summary`, { signal });

/** The server's message for each field it refused, keyed by field name ("amount", "method", "reason", "allocations[0]"...). */
export function billingFieldErrorsOf(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError)) return {};
  const raw = err.body.fieldErrors;
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return {};
  return Object.fromEntries(Object.entries(raw as Record<string, unknown>).filter((e): e is [string, string] => typeof e[1] === "string"));
}

/** "$60.00", or "-$60.00" for a payment: two decimals always, so an amount is never misread. */
export const formatMoney = (amount: number) => `${amount < 0 ? "-" : ""}$${Math.abs(amount).toFixed(2)}`;

/** Whether a response really is the shape asked for, so a screen never draws from a body that is something else. */
export const isResponsibility = (x: unknown): x is Responsibility => typeof x === "object" && x !== null && typeof (x as Responsibility).source === "string" && Array.isArray((x as Responsibility).history);
export const isGuarantorSummary = (x: unknown): x is GuarantorSummary => typeof x === "object" && x !== null && Array.isArray((x as GuarantorSummary).accounts);
export const isPaymentMethodList = (x: unknown): x is PaymentMethod[] => Array.isArray(x) && x.every((m) => typeof m === "object" && m !== null && typeof (m as PaymentMethod).name === "string");
