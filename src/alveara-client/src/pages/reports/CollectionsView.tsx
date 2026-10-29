import { useState } from "react";
import { getCollections } from "../../services/reportsApi";
import type { LedgerDrill, ReportQuery } from "../../services/reportsApi";
import { LedgerProof } from "./LedgerProof";
import { LedgerEntryRecords } from "./MoreRecords";
import { NumberButton } from "./NumberButton";
import { ReportFrame } from "./ReportFrame";
import { LINE_LABELS, money, queryKey } from "./reportFormat";
import { useReport } from "./useReport";

type Spec = { title: string; drill: LedgerDrill };

const COLLECTION_LINES = ["Payments", "PaymentReversals", "Refunds", "RefundReversals"];

/**
 * The collections and payments report, in money (what was received is a positive amount here, though the ledger stores a payment as negative). Net collections are the payments received less payments reversed and
 * refunds. By payment method: the kind of method recorded on the entry ("Unspecified" for older entries that recorded none). Dated by when each entry was RECORDED. Below, the ledger lines and the proof that they add
 * up to the ledger itself; loaded opening balances are shown there, apart, and are not collections.
 */
export function CollectionsView({ query }: { query: ReportQuery }) {
  const { outcome, again } = useReport((signal) => getCollections(query, signal), queryKey(query));
  const [spec, setSpec] = useState<Spec | null>(null);
  const open = (title: string, drill: Omit<LedgerDrill, "route">) => setSpec({ title, drill: { route: "collections/entries", ...drill } });

  return (
    <ReportFrame outcome={outcome} onRetry={again} emptyText="No payment or refund was recorded in this period.">
      {(report) => {
        const d = report.data;
        return (
          <>
            <ul className="alv-rep__cards" aria-label="Summary">
              <li><span>Received</span><strong>{money(d.paymentsReceived)}</strong><small>{d.paymentEntries.toLocaleString()} {d.paymentEntries === 1 ? "payment" : "payments"}</small></li>
              <li><span>Payments reversed</span><strong>{money(d.paymentsReversed)}</strong></li>
              <li><span>Refunded</span><strong>{money(d.refunded)}</strong></li>
              <li><span>Net collections</span><strong>{money(d.netCollections)}</strong></li>
            </ul>
            <p className="alv-rep__hint">Dated by when each entry was recorded. Collections cannot be narrowed by provider or location: a payment has neither.</p>

            <h2>By payment method</h2>
            <table className="alv-rep__table">
              <caption className="alv-rep__sr">Collections by payment method</caption>
              <thead><tr><th scope="col">Method</th><th scope="col">Payments</th><th scope="col">Received</th><th scope="col">Reversed</th><th scope="col">Refunded</th><th scope="col">Net</th></tr></thead>
              <tbody>
                {d.methods.map((m) => (
                  <tr key={m.method}>
                    <th scope="row">{m.method}</th>
                    <td><NumberButton value={m.payments} label={`${m.method}, payments`} onOpen={() => open(`${m.method}: payments`, { method: m.method, line: "Payments" })} /></td>
                    <td>{money(m.received)}</td><td>{money(m.reversed)}</td><td>{money(m.refunded)}</td><td>{money(m.net)}</td>
                  </tr>
                ))}
              </tbody>
            </table>

            <LedgerProof proof={d.ledger} opens={COLLECTION_LINES} onOpen={(line) => open(LINE_LABELS[line] ?? line, { line })} />
            {spec && <LedgerEntryRecords key={JSON.stringify(spec)} query={query} drill={spec.drill} title={spec.title} timeZone={report.filter.timeZone} onClose={() => setSpec(null)} />}
          </>
        );
      }}
    </ReportFrame>
  );
}
