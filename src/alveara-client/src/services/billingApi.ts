/**
 * STORY-007: typed client for a patient's billing account (Controllers/BillingController).
 *
 * - The balance owed is computed by the server from the account's entries every time; the screen shows it and never adds anything up itself.
 * - A charge names a completed plan procedure and nothing else: the amount is the fee the plan copied, never typed.
 * - A payment or adjustment echoes the account's `rowVersion` it read (a stale one is the shared 409 concurrency conflict). Every change carries an idempotency key; the same key returns the entry
 *   already made.
 * - Amounts are US dollars as numbers. An entry's `amount` is signed as stored (a payment is negative); `balanceAfter` is the balance owed once it was recorded.
 */
import { ApiError, request, requestWithCsrf } from "./authApi";

export const PAYMENT_METHODS = ["Cash", "Check", "Card", "Other"] as const;

export interface BillingEntry {
  entryNumber: number;
  kind: "Charge" | "Payment" | "Adjustment";
  amount: number;
  balanceAfter: number;
  planItemId: string | null;
  procedureCode: string | null;
  procedureDescription: string | null;
  paymentMethod: string | null;
  reason: string | null;
  recordedAtUtc: string;
  recordedByName: string | null;
}

export interface BillingAccount {
  patientId: string;
  hasAccount: boolean;
  balanceOwed: number;
  currency: string;
  /** Null until something has been billed; a payment or adjustment must send it back. */
  rowVersion: string | null;
  entries: BillingEntry[];
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

export interface PaymentInput { amount: number | null; method: string; note: string | null }
export interface AdjustmentInput { amount: number | null; reason: string }

export const getBillingAccount = (patientId: string, signal?: AbortSignal) => request<BillingAccount>(`/api/patients/${patientId}/billing`, { signal });

export const listBillableProcedures = (patientId: string, signal?: AbortSignal) => request<BillableProcedure[]>(`/api/patients/${patientId}/billing/billable-procedures`, { signal });

export const recordCharge = (patientId: string, planItemId: string, idempotencyKey: string) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${patientId}/billing/charges`, "POST", { planItemId, idempotencyKey });

export const recordPayment = (account: BillingAccount, idempotencyKey: string, input: PaymentInput) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${account.patientId}/billing/payments`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

export const recordAdjustment = (account: BillingAccount, idempotencyKey: string, input: AdjustmentInput) =>
  requestWithCsrf<BillingAccount>(`/api/patients/${account.patientId}/billing/adjustments`, "POST", { idempotencyKey, rowVersion: account.rowVersion, ...input });

/** The server's message for each field it refused, keyed by field name ("amount", "method", "reason"...). */
export function billingFieldErrorsOf(err: unknown): Record<string, string> {
  if (!(err instanceof ApiError)) return {};
  const raw = err.body.fieldErrors;
  if (typeof raw !== "object" || raw === null || Array.isArray(raw)) return {};
  return Object.fromEntries(Object.entries(raw as Record<string, unknown>).filter((e): e is [string, string] => typeof e[1] === "string"));
}

/** "$60.00", or "-$60.00" for a payment: two decimals always, so an amount is never misread. */
export const formatMoney = (amount: number) => `${amount < 0 ? "-" : ""}$${Math.abs(amount).toFixed(2)}`;
