import { formatMoney } from "../../services/billingApi";
import type { BillingAccount, BillingEntry } from "../../services/billingApi";
import { standingOf, standingText } from "./billingLedgerView";

/** What an entry is about, in words a billing person would use. */
function describe(e: BillingEntry): string {
  if (e.kind === "Charge") return `${e.procedureCode ?? ""} ${e.procedureDescription ?? ""}`.trim();
  if (e.kind === "Payment") return [e.paymentMethodName ?? e.paymentMethod, e.paidByName ? `paid by ${e.paidByName}` : null, e.reason].filter(Boolean).join(" - ");
  if (e.kind === "Refund") return [e.paymentMethodName ?? e.paymentMethod, e.reason].filter(Boolean).join(" - ");
  return e.reason ?? "";
}

interface Props {
  account: BillingAccount;
  /** Set when the signed-in person may change the account: each entry then offers what can be done to it. */
  onReverse?: (entry: BillingEntry) => void;
  onApply?: (entry: BillingEntry) => void;
  /** ALV-007-C02: hand money back, move what a payment pays, correct a payment or adjustment. Offered only to people who may change the account. */
  onRefund?: (entry: BillingEntry) => void;
  onMove?: (entry: BillingEntry) => void;
  onCorrect?: (entry: BillingEntry) => void;
  /** Preview a receipt for a payment (reading is enough). */
  onReceipt?: (entry: BillingEntry) => void;
}

/**
 * STORY-007 and ALV-007-C01: the account's history, oldest first, with the balance owed after each entry. Every number is the server's (the amount as stored, the balance after it, and what the entry stands for now:
 * reversed, paid, open, applied, unapplied); nothing is added up here. Entries are only ever added: there is no edit or delete, so a correction shows as a new adjustment or reversal that says why, and a reversed
 * entry stays visible with the entry that undid it.
 */
export function BillingLedger({ account, onReverse, onApply, onRefund, onMove, onCorrect, onReceipt }: Props) {
  const actions = onReverse !== undefined || onApply !== undefined || onRefund !== undefined || onMove !== undefined || onCorrect !== undefined || onReceipt !== undefined;
  return (
    <table className="alv-bill__table" aria-label="Account history">
      <thead>
        <tr>
          <th scope="col">#</th><th scope="col">Date</th><th scope="col">Entry</th><th scope="col">Details</th><th scope="col" className="alv-bill__num">Amount</th><th scope="col" className="alv-bill__num">Balance owed after</th>
          <th scope="col">Recorded by</th>{actions && <th scope="col">Actions</th>}
        </tr>
      </thead>
      <tbody>
        {account.entries.map((e) => {
          const standing = standingOf(account, e.entryNumber);
          const text = standingText(e, standing);
          const refunded = (standing?.refundedAmount ?? 0) > 0;
          const live = e.kind === "Payment" && !standing?.isReversed;
          const canReverse = onReverse && e.kind !== "Reversal" && e.kind !== "Refund" && !standing?.isReversed && !refunded;
          const canRefund = onRefund && live && (standing?.refundableAmount ?? 0) > 0;
          const canMove = onMove && live && (standing?.appliedAmount ?? 0) > 0;
          const canCorrect = onCorrect && !standing?.isReversed && !refunded && (e.kind === "Payment" || e.kind === "Adjustment");
          const canApply = onApply && e.kind === "Payment" && !standing?.isReversed && (standing?.unappliedAmount ?? 0) > 0;
          return (
            <tr key={e.entryNumber} className={standing?.isReversed ? "alv-bill__reversed" : undefined}>
              <td>{e.entryNumber}</td>
              <td>{new Date(e.recordedAtUtc).toLocaleDateString()}</td>
              <td>{e.kind}</td>
              <td>{describe(e)}{text && <span className="alv-bill__standing"> ({text})</span>}</td>
              <td className="alv-bill__num">{formatMoney(e.amount)}</td>
              <td className="alv-bill__num">{formatMoney(e.balanceAfter)}</td>
              <td>{e.recordedByName ?? "Unknown"}</td>
              {actions && (
                <td>
                  {canApply && <button type="button" className="btn btn-outline-secondary" onClick={() => onApply(e)}>Apply payment {e.entryNumber}</button>}
                  {canRefund && <button type="button" className="btn btn-outline-secondary" onClick={() => onRefund(e)}>Refund payment {e.entryNumber}</button>}
                  {canMove && <button type="button" className="btn btn-outline-secondary" onClick={() => onMove(e)}>Move payment {e.entryNumber}</button>}
                  {canCorrect && <button type="button" className="btn btn-outline-secondary" onClick={() => onCorrect(e)}>Correct entry {e.entryNumber}</button>}
                  {onReceipt && live && <button type="button" className="btn btn-outline-secondary" onClick={() => onReceipt(e)}>Receipt for payment {e.entryNumber}</button>}
                  {canReverse && <button type="button" className="btn btn-outline-secondary" onClick={() => onReverse(e)}>Reverse entry {e.entryNumber}</button>}
                </td>
              )}
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
