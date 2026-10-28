import { useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { importErrorMessage, listRows, wasCancelled } from "../../services/bulkImportApi";
import type { ImportRow, RowStatus } from "../../services/bulkImportApi";
import { reasonText } from "./importFormat";

const PAGE = 50;
const FILTERS: (RowStatus | "")[] = ["", "Valid", "Rejected", "Unresolved", "Skipped", "Accepted"];

/**
 * What the dry-run (or the commit) decided about each row, a page at a time and filterable by status. A row shows its status and the reason in words, and a rejected row lists each field with the front desk's own
 * fixed message; nothing from the file is repeated. `refreshKey` changes whenever the batch does, so the table never shows results from an older dry-run.
 */
export function ImportRowsTable({ batchId }: { batchId: string }) {
  const [filter, setFilter] = useState<RowStatus | "">("");
  const [rows, setRows] = useState<ImportRow[] | null>(null);
  const [more, setMore] = useState(false);
  const [failed, setFailed] = useState<string | null>(null);
  const [loadingMore, setLoadingMore] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    listRows(batchId, { status: filter || undefined, limit: PAGE }, controller.signal)
      .then((page) => { setRows(page); setMore(page.length === PAGE); })
      .catch((e) => { if (!wasCancelled(e)) setFailed(importErrorMessage(e)); });
    return () => controller.abort();
  }, [batchId, filter]);                                                                // the parent keys this table by the batch's state, so new results remount it

  const loadMore = async () => {
    if (!rows?.length) return;
    setLoadingMore(true);
    try {
      const page = await listRows(batchId, { status: filter || undefined, limit: PAGE, after: rows[rows.length - 1].rowNumber });
      setRows([...rows, ...page]); setMore(page.length === PAGE);
    } catch (e) {
      setFailed(importErrorMessage(e));
    } finally {
      setLoadingMore(false);
    }
  };

  return (
    <div className="alv-import__rows">
      <div className="alv-fu-toolbar">
        <h3>Row results</h3>
        <label>Show <select value={filter} onChange={(e) => { setRows(null); setFailed(null); setFilter(e.target.value as RowStatus | ""); }}>
          {FILTERS.map((f) => <option key={f} value={f}>{f || "All rows"}</option>)}
        </select></label>
      </div>
      {failed && <ErrorState title="Could not load the row results" description={failed} />}
      {!rows && !failed && <LoadingState label="Loading the row results…" />}
      {rows?.length === 0 && <p>No rows with this status.</p>}
      {!!rows?.length && (
        <table className="alv-fu-table">
          <caption className="alv-fu-sr">Row results</caption>
          <thead><tr><th scope="col">Row</th><th scope="col">Status</th><th scope="col">Why</th><th scope="col">Details</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.rowNumber}>
                <th scope="row">{r.rowNumber}</th>
                <td>{r.status}</td>
                <td>{reasonText(r.reason)}</td>
                <td>
                  {r.errors && <ul>{Object.entries(r.errors).map(([field, message]) => <li key={field}>{field}: {message}</li>)}</ul>}
                  {r.duplicateOfRow && <span>Same person as row {r.duplicateOfRow}. </span>}
                  {r.matchedPatientIds.length > 0 && <span>Matches {r.matchedPatientIds.length} registered {r.matchedPatientIds.length === 1 ? "patient" : "patients"}.</span>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {more && <div className="alv-fu-actions"><Button onClick={loadMore} disabled={loadingMore}>{loadingMore ? "Loading…" : "Load more rows"}</Button></div>}
    </div>
  );
}
