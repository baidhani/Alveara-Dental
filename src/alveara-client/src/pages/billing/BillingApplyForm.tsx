import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { applyPayment, formatMoney } from "../../services/billingApi";
import type { BillingAccount } from "../../services/billingApi";
import { AllocationChooser } from "./AllocationChooser";
import { allocationErrorsByCharge, namedAllocations, openChargesOf, standingOf } from "./billingLedgerView";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  account: BillingAccount;
  paymentEntryNumber: number;
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-007-C01: apply what is left of an earlier payment to charges. The oldest open charges are paid first unless the person chooses the charges; nothing already recorded is changed (this adds allocations).
 * A save carries a key that is kept while the entry is unchanged, so a double click or a retry cannot apply it twice.
 */
export function BillingApplyForm({ account, paymentEntryNumber, onDone, onConflict, onCancel }: Props) {
  const [choose, setChoose] = useState(false);
  const [typed, setTyped] = useState<Record<number, string>>({});
  const [chargeErrors, setChargeErrors] = useState<Record<number, string>>({});
  const [message, setMessage] = useState<string | null>(null);
  const [summary, setSummary] = useState<string | undefined>();
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();
  const charges = openChargesOf(account);
  const left = standingOf(account, paymentEntryNumber)?.unappliedAmount ?? 0;

  async function submit() {
    const named = choose ? namedAllocations(charges, typed) : { allocations: [], order: [] as number[] };
    if (choose && named.allocations.length === 0) { setMessage("Not recorded: name at least one charge, or apply the rest to the oldest charges first. Nothing was sent."); return; }
    const signature = JSON.stringify([account.rowVersion, paymentEntryNumber, choose ? named.allocations : null]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setSummary(undefined);
    setChargeErrors({});
    try {
      const updated = await applyPayment(account, attempt.current.key, paymentEntryNumber, choose ? named.allocations : undefined);
      onDone(updated, `Payment ${paymentEntryNumber} applied. ${formatMoney(updated.unappliedTotal ?? 0)} of payments is still unapplied.`);
    } catch (err) {
      const failure = describeBillingFailure(err);
      setMessage(failure.message);
      setSummary(failure.fields.allocations);
      setChargeErrors(allocationErrorsByCharge(failure.fields, named.order));
      if (failure.conflict) onConflict();
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`Apply payment ${paymentEntryNumber}`} noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Apply payment {paymentEntryNumber}</h3>
      <p className="alv-clinical__meta">{formatMoney(left)} of this payment is not applied to any charge. It goes to the oldest open charges first, or to the ones you choose.</p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label className="alv-bill__choose" htmlFor={`${id}-choose`}><input id={`${id}-choose`} type="checkbox" checked={choose} onChange={(e) => setChoose(e.target.checked)} /> Choose which charges to pay</label>
      {choose && <AllocationChooser charges={charges} typed={typed} onTyped={(n, v) => setTyped((t) => ({ ...t, [n]: v }))} errors={chargeErrors} summaryError={summary} />}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Apply payment</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
