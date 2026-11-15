/**
 * ALV-N021 A2 (briefing 6.3): typed client for a visit's checkout (Controllers/AppointmentCheckoutController). GET returns every item with whether it applies,
 * its current outcome, the next action and who does it; POST records one decision. Recording sends an `Idempotency-Key`: one key per attempt, reused on a retry
 * of the SAME request, so a retry after a dropped connection never records twice. The server reads the ledger to verify a payment or waiver but never writes it.
 */
import { fetchCsrfToken, request } from "./authApi";

export type CheckoutItemCode = "treatment_ended" | "departure" | "follow_up" | "financial";

export interface CheckoutOutcome {
  id: string | null;
  code: string;
  /** Derived by the server (treatment ended, follow-up booked), not recorded by a person. */
  derived: boolean;
  reason: string | null;
  linkedLedgerEntryNumber: number | null;
  linkedAppointmentId: string | null;
  recordedByName: string | null;
  recordedAtUtc: string | null;
  /** False for "waiver requested": recorded and shown, but the item stays open. */
  resolves: boolean;
}

export interface CheckoutItem {
  code: CheckoutItemCode;
  applies: boolean;
  resolved: boolean;
  outcome: CheckoutOutcome | null;
  nextAction: string | null;
  responsibleRole: string | null;
  /** The outcomes that may be recorded for this item (the server still checks the role of whoever records one). */
  recordableOutcomes: string[];
}

export interface CheckoutReadiness {
  appointmentId: string;
  flowState: string;
  /** Outcomes may be recorded now (treatment has ended and the visit is not completed). */
  open: boolean;
  ready: boolean;
  /** The one line Calendar and Board show, e.g. "Ready for checkout — financial decision required." */
  summary: string;
  items: CheckoutItem[];
  openItemCodes: string[];
}

export interface RecordOutcomeInput {
  outcome: string;
  reason: string | null;
  ledgerEntryNumber: number | null;
}

export const getCheckout = (appointmentId: string, signal?: AbortSignal) => request<CheckoutReadiness>(`/api/appointments/${appointmentId}/checkout`, { signal });

/** One key per attempt (renewed when the request changes, reused on every retry of the same request). */
export const newCheckoutKey = () => `checkout-${crypto.randomUUID()}`;

export async function recordCheckoutOutcome(appointmentId: string, item: string, input: RecordOutcomeInput, idempotencyKey: string): Promise<CheckoutReadiness> {
  const csrfToken = await fetchCsrfToken();
  return request<CheckoutReadiness>(`/api/appointments/${appointmentId}/checkout/${item}`, {
    method: "POST",
    headers: { "Content-Type": "application/json", "X-CSRF-Token": csrfToken, "Idempotency-Key": idempotencyKey },
    body: JSON.stringify(input),
  });
}

export const CHECKOUT_ITEM_LABELS: Record<CheckoutItemCode, string> = {
  treatment_ended: "Treatment ended", departure: "Patient has left", follow_up: "Follow-up", financial: "Payment",
};

export const CHECKOUT_OUTCOME_LABELS: Record<string, string> = {
  treatment_ended: "Treatment ended", booked: "Follow-up booked", patient_left: "Patient has left",
  not_clinically_required: "Not clinically required", patient_declined: "Patient declined", deferred: "Deferred",
  payment_recorded: "Payment recorded", waived: "Waived", payment_plan: "Payment plan", insurance_to_bill: "Insurance to bill",
  send_statement: "Send a statement", waiver_requested: "Waiver requested (does not resolve)",
};

/** Outcomes the server refuses without a reason (CheckoutOutcomes.NeedReason) or without a ledger entry number (NeedLedgerEntry). */
export const NEEDS_REASON = new Set(["deferred", "waiver_requested"]);
export const NEEDS_LEDGER_ENTRY = new Set(["payment_recorded", "waived"]);
export const CHECKOUT_REASON_MAX = 500;
