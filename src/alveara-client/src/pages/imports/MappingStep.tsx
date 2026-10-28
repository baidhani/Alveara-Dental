import { useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { getMappingSuggestion, importErrorMessage, problemsOf, saveMapping, wasCancelled } from "../../services/bulkImportApi";
import type { ImportBatch, ImportProblem, MappingSuggestion } from "../../services/bulkImportApi";

/**
 * Step 2: which column of the file fills which field. The suggestion is only a starting point: the person confirms it, and the server checks it against the file's own header and the practice's rules, so a
 * mapping that does not work comes back with a precise problem rather than being saved. Only column names are shown, never a value from the file. Saving a new mapping makes an earlier dry-run stale.
 */
export function MappingStep({ batch, onSaved }: { batch: ImportBatch; onSaved: () => void }) {
  const [suggestion, setSuggestion] = useState<MappingSuggestion | null>(null);
  const [mapping, setMapping] = useState<Record<string, string>>({});
  const [loadFailed, setLoadFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problems, setProblems] = useState<ImportProblem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    getMappingSuggestion(batch.id, controller.signal)
      .then((s) => { setSuggestion(s); setMapping({ ...(s.current ?? s.suggested) }); setLoadFailed(false); })
      .catch((e) => { if (!wasCancelled(e)) setLoadFailed(true); });
    return () => controller.abort();
  }, [batch.id]);

  const save = async () => {
    setBusy(true); setProblems([]); setError(null); setSaved(false);
    try {
      const result = await saveMapping(batch.id, Object.fromEntries(Object.entries(mapping).map(([column, field]) => [column, field || null])));
      setSuggestion(result); setSaved(true); onSaved();
    } catch (e) {
      setProblems(problemsOf(e)); setError(importErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  if (loadFailed) return <ErrorState title="Could not load the column mapping" description="Reload the page to try again." />;
  if (!suggestion) return <LoadingState label="Loading the column mapping…" />;

  const used = new Set(Object.values(mapping).filter(Boolean));
  const missing = suggestion.fields.filter((f) => f.required && !used.has(f.key));
  const ignored = suggestion.columns.filter((c) => !mapping[c]);

  return (
    <section aria-labelledby="import-mapping" className="alv-fu-form alv-import__step">
      <h2 id="import-mapping">Step 2: match the columns</h2>
      <p>{batch.mappingConfirmed ? "This mapping is saved. Change it if a column is in the wrong place; you will then run the dry-run again." : "Check which field each column fills, then save. Fields marked required must be filled."}</p>
      <table className="alv-fu-table">
        <caption className="alv-fu-sr">Column to field</caption>
        <thead><tr><th scope="col">Column in the file</th><th scope="col">Fills this field</th></tr></thead>
        <tbody>
          {suggestion.columns.map((column) => (
            <tr key={column}>
              <th scope="row">{column}</th>
              <td>
                <select aria-label={`Field for column ${column}`} value={mapping[column] ?? ""} onChange={(e) => setMapping({ ...mapping, [column]: e.target.value })} disabled={busy}>
                  <option value="">Ignore this column</option>
                  {suggestion.fields.map((f) => <option key={f.key} value={f.key}>{f.label}{f.required ? " (required)" : ""}</option>)}
                </select>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {missing.length > 0 && <p className="alv-import__warning">Not filled yet and required: {missing.map((f) => f.label).join(", ")}.</p>}
      {ignored.length > 0 && <p>These columns will be ignored: {ignored.join(", ")}.</p>}
      <div className="alv-fu-actions"><Button variant="primary" onClick={save} disabled={busy}>{busy ? "Saving…" : "Save the mapping"}</Button></div>
      {saved && <p role="status">Mapping saved.</p>}
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {problems.length > 0 && <ul className="alv-import__problems" aria-label="Problems with the mapping">{problems.map((p, i) => <li key={i}>{p.column ? `Column “${p.column}”: ` : ""}{p.message}</li>)}</ul>}
    </section>
  );
}
