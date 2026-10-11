import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { formatMoney, reverseEntry } from "../../services/billingApi";
import type { BillingAccount, BillingEntry } from "../../services/billingApi";
import { describeBillingFailure } from "./billingFailure";

interface Props {
  account: BillingAccount;
  entry: BillingEntry;
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/**
 * ALV-007-C01: undo one earlier entry. Nothing is edited or removed: a new entry is added that is exactly the opposite and says why. Reversing a payment releases what it was applied to; a charge that payments are
 * still applied to cannot be reversed until they are, and a reversal can never be reversed (the server refuses each and says so). The reason is required.
 */
export function BillingReverseForm({ account, entry, onDone, onConflict, onCancel }: Props) {
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();

  async function submit() {
    if (reason.trim() === "") { setFieldError("A reversal needs a reason, so the history explains it."); setMessage("Not recorded: some entries need correcting. Nothing was sent."); return; }
    const signature = JSON.stringify([account.rowVersion, entry.entryNumber, reason.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setFieldError(null);
    try {
      const updated = await reverseEntry(account, attempt.current.key, entry.entryNumber, reason.trim());
      onDone(updated, `${entry.kind} ${entry.entryNumber} reversed. Balance owed is now ${formatMoney(updated.balanceOwed)}.`);
    } catch (err) {
      const failure = describeBillingFailure(err);
      setMessage(failure.message);
      setFieldError(failure.fields.reason ?? failure.fields.entryNumber ?? null);
      if (failure.conflict) onConflict();
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-dx__form" aria-label={`Reverse entry ${entry.entryNumber}`} noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Reverse {entry.kind.toLowerCase()} {entry.entryNumber} ({formatMoney(entry.amount)})</h3>
      <p className="alv-clinical__meta">This adds an entry that undoes it. The original stays in the history exactly as it was.</p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label htmlFor={`${id}-reason`}>Why is it being reversed?
        <input id={`${id}-reason`} type="text" maxLength={500} value={reason} aria-invalid={fieldError ? true : undefined} aria-describedby={fieldError ? `${id}-reason-e` : undefined} onChange={(e) => setReason(e.target.value)} />
      </label>
      {fieldError && <p id={`${id}-reason-e`} className="alv-bill__error">{fieldError}</p>}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Reverse entry</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
