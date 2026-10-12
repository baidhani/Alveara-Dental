import { useId, useRef, useState } from "react";
import { newKey } from "../../services/clinicalApi";
import { formatMoney } from "../../services/billingApi";
import type { BillingAccount, BillingEntry } from "../../services/billingApi";
import { moveAllocations } from "../../services/billingStatementsApi";
import { describeBillingFailure } from "./billingFailure";
import { openChargesOf } from "./billingLedgerView";

interface Props {
  account: BillingAccount;
  /** The payment whose applications are moved. */
  entry: BillingEntry;
  onDone: (account: BillingAccount, text: string) => void;
  onConflict: () => void;
  onCancel: () => void;
}

/** The charges this payment still pays, with what it still pays of each (what was applied less what was released), from the server's allocations. */
function paidBy(account: BillingAccount, payment: number) {
  const byCharge = new Map<number, number>();
  for (const a of account.allocations ?? []) {
    if (a.paymentEntryNumber !== payment || a.released) continue;
    byCharge.set(a.chargeEntryNumber, (byCharge.get(a.chargeEntryNumber) ?? 0) + a.amount - (a.releasedAmount ?? 0));
  }
  return [...byCharge].filter(([, v]) => v > 0).map(([charge, amount]) => ({ charge, amount: Math.round(amount * 100) / 100 }));
}

/**
 * ALV-007-C02: move part of what a payment pays from one charge to another. Both sides are explicit: the charge the money comes off and the open charge it goes on, for the same amount. Nothing is edited: the old
 * application is released and a new one is recorded in one step, so the balance owed and the payment's unapplied amount do not change. The server refuses anything that does not add up and the screen keeps what was typed.
 */
export function BillingMoveForm({ account, entry, onDone, onConflict, onCancel }: Props) {
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [amount, setAmount] = useState("");
  const [reason, setReason] = useState("");
  const [message, setMessage] = useState<string | null>(null);
  const [fields, setFields] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; signature: string } | null>(null);
  const id = useId();
  const sources = paidBy(account, entry.entryNumber);
  const targets = openChargesOf(account);

  async function submit() {
    const gaps: Record<string, string> = {};
    const value = amount.trim() === "" ? null : Number(amount);
    if (from === "") gaps["releases[0]"] = "Choose the charge to take the money off.";
    if (to === "") gaps["allocations[0]"] = "Choose the charge to put the money on.";
    if (value === null || Number.isNaN(value)) gaps.amount = "Enter the amount to move.";
    if (reason.trim() === "") gaps.reason = "A move needs a reason, so the history explains it.";
    if (Object.keys(gaps).length > 0) { setFields(gaps); setMessage("Not recorded: some entries need correcting. Nothing was sent."); return; }

    const signature = JSON.stringify([account.rowVersion, entry.entryNumber, from, to, value, reason.trim()]);
    if (attempt.current?.signature !== signature) attempt.current = { key: newKey(), signature };
    setBusy(true);
    setMessage(null);
    setFields({});
    try {
      const updated = await moveAllocations(account, attempt.current.key, {
        paymentEntryNumber: entry.entryNumber, releases: [{ chargeEntryNumber: Number(from), amount: value! }], allocations: [{ chargeEntryNumber: Number(to), amount: value! }], reason: reason.trim(),
      });
      onDone(updated, `${formatMoney(value!)} of payment ${entry.entryNumber} moved from charge ${from} to charge ${to}. Balance owed is unchanged.`);
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
    <form className="alv-dx__form" aria-label={`Move what payment ${entry.entryNumber} pays`} noValidate onSubmit={(e) => { e.preventDefault(); void submit(); }}>
      <h3>Move what payment {entry.entryNumber} pays</h3>
      <p className="alv-clinical__meta">This takes money off one charge, which becomes open again, and puts the same amount on another open charge. The payment, its amount and the balance owed do not change.</p>
      {message && <p className="alv-dx__message" role="alert">{message}</p>}
      <label htmlFor={`${id}-from`}>Take it off
        <select id={`${id}-from`} value={from} aria-invalid={fields["releases[0]"] ? true : undefined} onChange={(e) => setFrom(e.target.value)}>
          <option value="">Choose a charge this payment pays</option>
          {sources.map((s) => <option key={s.charge} value={s.charge}>Charge {s.charge} (this payment pays {formatMoney(s.amount)})</option>)}
        </select>
      </label>
      {error("releases[0]")}
      <label htmlFor={`${id}-to`}>Put it on
        <select id={`${id}-to`} value={to} aria-invalid={fields["allocations[0]"] ? true : undefined} onChange={(e) => setTo(e.target.value)}>
          <option value="">Choose an open charge</option>
          {targets.map((t) => <option key={t.entryNumber} value={t.entryNumber}>{t.label} ({formatMoney(t.open)} open)</option>)}
        </select>
      </label>
      {error("allocations[0]")}
      {fields.allocations && <p className="alv-bill__error">{fields.allocations}</p>}
      <label htmlFor={`${id}-amount`}>Amount to move (US dollars)
        <input id={`${id}-amount`} type="text" inputMode="decimal" value={amount} aria-invalid={fields.amount ? true : undefined} onChange={(e) => setAmount(e.target.value)} />
      </label>
      {error("amount")}
      <label htmlFor={`${id}-reason`}>Why is it being moved?
        <input id={`${id}-reason`} type="text" maxLength={500} value={reason} aria-invalid={fields.reason ? true : undefined} onChange={(e) => setReason(e.target.value)} />
      </label>
      {error("reason")}
      <div className="alv-clinical__row-actions">
        <button type="submit" className="btn btn-primary" disabled={busy}>Move</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onCancel}>Cancel</button>
      </div>
    </form>
  );
}
