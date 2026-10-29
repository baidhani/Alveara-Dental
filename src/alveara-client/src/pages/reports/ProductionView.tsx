import { useState } from "react";
import { getProduction } from "../../services/reportsApi";
import type { LedgerDrill, ReportQuery } from "../../services/reportsApi";
import { LedgerProof } from "./LedgerProof";
import { LedgerEntryRecords } from "./MoreRecords";
import { NumberButton } from "./NumberButton";
import { ReportFrame } from "./ReportFrame";
import { LINE_LABELS, money, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; drill: LedgerDrill };

/** The server's way of asking for the charges that no completed procedure can be traced to ("not attributed"). */
const NOT_ATTRIBUTED = "00000000-0000-0000-0000-000000000000";

/**
 * The production and charges report. Production is what was charged less what was reversed, dated by when each entry was RECORDED (not by the date of the procedure). Each provider's share is the provider now credited
 * with the procedure the charge bills; a charge that cannot be traced to a completed procedure is "Not attributed", so the shares still add up. Below, the ledger lines and the proof that they add up to the ledger itself.
 * Loaded opening balances and adjustments are shown there, apart: they are not production.
 */
export function ProductionView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getProduction(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const open = (title: string, drill: Omit<LedgerDrill, "route">) => setSpec({ title, drill: { route: "production/entries", ...drill } });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No charge was recorded in this period for the chosen filters.">
      {(report) => {
        const d = report.data;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Charged</span><strong>{money(d.grossCharges)}</strong><small>{d.chargeEntries.toLocaleString()} {d.chargeEntries === 1 ? "charge" : "charges"}</small></li>
              <li><span>Reversed</span><strong>{money(d.chargesReversed)}</strong></li>
              <li><span>Net production</span><strong>{money(d.netProduction)}</strong></li>
            </ul>
            <p className="alv-rep__hint">Dated by when each entry was recorded, not by the date of the procedure.</p>

            <h2>By provider</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Production by the provider credited with the procedure</caption>
              <thead><tr><th scope="col">Provider</th><th scope="col">Charges</th><th scope="col">Charged</th><th scope="col">Reversed</th><th scope="col">Net</th></tr></thead>
              <tbody>
                {d.providers.map((p) => (
                  <tr key={p.providerProfileId ?? "none"}>
                    <th scope="row">{p.providerName}</th>
                    <td><NumberButton value={p.charges} label={`${p.providerName}, charges`} onOpen={() => open(`${p.providerName}: charges`, { providerId: p.providerProfileId ?? NOT_ATTRIBUTED, line: "Charges" })} /></td>
                    <td>{money(p.gross)}</td><td>{money(p.reversed)}</td><td>{money(p.net)}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <h2>By procedure code</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Production by procedure code</caption>
              <thead><tr><th scope="col">Code</th><th scope="col">Charges</th><th scope="col">Charged</th><th scope="col">Reversed</th><th scope="col">Net</th></tr></thead>
              <tbody>{d.codes.map((c) => <tr key={c.procedureCode}><th scope="row">{c.procedureCode}</th><td>{c.charges.toLocaleString()}</td><td>{money(c.gross)}</td><td>{money(c.reversed)}</td><td>{money(c.net)}</td></tr>)}</tbody>
            </table>

            <LedgerProof proof={d.ledger} opens={["Charges", "ChargeReversals"]} onOpen={(line) => open(LINE_LABELS[line] ?? line, { line })} />
            {spec && <LedgerEntryRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
