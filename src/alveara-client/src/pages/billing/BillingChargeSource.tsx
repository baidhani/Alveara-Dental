import { useEffect, useState } from "react";
import { formatMoney } from "../../services/billingApi";
import { getChargeSource, isChargeSource } from "../../services/billingStatementsApi";
import type { ChargeSource } from "../../services/billingStatementsApi";

const when = (iso: string) => new Date(iso).toLocaleString();

/**
 * ALV-007-C02 R02: where a charge came from, shown inside the timeline for anyone who may read billing. It is the billing projection of the plan procedure and its completion (the procedure, the tooth and
 * surface, the charge, when the completion was recorded and when the procedure was performed, and whether that time was later amended): nothing clinical, so it needs no clinical-record permission. A
 * procedure completed the old, state-only way is shown as having no completion record rather than as having none of anything. A failed load says so instead of showing an empty source.
 */
export function BillingChargeSource({ patientId, entryNumber }: { patientId: string; entryNumber: number }) {
  const [source, setSource] = useState<ChargeSource | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    getChargeSource(patientId, entryNumber, controller.signal)
      .then((s) => { if (isChargeSource(s)) { setSource(s); setFailed(false); } else setFailed(true); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patientId, entryNumber]);

  if (failed) return <p className="alv-clinical__meta" role="alert">Could not load the source of charge {entryNumber}.</p>;
  if (source === null) return <p className="alv-clinical__meta">Loading the source of charge {entryNumber}…</p>;
  return (
    <div className="alv-bill__source" role="group" aria-label={`Source of charge ${entryNumber}`}>
      <p>
        <strong>{source.procedureCode} {source.procedureDescription}</strong>
        {source.toothKey ? `, tooth ${source.toothKey}${source.surface ? ` ${source.surface}` : ""}` : ""}. Charge {formatMoney(source.chargeAmount)}{source.chargeReversed ? " (reversed)" : ""}.
      </p>
      {source.completionRecorded
        ? <p>Completion recorded {source.completedAtUtc ? when(source.completedAtUtc) : ""}; performed {source.performedAtUtc ? when(source.performedAtUtc) : ""}{source.performedTimeAmended ? " (the performed time was later amended)" : ""}.</p>
        : <p>This procedure was completed the old way, which recorded only that it was completed: there is no completion record to show.</p>}
    </div>
  );
}
