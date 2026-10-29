import { useState } from "react";
import { getTreatmentPlans } from "../../services/reportsApi";
import type { ReportQuery } from "../../services/reportsApi";
import { PlanItemRecords } from "./MoreRecords";
import { NumberButton } from "./NumberButton";
import { ReportFrame } from "./ReportFrame";
import { money, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; cell?: string };

const CELLS: Record<string, string> = {
  Pending: "Waiting for a decision",
  Accepted: "Accepted",
  Declined: "Declined",
  Withdrawn: "Withdrawn",
  AcceptedScheduled: "Accepted and scheduled",
  AcceptedUnscheduled: "Accepted, not scheduled",
  AcceptedCompleted: "Accepted and done",
};

/** The first four cells are exclusive and add up to all the items; the last three cut the accepted ones further and are shown indented. */
const CUTS = ["AcceptedScheduled", "AcceptedUnscheduled", "AcceptedCompleted"];

/**
 * The treatment-plan status report: the plans MADE in the period and where each proposed procedure stands at the end of the period. Waiting, accepted, declined and withdrawn add up to all the items; the three cuts
 * below accepted say how far the accepted ones have got. The fees are the practice's estimates copied into the plan when each procedure was proposed; they are not what was billed (the financial reports are the ledger).
 */
export function TreatmentPlansView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getTreatmentPlans(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No treatment plan was made in this period.">
      {(report) => {
        const d = report.data;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Plans made</span><strong>{d.plans.toLocaleString()}</strong><small>{d.plansProposed.toLocaleString()} proposed, {d.plansWithdrawn.toLocaleString()} withdrawn</small></li>
              <li><span>Procedures proposed</span><strong>{d.items.toLocaleString()}</strong></li>
            </ul>
            <p className={d.reconciled ? "alv-rep__hint" : "alv-rep__problem"} role={d.reconciled ? undefined : "alert"}>
              {d.reconciled ? "The groups add up to the procedures proposed." : "The groups do not add up to the procedures proposed. Do not rely on these numbers; tell your administrator."}
            </p>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Proposed procedures by where they stand at the end of the period</caption>
              <thead><tr><th scope="col">Where it stands</th><th scope="col">Procedures</th><th scope="col">Estimated fee</th></tr></thead>
              <tbody>
                {d.cells.map((c) => (
                  <tr key={c.cell}>
                    <th scope="row">{CUTS.includes(c.cell) ? `of which: ${(CELLS[c.cell] ?? c.cell).toLowerCase()}` : CELLS[c.cell] ?? c.cell}</th>
                    <td><NumberButton value={c.items} label={CELLS[c.cell] ?? c.cell} onOpen={() => setSpec({ title: CELLS[c.cell] ?? c.cell, cell: c.cell })} /></td>
                    <td>{money(c.estimatedFee)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            <p className="alv-rep__hint">The fees are the practice's estimates copied into each plan. They are not amounts billed.</p>
            {spec && <PlanItemRecords key={spec.cell} query={query} cell={spec.cell} title={spec.title} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
