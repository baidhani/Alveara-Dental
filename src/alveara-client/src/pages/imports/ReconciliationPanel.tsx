import { useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { getReconciliation, wasCancelled } from "../../services/bulkImportApi";
import type { ImportBatch, Reconciliation } from "../../services/bulkImportApi";
import { ErrorReportButton } from "./ErrorReportButton";
import { money, reasonText } from "./importFormat";

/**
 * What the commit did, counted from the database and not from the screen: rows by status, the records the batch really made against the rows it says it accepted, for balances the ledger total against the batch's own
 * total, and every difference as a plain sentence. "Reconciled" is shown only when there is nothing to explain, in words as well as colour. Nothing from the file is shown.
 */
export function ReconciliationPanel({ batch }: { batch: ImportBatch }) {
  const [report, setReport] = useState<Reconciliation | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getReconciliation(batch.id, controller.signal).then(setReport).catch((e) => { if (!wasCancelled(e)) setFailed(true); });
    return () => controller.abort();
  }, [batch.id]);                                                                       // the parent keys this panel by the commit's state, so a new commit remounts it and counts afresh

  return (
    <section aria-labelledby="import-recon" className="alv-fu-form alv-import__step">
      <h2 id="import-recon">What happened</h2>
      {failed && <ErrorState title="Could not load the reconciliation" description="Reload the page to try again." />}
      {!report && !failed && <LoadingState label="Counting what the commit did…" />}
      {report && (
        <>
          <p>
            <span className={`alv-import__badge alv-import__badge--${report.reconciled ? "ok" : "bad"}`}>{report.reconciled ? "Reconciled" : "Does not reconcile"}</span>{" "}
            {report.reconciled ? "Every row and every record is accounted for." : `${report.problems.length} ${report.problems.length === 1 ? "difference needs" : "differences need"} explaining.`}
          </p>
          {report.problems.length > 0 && <ul className="alv-import__problems" aria-label="Differences">{report.problems.map((p) => <li key={p.code}>{p.message}</li>)}</ul>}
          <table className="alv-fu-table">
            <caption className="alv-fu-sr">Rows by outcome</caption>
            <thead><tr><th scope="col">Rows in the file</th><th scope="col">Accepted</th><th scope="col">Rejected</th><th scope="col">Skipped</th><th scope="col">Waiting for a decision</th><th scope="col">Not committed</th></tr></thead>
            <tbody><tr><td>{report.rowCount}</td><td>{report.accepted}</td><td>{report.rejected}</td><td>{report.skipped}</td><td>{report.unresolved}</td><td>{report.notCommitted}</td></tr></tbody>
          </table>
          <p>{report.accepted} rows were accepted; {report.recordsFound} of them point at a record that exists, and {report.recordsMadeByBatch} records carry this batch's mark.</p>
          {report.ledgerTotal !== null && (
            <p>Balances: valid at the dry-run <strong>{money(report.validAmount ?? 0)}</strong>, loaded <strong>{money(report.acceptedAmount ?? 0)}</strong>, held on the ledgers for this batch <strong>{money(report.ledgerTotal)}</strong>.</p>
          )}
          {report.reasons.filter((r) => r.reason).length > 0 && (
            <details>
              <summary>Why rows were not accepted</summary>
              <ul>{report.reasons.filter((r) => r.reason).map((r) => <li key={`${r.status}-${r.reason}`}>{r.rows} × {r.status}: {reasonText(r.reason)}</li>)}</ul>
            </details>
          )}
          <p>{report.auditEvents} audit {report.auditEvents === 1 ? "entry was" : "entries were"} recorded for this batch.</p>
          {report.rejected + report.skipped + report.unresolved > 0 && <div className="alv-fu-actions"><ErrorReportButton batchId={batch.id} /></div>}
        </>
      )}
    </section>
  );
}
