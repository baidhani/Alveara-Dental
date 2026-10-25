import { useCallback, useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { listLibrary } from "../../services/documentsApi";
import { caseErrorMessage, getCaseDocuments, linkCaseDocument, unlinkCaseDocument } from "../../services/externalCasesApi";
import type { CaseDocument, ExternalCase } from "../../services/externalCasesApi";
import { formatDay } from "./externalCaseFormat";
import "../followups/followups.css";

interface Props { c: ExternalCase; canManage: boolean; onChanged: () => void }

/**
 * ALV-009-C02: the documents linked to a case. They are the patient's own library documents shown here by reference, never copies. A link whose document can no longer be shown is listed with the reason (inactive, file
 * missing, failed its integrity check) rather than dropped. People who may manage the case can link another of the patient's active documents or remove a link (the document itself stays in the library). A failed load says it
 * could not load rather than showing an empty list.
 */
export function CaseDocumentsSection({ c, canManage, onChanged }: Props) {
  const [docs, setDocs] = useState<CaseDocument[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [options, setOptions] = useState<{ id: string; title: string }[]>([]);
  const [choice, setChoice] = useState("");
  const [status, setStatus] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const open = c.status !== "Completed" && c.status !== "Cancelled";

  const load = useCallback((signal?: AbortSignal) => {
    getCaseDocuments(c.kind, c.id, signal).then((d) => { setDocs(d); setFailed(false); }).catch(() => { if (!signal?.aborted) setFailed(true); });
  }, [c.kind, c.id]);
  useEffect(() => { const controller = new AbortController(); load(controller.signal); return () => controller.abort(); }, [load]);

  useEffect(() => {
    if (!canManage || !open) return;
    const controller = new AbortController();
    listLibrary(c.patientId, { status: "Active" }, controller.signal)
      .then((lib) => setOptions(lib.entries.filter((e) => e.kind === "Document" && e.document).map((e) => ({ id: e.document!.id, title: `${e.title} (${e.categoryName}, ${e.date})` }))))
      .catch(() => { /* the picker simply stays empty; linking is not offered without the library */ });
    return () => controller.abort();
  }, [canManage, open, c.patientId]);

  async function link() {
    if (!choice) return;
    setError(null); setStatus(null);
    try { await linkCaseDocument(c.kind, c.id, choice); setChoice(""); setStatus("Document linked."); load(); onChanged(); }
    catch (err) { setError(caseErrorMessage(err)); }
  }

  async function unlink(d: CaseDocument) {
    setError(null); setStatus(null);
    try { await unlinkCaseDocument(c.kind, c.id, d.documentId); setStatus(`Removed the link to ${d.fileName}. The document is still in the library.`); load(); onChanged(); }
    catch (err) { setError(caseErrorMessage(err)); }
  }

  const linked = new Set((docs ?? []).map((d) => d.documentId));
  return (
    <section aria-labelledby="alv-ec-docs-title" className="alv-fu-docs">
      <h2 id="alv-ec-docs-title">Linked documents</h2>
      <p className="alv-clinical__status" role="status" aria-live="polite">{status ?? ""}</p>
      {error && <p role="alert" className="alv-fu-error">{error}</p>}
      {failed ? <p role="alert" className="alv-fu-error">Could not load the linked documents. Do not assume there are none.</p>
        : docs === null ? <p>Loading the linked documents…</p>
        : docs.length === 0 ? <p>No documents are linked to this case.</p> : (
          <ul className="alv-fu-list" aria-label="Linked documents">
            {docs.map((d) => (
              <li key={d.documentId}>
                <strong>{d.fileName}</strong> <span className="alv-fu-item__meta">{d.category}, {formatDay(d.documentDate)}{d.description ? `, ${d.description}` : ""}</span>
                {d.broken && <p role="note" className="alv-fu-error">Broken link: {d.brokenReason}</p>}
                {canManage && open && <Button type="button" onClick={() => unlink(d)} aria-label={`Remove the link to ${d.fileName}`}>Remove link</Button>}
              </li>
            ))}
          </ul>
        )}
      {canManage && open && (
        <div className="alv-form-field">
          <label htmlFor="alv-ec-doc-pick" className="alv-form-field__label">Link another of this patient&apos;s documents</label>
          <select id="alv-ec-doc-pick" value={choice} onChange={(e) => setChoice(e.target.value)}>
            <option value="">Choose a document…</option>
            {options.filter((o) => !linked.has(o.id)).map((o) => <option key={o.id} value={o.id}>{o.title}</option>)}
          </select>{" "}
          <Button type="button" onClick={link} disabled={!choice}>Link document</Button>
        </div>
      )}
    </section>
  );
}
