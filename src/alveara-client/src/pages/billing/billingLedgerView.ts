import type { AllocationInput, BillingAccount, BillingEntry, BillingEntryStanding } from "../../services/billingApi";
import { formatMoney } from "../../services/billingApi";

export interface OpenCharge {
  entryNumber: number;
  label: string;
  open: number;
}

/** What the server says an entry stands for now (reversed, paid, open, applied, unapplied); an account from an older server has none. */
export const standingOf = (account: BillingAccount, entryNumber: number): BillingEntryStanding | undefined => account.standing?.find((s) => s.entryNumber === entryNumber);

/** The charges that still have something to pay, in the order they were made: what a payment can be applied to. The numbers are the server's. */
export function openChargesOf(account: BillingAccount): OpenCharge[] {
  return account.entries
    .filter((e) => e.kind === "Charge")
    .map((e) => ({ entry: e, standing: standingOf(account, e.entryNumber) }))
    .filter((c) => c.standing && !c.standing.isReversed && (c.standing.openAmount ?? 0) > 0)
    .map((c) => ({ entryNumber: c.entry.entryNumber, label: `Charge ${c.entry.entryNumber}: ${c.entry.procedureCode ?? ""} ${c.entry.procedureDescription ?? ""}`.trim(), open: c.standing!.openAmount! }));
}

/** A short plain-words statement of what an entry stands for now, or null when there is nothing to add. */
export function standingText(entry: BillingEntry, standing: BillingEntryStanding | undefined): string | null {
  if (entry.kind === "Reversal") return entry.reversesEntryNumber ? `Reverses entry ${entry.reversesEntryNumber}` : null;
  if (!standing) return null;
  if (standing.isReversed) return `Reversed by entry ${standing.reversedByEntryNumber}`;
  if (entry.kind === "Charge") return `Paid ${formatMoney(standing.paidAmount ?? 0)}, open ${formatMoney(standing.openAmount ?? 0)}`;
  if (entry.kind === "Payment") return `Applied ${formatMoney(standing.appliedAmount ?? 0)}, unapplied ${formatMoney(standing.unappliedAmount ?? 0)}`;
  return null;
}

/** The allocations a person typed, as the server takes them (a charge with no amount is left out), and which charge each one is, so a refusal can be shown beside the right box. */
export function namedAllocations(charges: OpenCharge[], typed: Record<number, string>): { allocations: AllocationInput[]; order: number[] } {
  const named = charges.filter((c) => (typed[c.entryNumber] ?? "").trim() !== "");
  return { allocations: named.map((c) => ({ chargeEntryNumber: c.entryNumber, amount: Number(typed[c.entryNumber]) })), order: named.map((c) => c.entryNumber) };
}

/** The server's per-allocation messages ("allocations[2]") turned into messages per charge number, using the order the allocations were sent in. */
export function allocationErrorsByCharge(fields: Record<string, string>, order: number[]): Record<number, string> {
  const result: Record<number, string> = {};
  for (const [key, message] of Object.entries(fields)) {
    const match = /^allocations\[(\d+)\]$/.exec(key);
    if (match && order[Number(match[1])] !== undefined) result[order[Number(match[1])]] = message;
  }
  return result;
}
