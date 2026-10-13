import { useEffect, useState } from "react";
import { getAging, getTimeline, isAgingView, isTimeline } from "../../services/billingStatementsApi";
import type { AgingView, TimelineEvent } from "../../services/billingStatementsApi";
import { formatMoney } from "../../services/billingApi";
import { BillingChargeSource } from "./BillingChargeSource";

const money = (text: string) => `$${text}`;

/** ALV-007-C02: what is open on the account by age, as the server calculated it (live: a completed procedure ages from its latest amended performed time). Left out when it cannot be read. */
export function BillingAging({ patientId, version }: { patientId: string; version: string | null }) {
  const [aging, setAging] = useState<AgingView | null>(null);
  useEffect(() => {
    const controller = new AbortController();
    getAging(patientId, controller.signal).then((a) => setAging(isAgingView(a) ? a : null)).catch(() => setAging(null));
    return () => controller.abort();
  }, [patientId, version]);
  if (!aging || aging.openCharges.length === 0) return null;
  const b = aging.buckets;
  return (
    <section aria-labelledby="alv-bill-aging-title">
      <h3 id="alv-bill-aging-title">What is open, by age</h3>
      <table className="alv-bill__table" aria-label="Open charges by age">
        <thead><tr><th scope="col">0-30 days</th><th scope="col">31-60 days</th><th scope="col">61-90 days</th><th scope="col">Over 90 days</th></tr></thead>
        <tbody><tr><td>{money(b.current)}</td><td>{money(b.days31To60)}</td><td>{money(b.days61To90)}</td><td>{money(b.over90)}</td></tr></tbody>
      </table>
    </section>
  );
}

/**
 * ALV-007-C02: the account's timeline: every entry, application, release, change of responsible party and issued document in the order it happened, with the completed procedure behind each charge. It is
 * loaded when asked for (it is the long view) and says plainly when it could not be loaded rather than showing an empty history.
 */
export function BillingTimeline({ patientId, version, canViewClinical = false }: { patientId: string; version: string | null; canViewClinical?: boolean }) {
  const [open, setOpen] = useState(false);
  const [sourceOf, setSourceOf] = useState<number | null>(null);
  const [events, setEvents] = useState<TimelineEvent[] | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    getTimeline(patientId, controller.signal).then((t) => { setEvents(isTimeline(t) ? t : null); setFailed(!isTimeline(t)); }).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [open, patientId, version]);
  return (
    <section aria-labelledby="alv-bill-timeline-title">
      <h3 id="alv-bill-timeline-title">Timeline</h3>
      <button type="button" className="btn btn-outline-secondary" aria-expanded={open} onClick={() => setOpen((o) => !o)}>{open ? "Hide the timeline" : "Show the timeline"}</button>
      {open && failed && <p className="alv-clinical__meta" role="alert">Could not load the timeline.</p>}
      {open && events && (
        <ol className="alv-bill__timeline" aria-label="Account timeline">
          {events.map((e, i) => (
            <li key={`${e.type}-${e.number}-${i}`}>
              <span className="alv-bill__when">{new Date(e.atUtc).toLocaleString()}</span> <strong>{e.type}</strong>{e.number !== null ? ` ${e.number}` : ""}: {e.summary}
              {e.amount !== null && ` (${formatMoney(e.amount)})`}{e.source && <span className="alv-bill__standing"> - {e.source}</span>}{e.actorName && <span className="alv-bill__standing"> - {e.actorName}</span>}
              {e.type === "Charge" && e.planItemId && e.number !== null && (
                <>{" "}<button type="button" className="btn btn-outline-secondary" aria-expanded={sourceOf === e.number} onClick={() => setSourceOf((cur) => (cur === e.number ? null : e.number))}>{sourceOf === e.number ? `Hide source of charge ${e.number}` : `View source of charge ${e.number}`}</button></>
              )}
              {canViewClinical && e.completedProcedureId && <> <a className="alv-workspace__link" href={`/patients/${patientId}/history`}>Clinical history</a></>}
              {e.type === "Charge" && e.number !== null && sourceOf === e.number && <BillingChargeSource patientId={patientId} entryNumber={e.number} />}
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
