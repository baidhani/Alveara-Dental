import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { PAYMENT_METHODS, formatMoney, recordPayment } from "../../services/billingApi";
import type { BillingAccount } from "../../services/billingApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  account: BillingAccount;
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
}

/**
 * STORY-007: take a payment. It goes to this patient's account, which the screen shows above it; the amount must be more than zero and no more than the balance owed (the server checks, and says what is
 * owed). A save carries a key that is kept while the entry is unchanged, so a double click or a retry cannot record the payment twice. A refusal is shown beside its field with what was typed kept.
 */
export function BillingPaymentForm({ account, onDone, onConflict }: Props) {
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("");
  const [note, setNote] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();

  async function submit() {
    const gaps: Record<string, string> = {};
    const value = amount.trim() === "" ? null : Number(amount);
    if (value === null || Number.isNaN(value)) gaps.amount = "Enter the payment amount.";
    if (method === "") gaps.method = "Choose how the payment was received.";
    if (Object.keys(gaps).length > 0) { setFields(gaps); setMessage("Not recorded: some entries need correcting. Nothing was sent."); return; }

    const signature = JSON.stringify([account.rowVersion, value, method, note.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setFields({});
    try {
      const updated = await recordPayment(account, attempt.current.key, { amount: value, method, note: note.trim() === "" ? null : note.trim() });
      setAmount(""); setMethod(""); setNote(""); attempt.current = null;
      onDone(updated, `Payment of ${formatMoney(value!)} recorded. Balance owed is now ${formatMoney(updated.balanceOwed)}.`);
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
    <form className="alv-dx__form" aria-label="Record a payment" noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Record a payment</h3>
      <p className="alv-clinical__meta">A payment is applied to this account's balance owed ({formatMoney(account.balanceOwed)}) and cannot be more than it.</p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label htmlFor={`${id}-amount`}>Amount (US dollars)
        <input id={`${id}-amount`} type="text" inputMode="decimal" value={amount} aria-invalid={fields.amount ? true : undefined} aria-describedby={fields.amount ? `${id}-amount-e` : undefined} onChange={(e) => setAmount(e.target.value)} />
      </label>
      {fields.amount && <p id={`${id}-amount-e`} className="alv-bill__error">{fields.amount}</p>}
      <label htmlFor={`${id}-method`}>How it was received
        <select id={`${id}-method`} value={method} aria-invalid={fields.method ? true : undefined} aria-describedby={fields.method ? `${id}-method-e` : undefined} onChange={(e) => setMethod(e.target.value)}>
          <option value="">Choose a method</option>
          {PAYMENT_METHODS.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </label>
      {fields.method && <p id={`${id}-method-e`} className="alv-bill__error">{fields.method}</p>}
      <label htmlFor={`${id}-note`}>Note (optional)
        <input id={`${id}-note`} type="text" maxLength={500} value={note} aria-invalid={fields.note ? true : undefined} aria-describedby={fields.note ? `${id}-note-e` : undefined} onChange={(e) => setNote(e.target.value)} />
      </label>
      {fields.note && <p id={`${id}-note-e`} className="alv-bill__error">{fields.note}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Record payment</button>
      </div>
    </form>
  );
}
