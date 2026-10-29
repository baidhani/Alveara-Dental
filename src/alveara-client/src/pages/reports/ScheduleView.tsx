import { useState } from "react";
import { getSchedule } from "../../services/reportsApi";
import type { AppointmentDrill, ReportQuery, ScheduleCounts } from "../../services/reportsApi";
import { NumberButton } from "./NumberButton";
import { AppointmentRecords } from "./RecordsPanel";
import { ReportFrame } from "./ReportFrame";
import { hoursAndMinutes, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Base = Omit<AppointmentDrill, "route">;
type Spec = { title: string; drill: AppointmentDrill };

/** The five count cells of one row (all, scheduled, cancelled, no-shows, booked time); each count opens the appointments behind it. */
function CountCells({ counts, label, base, open }: { counts: ScheduleCounts; label: string; base: Base; open: (title: string, drill: Base) => void }) {
  return (
    <>
      <td><NumberButton value={counts.appointments} label={`${label}, all appointments`} onOpen={() => open(`${label}: all appointments`, base)} /></td>
      <td><NumberButton value={counts.scheduled} label={`${label}, scheduled`} onOpen={() => open(`${label}: scheduled`, { ...base, status: "Scheduled" })} /></td>
      <td><NumberButton value={counts.cancelled} label={`${label}, cancelled`} onOpen={() => open(`${label}: cancelled`, { ...base, status: "Cancelled" })} /></td>
      <td><NumberButton value={counts.noShow} label={`${label}, no-shows`} onOpen={() => open(`${label}: no-shows`, { ...base, status: "NoShow" })} /></td>
      <td>{hoursAndMinutes(counts.bookedMinutes)}</td>
    </>
  );
}

const Head = ({ first }: { first: string }) => (
  <thead><tr><th scope="col">{first}</th><th scope="col">Appointments</th><th scope="col">Scheduled</th><th scope="col">Cancelled</th><th scope="col">No-shows</th><th scope="col">Booked time</th></tr></thead>
);

/**
 * The schedule report: appointments that start in the period, by day and by booked provider. Every count is a button that lists the appointments behind it. A day with nothing booked is still a row of zeros, so a gap
 * is visible. The reschedules are a count of recorded events on these appointments; each appointment's own history shows them.
 */
export function ScheduleView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getSchedule(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const open = (title: string, drill: Base) => setSpec({ title, drill: { route: "schedule/appointments", ...drill } });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No appointments start in this period for the chosen filters.">
      {(report) => {
        const t = report.data.totals;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Appointments</span><strong>{t.appointments.toLocaleString()}</strong></li>
              <li><span>Scheduled</span><strong>{t.scheduled.toLocaleString()}</strong></li>
              <li><span>Cancelled</span><strong>{t.cancelled.toLocaleString()}</strong></li>
              <li><span>No-shows</span><strong>{t.noShow.toLocaleString()}</strong></li>
              <li><span>Booked time</span><strong>{hoursAndMinutes(t.bookedMinutes)}</strong></li>
              <li><span>Reschedules recorded</span><strong>{report.data.rescheduleEvents.toLocaleString()}</strong></li>
            </ul>
            <p className="alv-rep__hint">Booked time counts only appointments that still hold time (scheduled ones). Utilisation against provider hours is not shown here.</p>

            <h2>By day</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Appointments by day</caption>
              <Head first="Day" />
              <tbody>{report.data.days.map((d) => <tr key={d.date}><th scope="row">{d.date}</th><CountCells counts={d.counts} label={d.date} base={{ date: d.date }} open={open} /></tr>)}</tbody>
            </table>

            <h2>By booked provider</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Appointments by booked provider</caption>
              <Head first="Provider" />
              <tbody>{report.data.providers.map((p) => <tr key={p.providerProfileId}><th scope="row">{p.providerName}</th><CountCells counts={p.counts} label={p.providerName} base={{ providerId: p.providerProfileId }} open={open} /></tr>)}</tbody>
            </table>

            {spec && <AppointmentRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
