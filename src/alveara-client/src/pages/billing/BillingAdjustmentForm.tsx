import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { formatMoney, recordAdjustment } from "../../services/billingApi";
import type { BillingAccount } from "../../services/billingApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  account: BillingAccount;
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
}

/**
 * STORY-007: correct the balance. An adjustment is a signed amount (negative reduces what is owed) with the reason for it, and it is the only way to correct a mistake: nothing already recorded is edited
 * or removed. It cannot take the balance below zero (the server checks). A save carries a key that is kept while the entry is unchanged; a refusal is shown beside its field with what was typed kept.
 */
export function BillingAdjustmentForm({ account, onDone, onConflict }: Props) {
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();

  async function submit() {
    const gaps: Record<string, string> = {};
    const value = amount.trim() === "" ? null : Number(amount);
    if (value === null || Number.isNaN(value)) gaps.amount = "Enter the adjustment amount (negative to reduce what is owed).";
    if (reason.trim() === "") gaps.reason = "An adjustment needs a reason, so the history explains it.";
    if (Object.keys(gaps).length > 0) { setFields(gaps); setMessage("Not recorded: some entries need correcting. Nothing was sent."); return; }

    const signature = JSON.stringify([account.rowVersion, value, reason.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setFields({});
    try {
      const updated = await recordAdjustment(account, attempt.current.key, { amount: value, reason: reason.trim() });
      setAmount(""); setReason(""); attempt.current = null;
      onDone(updated, `Adjustment of ${formatMoney(value!)} recorded. Balance owed is now ${formatMoney(updated.balanceOwed)}.`);
    } catch (err) {
      const failure = describeBillingFailure(err);
      setMessage(failure.message);
      setFields(failure.fields);
      if (failure.conflict) onConflict();
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label="Record an adjustment" noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Record an adjustment</h3>
      <p className="alv-clinical__meta">Corrects the balance owed ({formatMoney(account.balanceOwed)}). Earlier entries are never changed; this adds a new one that says why.</p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label htmlFor={`${id}-amount`}>Amount (US dollars, negative to reduce)
        <input id={`${id}-amount`} type="text" inputMode="decimal" value={amount} aria-invalid={fields.amount ? true : undefined} aria-describedby={fields.amount ? `${id}-amount-e` : undefined} onChange={(e) => setAmount(e.target.value)} />
      </label>
      {fields.amount && <p id={`${id}-amount-e`} className="alv-bill__error">{fields.amount}</p>}
      <label htmlFor={`${id}-reason`}>Reason
        <input id={`${id}-reason`} type="text" maxLength={500} value={reason} aria-invalid={fields.reason ? true : undefined} aria-describedby={fields.reason ? `${id}-reason-e` : undefined} onChange={(e) => setReason(e.target.value)} />
      </label>
      {fields.reason && <p id={`${id}-reason-e`} className="alv-bill__error">{fields.reason}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Record adjustment</button>
      </div>
    </form>
  );
}
