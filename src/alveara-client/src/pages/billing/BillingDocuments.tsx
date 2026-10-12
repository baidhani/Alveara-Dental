import { useCallback, useEffect, useImperativeHandle, useRef, useState } from "react";
import type { Ref } from "react";
import { newKey } from "../../services/clinicalApi";
import { ApiError } from "../../services/authApi";
import {
  documentCsvUrl, getDocument, isDocumentDetail, isDocumentList, isDocumentPreview, issueDocument, listDocuments, previewDocument,
} from "../../services/billingStatementsApi";
import type { DocumentPreview, IssuedDocument, IssuedDocumentDetail } from "../../services/billingStatementsApi";
import { describeBillingFailure } from "./billingFailure";
import { ReceiptView, StatementView } from "./BillingDocumentView";

interface Props {
  patientId: string;
  canWrite: boolean;
  /** Lets the "Receipt" button on a payment ask for that payment's receipt to be previewed. */
  controlRef?: Ref<BillingDocumentsControl>;
  /** Changes whenever the account changes, so the list of issued documents is reloaded. */
  version: string | null;
}

export interface BillingDocumentsControl { previewReceipt: (paymentEntryNumber: number) => void }

const errorCode = (err: unknown) => (err instanceof ApiError && typeof err.body.error === "string" ? err.body.error : null);

/**
 * ALV-007-C02: statements and receipts. A person previews the document (the ledger read at a cut-off, with the server's content and hash), reviews it, and issues exactly that preview: the issue carries the
 * preview's token, and if the content would now differ the server refuses it as stale, so the screen says so and previews again rather than issuing something that was not reviewed. An issued document is
 * listed by number and shown exactly as it was issued, with what has happened since written apart from it. A statement prints (the page's print style shows only the document) and exports as CSV; a document
 * that can no longer be rebuilt to match what was issued is not shown, and the screen says so.
 */
export function BillingDocuments({ patientId, canWrite, controlRef, version }: Props) {
  const [issued, setIssued] = useState<IssuedDocument[] | null>(null);
  const [preview, setPreview] = useState<DocumentPreview | null>(null);
  const [detail, setDetail] = useState<IssuedDocumentDetail | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; token: string } | null>(null);

  const loadList = useCallback(() => {
    listDocuments(patientId).then((d) => setIssued(isDocumentList(d) ? d : null)).catch(() => setIssued(null));
  }, [patientId]);
  useEffect(() => { loadList(); }, [loadList, version]);

  const startPreview = useCallback(async (type: "Statement" | "Receipt", payment?: number) => {
    setBusy(true);
    setMessage(null);
    setDetail(null);
    try {
      const p = await previewDocument(patientId, type, payment);
      if (!isDocumentPreview(p)) throw new Error("not a preview");
      setPreview(p);
      attempt.current = null;
    } catch (err) {
      setPreview(null);
      setMessage(errorCode(err) === "nothing_owed" ? "Nothing has been billed yet, so there is nothing to put on a statement." : "Could not prepare the preview. Nothing was issued; try again.");
    } finally {
      setBusy(false);
    }
  }, [patientId]);

  useImperativeHandle(controlRef, () => ({ previewReceipt: (payment: number) => { void startPreview("Receipt", payment); } }), [startPreview]);

  async function issue() {
    if (!preview) return;
    if (attempt.current?.token !== preview.previewToken) attempt.current = { key: newKey(), token: preview.previewToken };
    setBusy(true);
    setMessage(null);
    try {
      const doc = await issueDocument(patientId, attempt.current.key, preview.previewToken);
      setPreview(null);
      setMessage(`${doc.documentType} ${doc.documentNumber} issued.`);
      loadList();
      void show(doc.id, true);
    } catch (err) {
      if (errorCode(err) === "document_preview_stale") {
        await startPreview(preview.documentType, preview.receipt?.paymentNumber);
        setMessage("The account changed since this preview was made, so nothing was issued. Review the new preview below and issue that.");
      } else setMessage(describeBillingFailure(err).message.replace("Not recorded", "Not issued"));
    } finally {
      setBusy(false);
    }
  }

  async function show(id: string, keepMessage = false) {
    setBusy(true);
    if (!keepMessage) setMessage(null);
    try {
      const d = await getDocument(patientId, id);
      if (!isDocumentDetail(d)) throw new Error("not a document");
      setDetail(d);
      setPreview(null);
    } catch (err) {
      setDetail(null);
      setMessage(errorCode(err) === "document_content_changed" ? "This document can no longer be shown exactly as it was issued, so it is not shown." : "Could not load the document.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <section aria-labelledby="alv-bill-docs-title">
      <h3 id="alv-bill-docs-title">Statements and receipts</h3>
      <p className="alv-clinical__meta">A statement or receipt is the account's history up to a moment. Preview it, check it, then issue exactly what you reviewed; an issued document never changes.</p>
      <p className="alv-clinical__meta" aria-live="polite">{message}</p>
      <div className="alv-clinical__row-actions">
        <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void startPreview("Statement")}>Preview a statement</button>
      </div>

      {preview && (
        <div className="alv-bill__preview">
          <p className="alv-clinical__meta">Preview: not issued yet.</p>
          <div className="alv-bill__print">
            {preview.statement && <StatementView statement={preview.statement} />}
            {preview.receipt && <ReceiptView receipt={preview.receipt} />}
          </div>
          {canWrite && (
            <div className="alv-clinical__row-actions">
              <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void issue()}>Issue this {preview.documentType.toLowerCase()}</button>
              <button type="button" className="btn btn-outline-secondary" onClick={() => setPreview(null)}>Discard preview</button>
            </div>
          )}
        </div>
      )}

      {detail && (
        <div className="alv-bill__preview">
          <p className="alv-clinical__meta">{detail.document.documentType} {detail.document.documentNumber}, issued {new Date(detail.document.issuedAtUtc).toLocaleString()}{detail.document.issuedByName ? ` by ${detail.document.issuedByName}` : ""}. Shown exactly as issued.</p>
          <div className="alv-bill__print">
            {detail.statement && <StatementView statement={detail.statement} />}
            {detail.receipt && <ReceiptView receipt={detail.receipt} />}
          </div>
          {detail.currentStatus.length > 0 && (
            <div className="alv-bill__notice" role="note" aria-label="Since this document was issued">
              <strong>Since it was issued (not part of the document):</strong>
              <ul>{detail.currentStatus.map((n) => <li key={n}>{n}</li>)}</ul>
            </div>
          )}
          <div className="alv-clinical__row-actions">
            <button type="button" className="btn btn-outline-secondary" onClick={() => window.print()}>Print</button>
            {detail.statement && <a className="alv-workspace__link" href={documentCsvUrl(patientId, detail.document.id)} download>Download CSV</a>}
            <button type="button" className="btn btn-outline-secondary" onClick={() => setDetail(null)}>Close</button>
          </div>
        </div>
      )}

      {issued && issued.length > 0 && (
        <table className="alv-bill__table" aria-label="Issued documents">
          <thead><tr><th scope="col">Document</th><th scope="col">Issued</th><th scope="col">Up to entry</th><th scope="col">By</th><th scope="col">Actions</th></tr></thead>
          <tbody>
            {issued.map((d) => (
              <tr key={d.id}>
                <td>{d.documentType} {d.documentNumber}{d.paymentEntryNumber ? ` (payment ${d.paymentEntryNumber})` : ""}</td>
                <td>{new Date(d.issuedAtUtc).toLocaleString()}</td><td>{d.cutOffs.entryNumber}</td><td>{d.issuedByName ?? "Unknown"}</td>
                <td><button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void show(d.id)}>View {d.documentType.toLowerCase()} {d.documentNumber}</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
