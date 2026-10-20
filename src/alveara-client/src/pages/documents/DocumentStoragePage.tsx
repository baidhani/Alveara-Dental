import { useCallback, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { PageHeader } from "../../components/PageHeader";
import { ApiError } from "../../services/authApi";
import { checkDocumentStorage, getStorageSummary, removeOrphans, runStorageCheck } from "../../services/documentsApi";
import type { OrphanRemovalResult, StorageSummary } from "../../services/documentsApi";
import { formatBytes, formatDateTime } from "./documentText";
import "./Documents.css";

const MAX_REMOVAL = 500;
const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
const statusWord = (s: string) => (s === "Corrupt" ? "Damaged" : s);

/** What happened to each file a reviewer asked to remove, in words. */
function removalMessage(r: OrphanRemovalResult): string {
  const count = (outcome: string) => r.results.filter((x) => x.outcome === outcome).length;
  const parts = [`Removed ${plural(r.removed, "file", "files")}.`];
  const tooNew = count("TooNew"), gone = count("AlreadyGone"), used = count("StillReferenced"), failed = count("Failed");
  if (tooNew) parts.push(`${tooNew} ${tooNew === 1 ? "was" : "were"} too new to remove (it may be an import in progress).`);
  if (gone) parts.push(`${gone} ${gone === 1 ? "was" : "were"} already gone.`);
  if (used) parts.push(`${used} ${used === 1 ? "is" : "are"} now used by a document and ${used === 1 ? "was" : "were"} kept.`);
  if (failed) parts.push(`${failed} could not be removed and ${failed === 1 ? "was" : "were"} kept.`);
  return parts.join(" ");
}

/**
 * ALV-010-C01: the storage page for staff who manage the document library. It shows how many stored files were found intact, missing or damaged at the last check, lists the files that need attention (with a way
 * to check one again and to go to the patient's documents), runs a check of every file on request, and lists the stored files that no document points to so a reviewer can select and remove them after a confirmation.
 * Nothing is removed without a selection and a confirmation, every outcome is reported in words, and a failure is shown as a failure. The server decides who may do any of this; the permission only decides what is drawn.
 */
export function DocumentStoragePage() {
  const [summary, setSummary] = useState<StorageSummary | null>(null);
  const [failed, setFailed] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<"check" | "remove" | null>(null);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [confirming, setConfirming] = useState(false);

  const load = useCallback(async () => {
    try {
      setSummary(await getStorageSummary());
      setFailed(false);
    } catch {
      setFailed(true);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  async function run<T>(kind: "check" | "remove", action: () => Promise<T>, done: (result: T) => string) {
    if (busy) return;
    setBusy(kind); setMessage(null); setError(null);
    try {
      const result = await action();
      setMessage(done(result));
      await load();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the server. Nothing was changed. Check your connection and try again.");
    } finally {
      setBusy(null);
    }
  }

  if (failed && summary === null) return <ErrorState title="Could not load the document storage" action={<Button onClick={() => { setFailed(false); void load(); }}>Retry</Button>} />;
  if (summary === null) return <LoadingState label="Loading document storage…" />;

  const orphans = summary.orphans;
  const toggle = (id: string) => setSelected((s) => { const next = new Set(s); if (next.has(id)) next.delete(id); else next.add(id); return next; });
  const chosen = orphans.filter((o) => selected.has(o.blobId)).map((o) => o.blobId);
  const tooMany = chosen.length > MAX_REMOVAL;

  return (
    <div>
      <PageHeader title="Document storage" description="Whether the stored files of documents are present and intact, and the stored files that no document points to." />
      {message && <p className="alv-documents__status" role="status">{message}</p>}
      {error && <p className="alv-form-field__error" role="alert">{error}</p>}

      <section className="alv-documents__section" aria-label="Status">
        <h2 className="alv-workspace__section-title">Status of the stored files</h2>
        <table className="alv-documents__table" aria-label="Storage status">
          <tbody>
            <tr><th scope="row">Documents</th><td>{summary.documents}</td></tr>
            <tr><th scope="row">Intact</th><td>{summary.ok}</td></tr>
            <tr><th scope="row">Missing</th><td>{summary.missing}</td></tr>
            <tr><th scope="row">Damaged</th><td>{summary.corrupt}</td></tr>
            <tr><th scope="row">Not checked yet</th><td>{summary.neverChecked}</td></tr>
          </tbody>
        </table>
        <div className="alv-documents__inline">
          <Button type="button" variant="primary" disabled={busy !== null}
            onClick={() => void run("check", runStorageCheck, (r) => `Checked ${plural(r.checked, "document", "documents")}: ${r.ok} intact, ${r.missing} missing, ${r.corrupt} damaged.${r.skipped ? ` ${r.skipped} could not be read just now and was left as it was.` : ""}`)}>
            {busy === "check" ? "Checking…" : "Check all stored files"}
          </Button>
          <p className="alv-workspace__note">The check reads every stored file, so it takes time in proportion to the library. Run it when the practice is quiet.</p>
        </div>
      </section>

      <section className="alv-documents__section" aria-label="Files that need attention">
        <h2 className="alv-workspace__section-title">Files that need attention</h2>
        {summary.problems.length === 0 ? <EmptyState title="No file needs attention" description="Every file the last check looked at was found intact." /> : (
          <table className="alv-documents__table" aria-label="Files that need attention">
            <thead><tr><th scope="col">File</th><th scope="col">Result</th><th scope="col">Checked</th><th scope="col">Actions</th></tr></thead>
            <tbody>
              {summary.problems.map((p) => (
                <tr key={p.documentId}>
                  <td>{p.fileName}</td>
                  <td>{statusWord(p.status)}</td>
                  <td>{formatDateTime(p.checkedAtUtc)}</td>
                  <td className="alv-documents__row-actions">
                    <Button type="button" aria-label={`Check ${p.fileName} again`} disabled={busy !== null}
                      onClick={() => void run("check", () => checkDocumentStorage(p.documentId), (r) => `${p.fileName} is ${r.status === "Ok" ? "intact" : r.status === "Missing" ? "missing" : "damaged"}.`)}>Check again</Button>
                    <Link to={`/patients/${p.patientId}/documents`} aria-label={`Open documents of the patient for ${p.fileName}`}>Open the patient's documents</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="alv-documents__section" aria-label="Unreferenced files">
        <h2 className="alv-workspace__section-title">Stored files nothing points to</h2>
        <p className="alv-workspace__note">These files are on disk but no document uses them, for example after an import was interrupted. Only files older than an hour are listed, so an import in progress is never shown. Review them, select the ones to remove and confirm; nothing is removed automatically.</p>
        {orphans.length === 0 ? <EmptyState title="No stored file is unreferenced" /> : (
          <>
            <ul className="alv-documents__types" aria-label="Stored files nothing points to">
              {orphans.map((o) => (
                <li key={o.blobId}>
                  <label>
                    <input type="checkbox" checked={selected.has(o.blobId)} onChange={() => toggle(o.blobId)} /> <span className="alv-documents__type">{o.blobId}</span>
                  </label>
                  <span>{formatBytes(o.sizeBytes)}, last written {formatDateTime(o.lastWriteUtc)}</span>
                </li>
              ))}
            </ul>
            <div className="alv-documents__actions">
              <Button type="button" onClick={() => setSelected(new Set(orphans.map((o) => o.blobId)))}>Select all</Button>
              <Button type="button" onClick={() => setSelected(new Set())}>Select none</Button>
              <Button type="button" variant="danger" disabled={chosen.length === 0 || tooMany || busy !== null} onClick={() => setConfirming(true)}>Remove selected files</Button>
            </div>
            {tooMany && <p className="alv-form-field__error">Select at most {MAX_REMOVAL} files at a time.</p>}
            {confirming && (
              <div className="alv-documents__duplicate" role="alertdialog" aria-labelledby="alv-orphans-confirm" aria-describedby="alv-orphans-confirm-text">
                <p id="alv-orphans-confirm" className="alv-documents__duplicate-title">Remove {plural(chosen.length, "stored file", "stored files")}?</p>
                <p id="alv-orphans-confirm-text">The files are deleted from the server and this cannot be undone. Each one is checked again just before it is removed, and any that a document now uses is kept.</p>
                <div className="alv-documents__actions">
                  <Button type="button" variant="danger" disabled={busy !== null}
                    onClick={() => { setConfirming(false); void run("remove", () => removeOrphans(chosen), (r) => { setSelected(new Set()); return removalMessage(r); }); }}>
                    Remove {plural(chosen.length, "file", "files")}
                  </Button>
                  <Button type="button" onClick={() => setConfirming(false)}>Cancel</Button>
                </div>
              </div>
            )}
          </>
        )}
      </section>
    </div>
  );
}
