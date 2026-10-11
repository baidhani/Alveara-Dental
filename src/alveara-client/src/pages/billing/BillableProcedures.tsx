import { useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { formatMoney, recordCharge } from "../../services/billingApi";
import type { BillableProcedure, BillingAccount } from "../../services/billingApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  patientId: string;
  procedures: BillableProcedure[];
  /** Whether the signed-in person may bill. It only decides whether the buttons are drawn (the server checks every call). */
  canWrite: boolean;
  onBilled: (account: BillingAccount, text: string) => void;
}

/**
 * STORY-007: the completed procedures not yet billed, with the fee the treatment plan copied when each was proposed. Billing one records a charge for exactly that fee: there is no amount to type. Each
 * procedure keeps its own save key until it succeeds, so a double click or a retry cannot bill it twice.
 */
export function BillableProcedures({ patientId, procedures, canWrite, onBilled }: Props) {
  const [busy, setBusy] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const keys = useRef(new Map<string, string>());

  async function bill(p: BillableProcedure) {
    if (!keys.current.has(p.planItemId)) keys.current.set(p.planItemId, newKey());
    setBusy(p.planItemId);
    setMessage(null);
    try {
      const account = await recordCharge(patientId, p.planItemId, keys.current.get(p.planItemId)!);
      keys.current.delete(p.planItemId);
      onBilled(account, `${p.procedureCode} billed for ${formatMoney(p.fee)}. Balance owed is now ${formatMoney(account.balanceOwed)}.`);
    } catch (err) {
      setMessage(describeBillingFailure(err).message.replace("Not recorded:", `${p.procedureCode} was not billed:`));
    } finally {
      setBusy(null);
    }
  }

  return (
    <section aria-labelledby="alv-bill-ready-title">
      <h3 id="alv-bill-ready-title">Completed procedures ready to bill</h3>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      {procedures.length === 0
        ? <p className="alv-clinical__meta">No completed procedure is waiting to be billed.</p>
        : (
          <ul className="alv-bill__ready" aria-label="Completed procedures ready to bill">
            {procedures.map((p) => (
              <li key={p.planItemId} className="alv-bill__ready-item">
                <span><strong>{p.procedureCode}</strong> {p.description}{p.toothKey ? ` (tooth ${p.toothKey}${p.surface ? `, ${p.surface}` : ""})` : ""} - fee {formatMoney(p.fee)}</span>
                {canWrite && <button type="button" className="btn btn-primary" disabled={busy !== null} onClick={() => void bill(p)}>Bill {p.procedureCode}</button>}
              </li>
            ))}
          </ul>
        )}
    </section>
  );
}
