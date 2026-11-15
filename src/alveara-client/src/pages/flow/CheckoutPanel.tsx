import { useEffect, useId, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ApiError } from "../../services/authApi";
import {
  CHECKOUT_ITEM_LABELS, CHECKOUT_OUTCOME_LABELS, CHECKOUT_REASON_MAX, NEEDS_LEDGER_ENTRY, NEEDS_REASON, getCheckout, newCheckoutKey, recordCheckoutOutcome,
} from "../../services/checkoutApi";
import type { CheckoutItem, CheckoutReadiness, RecordOutcomeInput } from "../../services/checkoutApi";
import "./FlowBoard.css";   // the panel is also used in the calendar drawer

interface Props {
  appointmentId: string;
  patientName: string;
  /** Called after an outcome was recorded, so the caller re-reads its actions (Complete becomes enabled once every item is resolved). */
  onRecorded: (readiness: CheckoutReadiness) => void;
}

/**
 * ALV-N021 A2 (briefing 6.3): the checkout of a visit that is ready for checkout - the same panel on the Visit board and in the calendar drawer, built only from
 * GET appointments/{id}/checkout. Each item shows whether it applies, its state in words (never colour alone), the outcome with who and when, and the next action.
 * A decision is recorded with an Idempotency-Key (one per attempt, reused on a retry of the same request). Payments and waivers name the ledger entry the server
 * verifies; "deferred" and "waiver requested" need a reason. The server decides who may record what; a refusal is explained and nothing is recorded.
 */
export function CheckoutPanel({ appointmentId, patientName, onRecorded }: Props) {
  const [readiness, setReadiness] = useState<CheckoutReadiness | null>(null);
  const [failed, setFailed] = useState(false);
  const [announce, setAnnounce] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    getCheckout(appointmentId, controller.signal)
      .then((r) => { setReadiness(r); setFailed(false); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [appointmentId]);

  if (failed) return <p className="alv-workspace__note" role="alert">Could not load the checkout for {patientName}. Refresh and try again.</p>;
  if (!readiness) return <p className="alv-workspace__note">Loading the checkout…</p>;

  const recorded = (r: CheckoutReadiness, item: string) => {
    setReadiness(r);
    setAnnounce(`${CHECKOUT_ITEM_LABELS[item as keyof typeof CHECKOUT_ITEM_LABELS] ?? item} recorded. ${r.summary}`);
    onRecorded(r);
  };

  return (
    <section className="flow-checkout" aria-label={`Checkout for ${patientName}`}>
      <p className="flow-checkout__summary"><strong>{readiness.summary}</strong></p>
      <p className="alv-workspace__saved-slot" role="status">{announce}</p>
      <ul className="flow-checkout__items">
        {readiness.items.filter((i) => i.applies).map((item) => (
          <li key={item.code} className={`flow-checkout__item flow-checkout__item--${item.resolved ? "done" : "open"}`} data-item={item.code}>
            <p className="flow-checkout__line">
              <strong>{CHECKOUT_ITEM_LABELS[item.code]}</strong>: {item.resolved ? "Done" : "Open"}
              {item.outcome && <> · {CHECKOUT_OUTCOME_LABELS[item.outcome.code] ?? item.outcome.code}{describeOutcome(item)}</>}
            </p>
            {!item.resolved && item.nextAction && <p className="alv-workspace__note">{item.nextAction}{item.responsibleRole ? ` (${item.responsibleRole})` : ""}</p>}
            {readiness.open && item.recordableOutcomes.length > 0 && (
              <OutcomeForm appointmentId={appointmentId} item={item} onRecorded={(r) => recorded(r, item.code)} />
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}

function describeOutcome(item: CheckoutItem): string {
  const o = item.outcome!;
  const parts: string[] = [];
  if (o.linkedLedgerEntryNumber !== null) parts.push(`ledger entry ${o.linkedLedgerEntryNumber}`);
  if (o.reason) parts.push(`reason: ${o.reason}`);
  if (o.recordedByName) parts.push(`by ${o.recordedByName}`);
  return parts.length ? ` (${parts.join(", ")})` : "";
}

/** One item's decision form. A changed request gets a new Idempotency-Key; a retry of the same request reuses it. */
function OutcomeForm({ appointmentId, item, onRecorded }: { appointmentId: string; item: CheckoutItem; onRecorded: (r: CheckoutReadiness) => void }) {
  const [outcome, setOutcome] = useState("");
  const [reason, setReason] = useState("");
  const [entry, setEntry] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const attempt = useRef<{ body: string; key: string } | null>(null);
  const ids = { outcome: useId(), reason: useId(), entry: useId() };
  const label = CHECKOUT_ITEM_LABELS[item.code];

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy) return;
    if (!outcome) { setError("Choose an outcome."); return; }
    if (NEEDS_REASON.has(outcome) && !reason.trim()) { setError("Give a reason for this outcome."); return; }
    const number = Number(entry);
    if (NEEDS_LEDGER_ENTRY.has(outcome) && (!entry.trim() || !Number.isInteger(number) || number <= 0)) { setError("Enter the ledger entry number of the payment or waiver."); return; }
    const input: RecordOutcomeInput = { outcome, reason: reason.trim() || null, ledgerEntryNumber: NEEDS_LEDGER_ENTRY.has(outcome) ? number : null };
    const body = JSON.stringify(input);
    if (attempt.current?.body !== body) attempt.current = { body, key: newCheckoutKey() };
    setBusy(true);
    setError(null);
    try {
      onRecorded(await recordCheckoutOutcome(appointmentId, item.code, input, attempt.current.key));
      attempt.current = null;
      setOutcome(""); setReason(""); setEntry("");
    } catch (err) {
      setError(err instanceof ApiError
        ? (err.status === 403 ? "Your role may not record this outcome. Nothing was recorded." : `${err.message} Nothing was recorded.`)
        : "We could not confirm whether that was recorded - the connection dropped. Press Record again: it will not be recorded twice.");
    } finally {
      setBusy(false);
    }
  };

  return (
    <form className="flow-checkout__form" onSubmit={(e) => void submit(e)} noValidate aria-label={`Record ${label.toLowerCase()} outcome`}>
      <div className="alv-form-field">
        <label htmlFor={ids.outcome} className="alv-form-field__label">{item.resolved ? `Change ${label.toLowerCase()} outcome` : `${label} outcome`}</label>
        <select id={ids.outcome} className="alv-form-field__input" value={outcome} disabled={busy} onChange={(e) => { setOutcome(e.target.value); setError(null); }}>
          <option value="">Choose…</option>
          {item.recordableOutcomes.map((o) => <option key={o} value={o}>{CHECKOUT_OUTCOME_LABELS[o] ?? o}</option>)}
        </select>
      </div>
      {NEEDS_LEDGER_ENTRY.has(outcome) && (
        <div className="alv-form-field">
          <label htmlFor={ids.entry} className="alv-form-field__label">Ledger entry number (required)</label>
          <input id={ids.entry} className="alv-form-field__input" inputMode="numeric" value={entry} disabled={busy} onChange={(e) => setEntry(e.target.value)} />
        </div>
      )}
      {(NEEDS_REASON.has(outcome) || outcome !== "") && (
        <div className="alv-form-field">
          <label htmlFor={ids.reason} className="alv-form-field__label">{NEEDS_REASON.has(outcome) ? "Reason (required)" : "Reason (optional)"}</label>
          <textarea id={ids.reason} className="alv-form-field__input alv-form-inputs__textarea" rows={2} maxLength={CHECKOUT_REASON_MAX} value={reason} disabled={busy} onChange={(e) => setReason(e.target.value)} />
        </div>
      )}
      {error && <p className="flow-card__error" role="alert">{error}</p>}
      <Button type="submit" disabled={busy}>{busy ? "Recording…" : "Record"}</Button>
    </form>
  );
}
