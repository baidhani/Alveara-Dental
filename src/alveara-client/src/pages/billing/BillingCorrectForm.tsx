import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { PAYMENT_METHODS, formatMoney } from "../../services/billingApi";
import type { BillingAccount, BillingEntry } from "../../services/billingApi";
import { correctEntry } from "../../services/billingStatementsApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  account: BillingAccount;
  /** The payment or adjustment being corrected. */
  entry: BillingEntry;
  methods?: string[];
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-007-C02: correct a payment or an adjustment. The screen says what will happen: the entry is reversed exactly (it stays in the history) and a replacement is recorded that names it, in one step. A replacement
 * payment starts unapplied and is applied afterwards; a charge is never corrected here (it can only be reversed). The reason for the reversal and the replacement amount are required; the server refuses a
 * correction that would leave the account owing less than nothing and the screen keeps what was typed.
 */
export function BillingCorrectForm({ account, entry, methods, onDone, onConflict, onCancel }: Props) {
  const isPayment = entry.kind === "Payment";
  const [amount, setAmount] = useState("");
  const [method, setMethod] = useState("");
  const [note, setNote] = useState("");
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();
  const names = methods && methods.length > 0 ? methods : [...PAYMENT_METHODS];

  async function submit() {
    const gaps: Record<string, string> = {};
    const value = amount.trim() === "" ? null : Number(amount);
    if (value === null || Number.isNaN(value)) gaps.amount = isPayment ? "Enter the corrected payment amount." : "Enter the corrected adjustment amount (negative to reduce what is owed).";
    if (isPayment && method === "") gaps.method = "Choose how the payment was received.";
    if (reason.trim() === "") gaps.reason = "A correction needs a reason, so the history explains it.";
    if (Object.keys(gaps).length > 0) { setFields(gaps); setMessage("Not recorded: some entries need correcting. Nothing was sent."); return; }

    const signature = JSON.stringify([account.rowVersion, entry.entryNumber, value, method, note.trim(), reason.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setFields({});
    try {
      const updated = await correctEntry(account, attempt.current.key, {
        entryNumber: entry.entryNumber, reason: reason.trim(), amount: value, ...(isPayment ? { method } : {}), note: note.trim() === "" ? null : note.trim(),
      });
      onDone(updated, `${entry.kind} ${entry.entryNumber} corrected: reversed and replaced. Balance owed is now ${formatMoney(updated.balanceOwed)}.`);
    } catch (err) {
      const failure = describeBillingFailure(err);
      setMessage(failure.message);
      setFields(failure.fields);
      if (failure.conflict) onConflict();
    } finally {
      setBusy(false);
    }
  }

  const error = (key: string) => fields[key] && <p id={`${id}-${key}-e`} className="alv-bill__error">{fields[key]}</p>;
  return (
    <form className="alv-dx__form" aria-label={`Correct ${entry.kind.toLowerCase()} ${entry.entryNumber}`} noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Correct {entry.kind.toLowerCase()} {entry.entryNumber} ({formatMoney(entry.amount)})</h3>
      <p className="alv-clinical__meta">
        This reverses entry {entry.entryNumber} exactly, which stays in the history, and records a replacement that names it, in one step.
        {isPayment ? " The replacement payment starts unapplied; apply it to charges afterwards." : ""}
      </p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label htmlFor={`${id}-amount`}>{isPayment ? "Corrected payment amount (US dollars)" : "Corrected adjustment amount (US dollars, negative reduces what is owed)"}
        <input id={`${id}-amount`} type="text" inputMode="decimal" value={amount} aria-invalid={fields.amount ? true : undefined} onChange={(e) => setAmount(e.target.value)} />
      </label>
      {error("amount")}
      {isPayment && (
        <label htmlFor={`${id}-method`}>How it was received
          <select id={`${id}-method`} value={method} aria-invalid={fields.method ? true : undefined} onChange={(e) => setMethod(e.target.value)}>
            <option value="">Choose a method</option>
            {names.map((m) => <option key={m} value={m}>{m}</option>)}
          </select>
        </label>
      )}
      {error("method")}
      <label htmlFor={`${id}-note`}>{isPayment ? "Note on the replacement (optional)" : "Reason for the replacement (defaults to the reason below)"}
        <input id={`${id}-note`} type="text" maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
      </label>
      {error("note")}
      <label htmlFor={`${id}-reason`}>Why is it being corrected?
        <input id={`${id}-reason`} type="text" maxLength={500} value={reason} aria-invalid={fields.reason ? true : undefined} onChange={(e) => setReason(e.target.value)} />
      </label>
      {error("reason")}
      {fields.entryNumber && <p className="alv-bill__error">{fields.entryNumber}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Reverse and replace</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
