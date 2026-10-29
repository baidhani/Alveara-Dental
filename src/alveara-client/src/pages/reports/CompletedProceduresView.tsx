import { useState } from "react";
import { getCompletedProcedures } from "../../services/reportsApi";
import type { ProcedureDrill, ReportQuery } from "../../services/reportsApi";
import { ProcedureRecords } from "./MoreRecords";
import { NumberButton } from "./NumberButton";
import { ReportFrame } from "./ReportFrame";
import { money, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; drill: ProcedureDrill };

/**
 * The completed-procedures report: procedures PERFORMED in the period, dated by the performed time as corrected (an amendment that moves a procedure to another day moves it here), by the provider now credited and by
 * catalog code. The three cuts are of the same procedures, so each adds up to the total (the report says so). The fee is the practice's estimate in the plan, not what was billed. Every count lists its procedures.
 */
export function CompletedProceduresView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getCompletedProcedures(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const open = (title: string, drill: ProcedureDrill) => setSpec({ title, drill });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No procedure was performed in this period for the chosen filters.">
      {(report) => {
        const d = report.data;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Procedures performed</span><strong>{d.procedures.toLocaleString()}</strong></li>
              <li><span>Estimated fees</span><strong>{money(d.estimatedFee)}</strong><small>practice estimates, not billed amounts</small></li>
            </ul>
            <p className={d.reconciled ? "alv-rep__hint" : "alv-rep__problem"} role={d.reconciled ? undefined : "alert"}>
              {d.reconciled ? "The days, the providers and the codes each add up to the procedures performed." : "The cuts do not add up to the procedures performed. Do not rely on these numbers; tell your administrator."}
            </p>

            <h2>By day</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Procedures performed by day</caption>
              <thead><tr><th scope="col">Day</th><th scope="col">Procedures</th></tr></thead>
              <tbody>{d.days.map((x) => <tr key={x.date}><th scope="row">{x.date}</th><td><NumberButton value={x.procedures} label={x.date} onOpen={() => open(`Performed on ${x.date}`, { date: x.date })} /></td></tr>)}</tbody>
            </table>

            <h2>By provider credited</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Procedures by the provider credited</caption>
              <thead><tr><th scope="col">Provider</th><th scope="col">Procedures</th><th scope="col">Estimated fee</th></tr></thead>
              <tbody>{d.providers.map((p) => <tr key={p.providerProfileId}><th scope="row">{p.providerName}</th><td><NumberButton value={p.procedures} label={p.providerName} onOpen={() => open(`Performed by ${p.providerName}`, { providerId: p.providerProfileId })} /></td><td>{money(p.estimatedFee)}</td></tr>)}</tbody>
            </table>

            <h2>By procedure code</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Procedures by catalog code</caption>
              <thead><tr><th scope="col">Code</th><th scope="col">Description</th><th scope="col">Procedures</th><th scope="col">Estimated fee</th></tr></thead>
              <tbody>{d.codes.map((c) => <tr key={`${c.codeSystem}:${c.code}`}><th scope="row">{c.codeSystem} {c.code}</th><td>{c.description}</td><td><NumberButton value={c.procedures} label={`${c.codeSystem} ${c.code}`} onOpen={() => open(`Code ${c.codeSystem} ${c.code}`, { codeSystem: c.codeSystem, code: c.code })} /></td><td>{money(c.estimatedFee)}</td></tr>)}</tbody>
            </table>

            {spec && <ProcedureRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
