import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { PAYMENT_METHODS, formatMoney } from "../../services/billingApi";
import type { BillingAccount, BillingEntry } from "../../services/billingApi";
import { refundPayment } from "../../services/billingStatementsApi";
import { describeBillingFailure } from "./billingFailure";
import { standingOf } from "./billingLedgerView";

interface Props {
  account: BillingAccount;
  /** The payment being refunded. */
  entry: BillingEntry;
  /** Who the money goes back to: the person recorded as having paid it, otherwise the patient. It cannot be changed here. */
  recipient: string;
  methods?: string[];
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-007-C02: hand money back for one payment. The screen says what will happen before anything is sent: the money goes back to the original payer only (there is no way to choose anyone else), it comes first
 * from what the payment has not applied to any charge and then from what it paid, newest first (so those charges become open again), and a refund never creates credit. The reason and how it is returned are
 * required. Nothing is edited: a refund is a new entry. A save carries a key that is kept while the request is unchanged, so a double click or a retry cannot refund twice.
 */
export function BillingRefundForm({ account, entry, recipient, methods, onDone, onConflict, onCancel }: Props) {
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("");
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();
  const standing = standingOf(account, entry.entryNumber);
  const names = methods && methods.length > 0 ? methods : [...PAYMENT_METHODS];

  async function submit() {
    const gaps: Record<string, string> = {};
    const value = amount.trim() === "" ? null : Number(amount);
    if (value === null || Number.isNaN(value)) gaps.amount = "Enter the amount to refund.";
    if (method === "") gaps.method = "Choose how the money is returned.";
    if (reason.trim() === "") gaps.reason = "A refund needs a reason, so the history explains it.";
    if (Object.keys(gaps).length > 0) { setFields(gaps); setMessage("Not recorded: some entries need correcting. Nothing was sent."); return; }

    const signature = JSON.stringify([account.rowVersion, entry.entryNumber, value, method, reason.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setFields({});
    try {
      const updated = await refundPayment(account, attempt.current.key, { paymentEntryNumber: entry.entryNumber, amount: value, method, reason: reason.trim() });
      onDone(updated, `${formatMoney(value!)} refunded to ${recipient}. Balance owed is now ${formatMoney(updated.balanceOwed)}.`);
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
    <form className="alv-dx__form" aria-label={`Refund payment ${entry.entryNumber}`} noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Refund payment {entry.entryNumber} ({formatMoney(-entry.amount)})</h3>
      <p className="alv-clinical__meta">
        The money goes back to {recipient}, who paid it; it cannot be sent to anyone else. Up to {formatMoney(standing?.refundableAmount ?? 0)} can still be refunded.
        It is taken first from the {formatMoney(standing?.unappliedAmount ?? 0)} not applied to any charge, then from the charges this payment paid, newest first, which become open again. A refund adds an entry
        and never creates credit.
      </p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label htmlFor={`${id}-amount`}>Amount to refund (US dollars)
        <input id={`${id}-amount`} type="text" inputMode="decimal" value={amount} aria-invalid={fields.amount ? true : undefined} aria-describedby={fields.amount ? `${id}-amount-e` : undefined} onChange={(e) => setAmount(e.target.value)} />
      </label>
      {fields.amount && <p id={`${id}-amount-e`} className="alv-bill__error">{fields.amount}</p>}
      <label htmlFor={`${id}-method`}>How the money is returned
        <select id={`${id}-method`} value={method} aria-invalid={fields.method ? true : undefined} aria-describedby={fields.method ? `${id}-method-e` : undefined} onChange={(e) => setMethod(e.target.value)}>
          <option value="">Choose a method</option>
          {names.map((m) => <option key={m} value={m}>{m}</option>)}
        </select>
      </label>
      {fields.method && <p id={`${id}-method-e`} className="alv-bill__error">{fields.method}</p>}
      <label htmlFor={`${id}-reason`}>Why is it being refunded?
        <input id={`${id}-reason`} type="text" maxLength={500} value={reason} aria-invalid={fields.reason ? true : undefined} aria-describedby={fields.reason ? `${id}-reason-e` : undefined} onChange={(e) => setReason(e.target.value)} />
      </label>
      {fields.reason && <p id={`${id}-reason-e`} className="alv-bill__error">{fields.reason}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Refund to {recipient}</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
