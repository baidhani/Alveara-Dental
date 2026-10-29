import { useState } from "react";
import { getPatientFlow } from "../../services/reportsApi";
import type { AppointmentDrill, ReportQuery } from "../../services/reportsApi";
import { NumberButton } from "./NumberButton";
import { AppointmentRecords } from "./RecordsPanel";
import { ReportFrame } from "./ReportFrame";
import { queryKey, words } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; drill: AppointmentDrill };

/**
 * The patient-flow report: where each visit stands now. A visit is a scheduled appointment; its state runs from scheduled to completed, and every state is a row even when it is zero. Cancelled and no-show
 * appointments are not visits and are counted apart, so the states, the cancelled and the no-shows add up to the appointments (the report says whether they do). Every visit count lists its appointments.
 */
export function PatientFlowView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getPatientFlow(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const open = (title: string, drill: Omit<AppointmentDrill, "route">) => setSpec({ title, drill: { route: "patient-flow/appointments", ...drill } });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No appointments start in this period for the chosen filters.">
      {(report) => {
        const d = report.data;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Appointments</span><strong>{d.appointments.toLocaleString()}</strong></li>
              <li><span>Cancelled</span><strong>{d.cancelled.toLocaleString()}</strong></li>
              <li><span>No-shows</span><strong>{d.noShow.toLocaleString()}</strong></li>
              <li><span>Past their time, not completed</span><strong>{d.pastNotCompleted.toLocaleString()}</strong></li>
            </ul>
            <p className={d.reconciled ? "alv-rep__hint" : "alv-rep__problem"} role={d.reconciled ? undefined : "alert"}>
              {d.reconciled ? "The visit states add up to the scheduled appointments." : "The visit states do not add up to the scheduled appointments. Do not rely on these numbers; tell your administrator."}
            </p>

            <h2>Visits by state</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Visits by state</caption>
              <thead><tr><th scope="col">State</th><th scope="col">Visits</th></tr></thead>
              <tbody>{d.byState.map((s) => <tr key={s.state}><th scope="row">{words(s.state)}</th><td><NumberButton value={s.visits} label={`visits ${words(s.state).toLowerCase()}`} onOpen={() => open(`Visits: ${words(s.state).toLowerCase()}`, { flowState: s.state })} /></td></tr>)}</tbody>
            </table>

            <h2>Moves recorded</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Moves recorded on these appointments</caption>
              <thead><tr><th scope="col">Move</th><th scope="col">Times recorded</th></tr></thead>
              <tbody>{d.transitions.map((t) => <tr key={t.event}><th scope="row">{words(t.event)}</th><td>{t.count.toLocaleString()}</td></tr>)}</tbody>
            </table>

            {spec && <AppointmentRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
