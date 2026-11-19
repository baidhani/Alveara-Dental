import { formatMoney } from "../../services/billingApi";
import type { IssuedDocument } from "../../services/billingStatementsApi";

/** ALV-N024: what is true of an issued document now, as short notes (never part of the document). */
export function documentStatus(d: IssuedDocument): string[] {
  const notes: string[] = [];
  if (d.supersededByNumber) notes.push(`Superseded by receipt ${d.supersededByNumber}`);
  if (d.replacesNumber) notes.push(`Replaces receipt ${d.replacesNumber}`);
  if (d.paymentReversed) notes.push("Payment reversed");
  if (d.paymentRefunded && d.paymentRefunded > 0) notes.push(`${formatMoney(d.paymentRefunded)} refunded`);
  if (d.legacyDuplicate) notes.push("Multiple legacy receipts exist for this payment");
  return notes;
}
