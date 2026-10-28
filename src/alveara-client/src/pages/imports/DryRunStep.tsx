import { useState } from "react";
import { Button } from "../../components/Button";
import { importErrorMessage, runDryRun } from "../../services/bulkImportApi";
import type { ImportBatch } from "../../services/bulkImportApi";
import { ErrorReportButton } from "./ErrorReportButton";
import { ImportRowsTable } from "./ImportRowsTable";
import { money, validRows } from "./importFormat";

/**
 * Step 3: the dry-run. It checks every row exactly as the commit will, writes nothing, and says how many rows are valid, rejected, skipped and unresolved (for balances, also the total to check against the old
 * system). It can be run again after the file's mapping or the practice's rules change. The mapping must be saved first.
 */
export function DryRunStep({ batch, onChanged }: { batch: ImportBatch; onChanged: (batch: ImportBatch) => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const done = batch.dryRunAtUtc !== null;

  const run = async () => {
    setBusy(true); setError(null);
    try { onChanged((await runDryRun(batch.id)).batch); } catch (e) { setError(importErrorMessage(e)); } finally { setBusy(false); }
  };

  return (
    <section aria-labelledby="import-dryrun" className="alv-fu-form alv-import__step">
      <h2 id="import-dryrun">Step 3: dry-run</h2>
      <p>The dry-run checks every row and shows what would happen. <strong>It writes nothing.</strong></p>
      <div className="alv-fu-actions">
        <Button variant="primary" onClick={run} disabled={busy || !batch.mappingConfirmed}>{busy ? "Checking the rows…" : done ? "Run the dry-run again" : "Run the dry-run"}</Button>
        {done && <ErrorReportButton batchId={batch.id} />}
      </div>
      {!batch.mappingConfirmed && <p>Save the column mapping first.</p>}
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {done && (
        <>
          <p role="status" aria-live="polite" className="alv-import__summary">
            {batch.rowCount} rows: <strong>{validRows(batch)} valid</strong>, {batch.rejectedRows} rejected, {batch.skippedRows} skipped, {batch.unresolvedRows} waiting for a decision.
            {batch.sourceKind === "OpeningBalances" && <> Total of the valid balances: <strong>{money(batch.validAmount)}</strong>. Check this against the old system before committing.</>}
          </p>
          <ImportRowsTable key={`${batch.dryRunAtUtc}|${batch.unresolvedRows}|${batch.status}|${batch.acceptedRows}`} batchId={batch.id} />
        </>
      )}
    </section>
  );
}
