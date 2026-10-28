import { useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { decideRow, importErrorMessage, listRows, wasCancelled } from "../../services/bulkImportApi";
import type { ImportBatch, ImportRow, RowDecision } from "../../services/bulkImportApi";

/**
 * The duplicate queue (patient files). A row that looks like someone already registered waits here for a person: "different person" lets it be registered, "same person" skips it. Nothing is ever merged here. The
 * matched records open in a new tab so the person can compare before deciding; without the permission to open patient records the queue is read-only and says why, because deciding without looking is exactly
 * the mistake this queue exists to prevent.
 */
export function DecisionQueue({ batch, onChanged }: { batch: ImportBatch; onChanged: (batch: ImportBatch) => void }) {
  const { hasPermission } = useAuth();
  const canCompare = hasPermission("ViewPatientRecords");
  const [rows, setRows] = useState<ImportRow[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [busyRow, setBusyRow] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState("");

  useEffect(() => {
    const controller = new AbortController();
    listRows(batch.id, { status: "Unresolved", limit: 50 }, controller.signal)
      .then((page) => { setRows(page); setFailed(false); })
      .catch((e) => { if (!wasCancelled(e)) setFailed(true); });
    return () => controller.abort();
  }, [batch.id, batch.dryRunAtUtc]);

  const decide = async (row: ImportRow, decision: RowDecision) => {
    setBusyRow(row.rowNumber); setError(null);
    try {
      const result = await decideRow(batch.id, row.rowNumber, decision);
      setRows((current) => current?.filter((r) => r.rowNumber !== row.rowNumber) ?? null);
      setNotice(`Row ${row.rowNumber}: ${decision === "Skip" ? "recorded as the same person, it will be skipped" : "recorded as a different person, it will be registered"}.`);
      onChanged(result.batch);
    } catch (e) {
      setError(importErrorMessage(e));
    } finally {
      setBusyRow(null);
    }
  };

  return (
    <section aria-labelledby="import-decisions" className="alv-fu-form alv-import__step">
      <h2 id="import-decisions">Likely duplicates</h2>
      <p>These rows look like patients who are already registered. Open the matching records and decide for each. Nothing is merged.</p>
      {!canCompare && <p className="alv-import__warning">You cannot open patient records, so you cannot compare these rows with the registered patients. Ask someone who can to decide them.</p>}
      {failed && <ErrorState title="Could not load the rows waiting for a decision" description="Reload the page to try again." />}
      {!rows && !failed && <LoadingState label="Loading the rows waiting for a decision…" />}
      {rows?.length === 0 && <p>No rows are waiting for a decision.</p>}
      {rows?.map((row) => (
        <div key={row.rowNumber} className="alv-import__decision">
          <h3>Row {row.rowNumber}</h3>
          <p>{row.matchedPatientIds.length === 1 ? "May be this registered patient:" : "May be one of these registered patients:"}</p>
          <ul>
            {row.matchedPatientIds.map((id, i) => (
              <li key={id}>{canCompare ? <a href={`/patients/${id}`} target="_blank" rel="noopener noreferrer">Open registered patient {i + 1} (new tab)</a> : `Registered patient ${i + 1}`}</li>
            ))}
          </ul>
          <div className="alv-fu-actions">
            <Button onClick={() => decide(row, "RegisterAsNew")} disabled={!canCompare || busyRow !== null}>This is a different person: register them</Button>
            <Button onClick={() => decide(row, "Skip")} disabled={!canCompare || busyRow !== null}>This is the same person: skip the row</Button>
          </div>
        </div>
      ))}
      <p role="status" aria-live="polite" className="alv-import__note">{notice}</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
    </section>
  );
}
