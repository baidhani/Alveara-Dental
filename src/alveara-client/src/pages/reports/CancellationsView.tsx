import { useState } from "react";
import { getCancellations } from "../../services/reportsApi";
import type { AppointmentDrill, CancellationCounts, ReportQuery } from "../../services/reportsApi";
import { NumberButton } from "./NumberButton";
import { AppointmentRecords } from "./RecordsPanel";
import { ReportFrame } from "./ReportFrame";
import { percent, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Base = Omit<AppointmentDrill, "route">;
type Spec = { title: string; drill: AppointmentDrill };

const LEAD_TIMES: Record<string, string> = {
  LessThan24h: "Less than 24 hours before",
  Within7Days: "1 to 7 days before",
  MoreThan7Days: "More than 7 days before",
  Unknown: "No time recorded",
};

function CountCells({ counts, label, base, open }: { counts: CancellationCounts; label: string; base: Base; open: (title: string, drill: Base) => void }) {
  return (
    <>
      <td><NumberButton value={counts.booked} label={`${label}, booked`} onOpen={() => open(`${label}: booked`, { ...base, measure: "Booked" })} /></td>
      <td><NumberButton value={counts.cancelled} label={`${label}, cancelled`} onOpen={() => open(`${label}: cancelled`, { ...base, measure: "Cancelled" })} /></td>
      <td>{percent(counts.cancellationRate)}</td>
      <td><NumberButton value={counts.noShow} label={`${label}, no-shows`} onOpen={() => open(`${label}: no-shows`, { ...base, measure: "NoShow" })} /></td>
      <td>{percent(counts.noShowRate)}</td>
    </>
  );
}

const Head = ({ first }: { first: string }) => (
  <thead><tr><th scope="col">{first}</th><th scope="col">Booked</th><th scope="col">Cancelled</th><th scope="col">Cancellation rate</th><th scope="col">No-shows</th><th scope="col">No-show rate</th></tr></thead>
);

/**
 * The cancellations and no-shows report. A rate is the fraction of the appointments booked in the period (whatever day they were cancelled); when nothing was booked there is no rate and it says "Not available", never
 * zero. Cancellations are also cut by how early they were made. Every count lists its appointments; the reason a person gave for cancelling is free text and is never shown.
 */
export function CancellationsView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getCancellations(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const open = (title: string, drill: Base) => setSpec({ title, drill: { route: "cancellations/appointments", ...drill } });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No appointments start in this period for the chosen filters, so there are no rates to show.">
      {(report) => {
        const d = report.data;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Booked</span><strong>{d.totals.booked.toLocaleString()}</strong></li>
              <li><span>Cancelled</span><strong>{d.totals.cancelled.toLocaleString()}</strong><small>{percent(d.totals.cancellationRate)} of booked</small></li>
              <li><span>No-shows</span><strong>{d.totals.noShow.toLocaleString()}</strong><small>{percent(d.totals.noShowRate)} of booked</small></li>
            </ul>
            <p className={d.reconciled ? "alv-rep__hint" : "alv-rep__problem"} role={d.reconciled ? undefined : "alert"}>
              {d.reconciled ? "The providers, the types and the lead times add up to the totals." : "The cuts do not add up to the totals. Do not rely on these numbers; tell your administrator."}
            </p>

            <h2>How early cancellations were made</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Cancelled appointments by how early they were cancelled</caption>
              <thead><tr><th scope="col">Cancelled</th><th scope="col">Appointments</th></tr></thead>
              <tbody>{d.leadTimes.map((l) => <tr key={l.bucket}><th scope="row">{LEAD_TIMES[l.bucket] ?? l.bucket}</th><td><NumberButton value={l.cancelled} label={`cancelled ${LEAD_TIMES[l.bucket] ?? l.bucket}`} onOpen={() => open(`Cancelled: ${(LEAD_TIMES[l.bucket] ?? l.bucket).toLowerCase()}`, { measure: "Cancelled", leadTime: l.bucket })} /></td></tr>)}</tbody>
            </table>

            <h2>By booked provider</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Cancellations and no-shows by booked provider</caption>
              <Head first="Provider" />
              <tbody>{d.providers.map((p) => <tr key={p.providerProfileId}><th scope="row">{p.providerName}</th><CountCells counts={p.counts} label={p.providerName} base={{ providerId: p.providerProfileId }} open={open} /></tr>)}</tbody>
            </table>

            <h2>By appointment type</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Cancellations and no-shows by appointment type</caption>
              <Head first="Type" />
              <tbody>{d.types.map((t) => <tr key={t.appointmentTypeId}><th scope="row">{t.typeName}</th><CountCells counts={t.counts} label={t.typeName} base={{ appointmentTypeId: t.appointmentTypeId }} open={open} /></tr>)}</tbody>
            </table>

            {spec && <AppointmentRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
