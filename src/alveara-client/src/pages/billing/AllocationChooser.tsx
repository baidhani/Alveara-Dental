import { useId } from "react";
import { formatMoney } from "../../services/billingApi";
import type { OpenCharge } from "./billingLedgerView";

interface Props {
  charges: OpenCharge[];
  /** What was typed for each charge, by entry number. */
  typed: Record<number, string>;
  onTyped: (entryNumber: number, value: string) => void;
  /** The server's message for a charge it refused, by entry number. */
  errors: Record<number, string>;
  /** The overall message about the allocations as a whole (more than the payment holds). */
  summaryError?: string;
}

/**
 * ALV-007-C01: choose which open charges a payment pays and how much of each. Only charges that still have something to pay are offered, each with what is left; whatever is not named stays unapplied and can be
 * applied later. A refusal is shown beside the charge it is about.
 */
export function AllocationChooser({ charges, typed, onTyped, errors, summaryError }: Props) {
  const id = useId();
  if (charges.length === 0) return <p className="alv-clinical__meta">No charge has anything left to pay, so this would stay unapplied.</p>;
  return (
    <fieldset className="alv-bill__alloc">
      <legend>Apply to these charges</legend>
      {summaryError && <p className="alv-bill__error" role="alert">{summaryError}</p>}
      {charges.map((c) => (
        <div key={c.entryNumber} className="alv-bill__alloc-row">
          <label htmlFor={`${id}-${c.entryNumber}`}>{c.label} (open {formatMoney(c.open)})
            <input id={`${id}-${c.entryNumber}`} type="text" inputMode="decimal" aria-label={`Apply to charge ${c.entryNumber} (US dollars)`} value={typed[c.entryNumber] ?? ""}
              aria-invalid={errors[c.entryNumber] ? true : undefined} aria-describedby={errors[c.entryNumber] ? `${id}-${c.entryNumber}-e` : undefined} onChange={(e) => onTyped(c.entryNumber, e.target.value)} />
          </label>
          {errors[c.entryNumber] && <p id={`${id}-${c.entryNumber}-e`} className="alv-bill__error">{errors[c.entryNumber]}</p>}
        </div>
      ))}
    </fieldset>
  );
}
