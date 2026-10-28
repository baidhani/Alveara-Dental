import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { SafeLink } from "../../components/SafeLink";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { importErrorMessage, listBatches, problemsOf, sweepOrphanFiles, uploadBatch, wasCancelled } from "../../services/bulkImportApi";
import type { ImportBatch, ImportKind, ImportProblem, SweepResult } from "../../services/bulkImportApi";
import { formatMoment } from "../exports/exportFormat";
import { kindLabel, statusOf } from "./importFormat";
import "../followups/followups.css";
import "./imports.css";

const KINDS: { value: ImportKind; label: string; hint: string }[] = [
  { value: "Patients", label: "Patients", hint: "Load these first: appointments and balances refer to patients who are already registered." },
  { value: "Appointments", label: "Appointments", hint: "Future appointments only. Each is checked against the schedule exactly as a booking made at the front desk would be." },
  { value: "OpeningBalances", label: "Opening balances", hint: "Each patient's amount owed becomes the first entry on their billing ledger. Needs the Manage billing permission to commit." },
];

/**
 * ALV-018-C02: "Data import". The person chooses what the file holds, says where it came from and sends the CSV. The server checks the WHOLE file first: if anything is wrong nothing is stored and every problem is listed
 * here with its place in the file, so the file can be corrected and chosen again. A good file becomes a batch and the page opens it. Below, every batch with its state in words, and the clean-up of stray files.
 */
export function ImportBatchesPage() {
  const navigate = useNavigate();
  const [batches, setBatches] = useState<ImportBatch[] | null>(null);
  const [loadFailed, setLoadFailed] = useState(false);
  const [kind, setKind] = useState<ImportKind>("Patients");
  const [label, setLabel] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [problems, setProblems] = useState<ImportProblem[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [confirmingSweep, setConfirmingSweep] = useState(false);
  const [sweep, setSweep] = useState<SweepResult | null>(null);
  const [sweepError, setSweepError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);

  const load = async (signal?: AbortSignal) => {
    try { setBatches(await listBatches(50, undefined, signal)); setLoadFailed(false); } catch (e) { if (!wasCancelled(e)) setLoadFailed(true); }
  };
  useEffect(() => {
    const controller = new AbortController();
    listBatches(50, undefined, controller.signal)
      .then((loaded) => { setBatches(loaded); setLoadFailed(false); })
      .catch((e) => { if (!wasCancelled(e)) setLoadFailed(true); });
    return () => controller.abort();
  }, []);

  const send = async () => {
    if (!file) return;
    setBusy(true); setProblems([]); setError(null);
    try {
      const result = await uploadBatch(kind, label, file);
      navigate(`/admin/data-import/${result.batch.id}`);
    } catch (e) {
      setProblems(problemsOf(e));
      setError(importErrorMessage(e));
    } finally {
      setBusy(false);
    }
  };

  const runSweep = async () => {
    setConfirmingSweep(false); setSweepError(null);
    try { setSweep(await sweepOrphanFiles()); } catch (e) { setSweepError(importErrorMessage(e)); }
  };

  return (
    <div className="alv-import">
      <PageHeader title="Data import" description="Bring patients, appointments and opening balances in from another system. The whole file is checked first, and nothing is written until you have seen the results and confirmed." />

      <section aria-labelledby="import-upload" className="alv-fu-form">
        <h2 id="import-upload">Send a file</h2>
        <fieldset className="alv-import__group">
          <legend>What the file holds</legend>
          {KINDS.map((k) => (
            <label key={k.value}><input type="radio" name="import-kind" checked={kind === k.value} onChange={() => setKind(k.value)} disabled={busy} /> {k.label}</label>
          ))}
          <p className="alv-import__hint">{KINDS.find((k) => k.value === kind)!.hint}</p>
        </fieldset>
        <label htmlFor="import-label">Where the file came from (for example, “Old system export, October”)</label>
        <input id="import-label" type="text" value={label} maxLength={100} onChange={(e) => setLabel(e.target.value)} disabled={busy} />
        <label htmlFor="import-file">CSV file (.csv, first row is the column names)</label>
        <input id="import-file" ref={input} type="file" accept=".csv,text/csv" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setProblems([]); setError(null); }} disabled={busy} />
        <div className="alv-fu-actions">
          <Button variant="primary" onClick={send} disabled={!file || !label.trim() || busy}>{busy ? "Checking the file…" : "Check and upload the file"}</Button>
        </div>
        {error && <p role="alert" className="alv-fu-error">{error}</p>}
        {problems.length > 0 && (
          <ul className="alv-import__problems" aria-label="Problems in the file">
            {problems.map((p, i) => (
              <li key={i}>{[p.row ? `Row ${p.row}` : "The file", p.column ? `column “${p.column}”` : null].filter(Boolean).join(", ")}: {p.message}</li>
            ))}
          </ul>
        )}
      </section>

      <section aria-labelledby="import-history">
        <div className="alv-fu-toolbar">
          <h2 id="import-history">Batches</h2>
          <Button onClick={() => load()}>Refresh</Button>
        </div>
        {loadFailed && <ErrorState title="Could not load the batches" action={<Button onClick={() => load()}>Retry</Button>} />}
        {!batches && !loadFailed && <LoadingState label="Loading the batches…" />}
        {batches?.length === 0 && <p>No files have been sent yet.</p>}
        {!!batches?.length && (
          <table className="alv-fu-table">
            <caption className="alv-fu-sr">Import batches, newest first</caption>
            <thead><tr><th scope="col">Sent</th><th scope="col">By</th><th scope="col">Source</th><th scope="col">Holds</th><th scope="col">Rows</th><th scope="col">State</th></tr></thead>
            <tbody>
              {batches.map((b) => {
                const state = statusOf(b);
                return (
                  <tr key={b.id}>
                    <td>{formatMoment(b.createdAtUtc, null)}</td>
                    <td>{b.createdBy}</td>
                    <td><SafeLink to={`/admin/data-import/${b.id}`}>{b.sourceLabel}</SafeLink></td>
                    <td>{kindLabel(b.sourceKind)}</td>
                    <td>{b.rowCount}</td>
                    <td><span className={`alv-import__badge alv-import__badge--${state.tone}`}>{state.label}</span></td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </section>

      <section aria-labelledby="import-sweep" className="alv-import__sweep">
        <h2 id="import-sweep">Stray files</h2>
        <p>If the server stopped in the middle of an upload, a file may be left on disk that no batch uses. This removes such files that are more than a day old. A batch's own file is never touched.</p>
        {confirmingSweep ? (
          <div className="alv-fu-actions">
            <Button variant="danger" onClick={runSweep}>Yes, remove stray files</Button>
            <Button onClick={() => setConfirmingSweep(false)}>Cancel</Button>
          </div>
        ) : (
          <div className="alv-fu-actions"><Button onClick={() => { setSweep(null); setConfirmingSweep(true); }}>Remove stray files</Button></div>
        )}
        <p className="alv-clinical__status" role="status" aria-live="polite">
          {sweep ? `Looked at ${sweep.found} file(s): removed ${sweep.removed}, kept ${sweep.kept}${sweep.failed ? `, could not remove ${sweep.failed}` : ""}.${sweep.batchesMissingFile ? ` ${sweep.batchesMissingFile} open batch(es) have lost their file; upload them again.` : ""}` : ""}
        </p>
        {sweepError && <p role="alert" className="alv-fu-error">{sweepError}</p>}
      </section>
    </div>
  );
}
