import { useCallback, useEffect, useId, useImperativeHandle, useRef, useState } from "react";
import type { Ref } from "react";
import { newKey } from "../../services/clinicalApi";
import { ApiError } from "../../services/authApi";
import {
  documentCsvUrl, getDocument, isDocumentDetail, isDocumentList, isDocumentPreview, issueDocument, listDocuments, previewDocument,
} from "../../services/billingStatementsApi";
import type { DocumentPreview, IssuedDocument, IssuedDocumentDetail } from "../../services/billingStatementsApi";
import { documentStatus } from "./documentStatus";
import { describeBillingFailure } from "./billingFailure";
import { ReceiptView, StatementView } from "./BillingDocumentView";
import { showInstant } from "../../time/practiceTime";

interface Props {
  patientId: string;
  canWrite: boolean;
  /** Lets the "Receipt" button on a payment ask for that payment's receipt. */
  controlRef?: Ref<BillingDocumentsControl>;
  /** Changes whenever the account changes, so the list of issued documents is reloaded. */
  version: string | null;
  /** ALV-N024 (F-207): called when a document action starts, so the page can clear an older status line. */
  onAction?: () => void;
}

export interface BillingDocumentsControl { previewReceipt: (paymentEntryNumber: number) => void }

const errorCode = (err: unknown) => (err instanceof ApiError && typeof err.body.error === "string" ? err.body.error : null);
const errorText = (err: unknown) => (err instanceof ApiError && typeof err.body.message === "string" ? err.body.message : null);

/**
 * ALV-007-C02: statements and receipts. A person previews the document (the ledger read at a cut-off, with the server's content and hash), reviews it, and issues exactly that preview: the issue carries the
 * preview's token, and if the content would now differ the server refuses it as stale, so the screen says so and previews again rather than issuing something that was not reviewed. An issued document is
 * listed by number and shown exactly as it was issued, with what has happened since written apart from it. A statement prints (the page's print style shows only the document) and exports as CSV; a document
 * that can no longer be rebuilt to match what was issued is not shown, and the screen says so.
 *
 * ALV-N024 (PD-17): a payment has one receipt. Asking for a payment's receipt when it has one opens that receipt; another is issued only as a deliberate replacement with a reason ("Issue a
 * replacement"), and the list then says which receipt superseded which. The preview or opened document is scrolled into view and takes focus (F-113), and its number is printed with it (Q-5av).
 */
export function BillingDocuments({ patientId, canWrite, controlRef, version, onAction }: Props) {
  const id = useId();
  const [issued, setIssued] = useState<IssuedDocument[] | null>(null);
  const [preview, setPreview] = useState<DocumentPreview | null>(null);
  const [replacing, setReplacing] = useState<IssuedDocument | null>(null);
  const [reason, setReason] = useState("");
  const [reasonError, setReasonError] = useState<string | null>(null);
  const [detail, setDetail] = useState<IssuedDocumentDetail | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const attempt = useRef<{ key: string; token: string } | null>(null);
  const shown = useRef<HTMLDivElement | null>(null);

  const loadList = useCallback(() => {
    listDocuments(patientId).then((d) => setIssued(isDocumentList(d) ? d : null)).catch(() => setIssued(null));
  }, [patientId]);
  useEffect(() => { loadList(); }, [loadList, version]);

  // F-113: whatever was just opened is brought into view and focused, so the click never seems to do nothing
  useEffect(() => {
    if (!preview && !detail) return;
    shown.current?.scrollIntoView?.({ block: "start" });
    shown.current?.focus();
  }, [preview, detail]);

  const show = useCallback(async (docId: string, keepMessage = false) => {
    setBusy(true);
    if (!keepMessage) setMessage(null);
    try {
      const d = await getDocument(patientId, docId);
      if (!isDocumentDetail(d)) throw new Error("not a document");
      setDetail(d);
      setPreview(null);
    } catch (err) {
      setDetail(null);
      setMessage(errorCode(err) === "document_content_changed" ? "This document can no longer be shown exactly as it was issued, so it is not shown." : "Could not load the document.");
    } finally {
      setBusy(false);
    }
  }, [patientId]);

  const startPreview = useCallback(async (type: "Statement" | "Receipt", payment?: number, replaces?: IssuedDocument) => {
    onAction?.();
    setBusy(true);
    setMessage(null);
    setDetail(null);
    setReplacing(replaces ?? null);
    setReason("");
    setReasonError(null);
    try {
      const p = await previewDocument(patientId, type, payment, undefined, replaces?.id);
      if (!isDocumentPreview(p)) throw new Error("not a preview");
      setPreview(p);
      attempt.current = null;
    } catch (err) {
      setPreview(null);
      const code = errorCode(err);
      setMessage(code === "nothing_owed" ? "Nothing has been billed yet, so there is nothing to put on a statement."
        : code === "receipt_exists" || code === "receipt_replaced" ? errorText(err) ?? "This payment already has a receipt."
          : "Could not prepare the preview. Nothing was issued; try again.");
    } finally {
      setBusy(false);
    }
  }, [patientId, onAction]);

  /** A payment that already has a receipt opens it; only one with none gets a preview. */
  const receiptFor = useCallback((payment: number) => {
    const open = (issued ?? []).filter((d) => d.documentType === "Receipt" && d.paymentEntryNumber === payment && !d.supersededByNumber)
      .sort((a, b) => b.documentNumber - a.documentNumber)[0];
    if (!open) { void startPreview("Receipt", payment); return; }
    onAction?.();
    setMessage(`Payment ${payment} already has receipt ${open.documentNumber}, shown below. To issue another, use "Issue a replacement" and say why.`);
    void show(open.id, true);
  }, [issued, startPreview, show, onAction]);

  useImperativeHandle(controlRef, () => ({ previewReceipt: receiptFor }), [receiptFor]);

  async function issue() {
    if (!preview) return;
    if (replacing && reason.trim() === "") { setReasonError("Say why."); return; }
    setReasonError(null);
    if (attempt.current?.token !== preview.previewToken) attempt.current = { key: newKey(), token: preview.previewToken };
    onAction?.();
    setBusy(true);
    setMessage(null);
    try {
      const doc = await issueDocument(patientId, attempt.current.key, preview.previewToken, replacing ? reason.trim() : undefined);
      setPreview(null);
      setReplacing(null);
      setMessage(doc.replacesNumber ? `Receipt ${doc.documentNumber} issued; it replaces receipt ${doc.replacesNumber}.` : `${doc.documentType} ${doc.documentNumber} issued.`);
      loadList();
      void show(doc.id, true);
    } catch (err) {
      const code = errorCode(err);
      if (code === "document_preview_stale") {
        await startPreview(preview.documentType, preview.receipt?.paymentNumber, replacing ?? undefined);
        setMessage("The account changed since this preview was made, so nothing was issued. Review the new preview below and issue that.");
      } else if (code === "receipt_busy") setMessage("Receipts are being issued for this payment right now, so nothing was issued. Try again in a moment.");
      else if (code === "receipt_exists" || code === "receipt_replaced") { setPreview(null); setMessage(`${errorText(err) ?? "This payment already has a receipt."} Nothing was issued.`); loadList(); }
      else setMessage(describeBillingFailure(err).message.replace("Not recorded", "Not issued"));
    } finally {
      setBusy(false);
    }
  }

  const current = detail ? (issued ?? []).find((d) => d.id === detail.document.id) ?? detail.document : null;

  return (
    <section aria-labelledby="alv-bill-docs-title">
      <h3 id="alv-bill-docs-title">Statements and receipts</h3>
      <p className="alv-clinical__meta">A statement or receipt is the account's history up to a moment. Preview it, check it, then issue exactly what you reviewed; an issued document never changes. A payment has one receipt; another is issued only as a replacement, with a reason.</p>
      <p className="alv-clinical__meta" aria-live="polite">{message}</p>
      <div className="alv-clinical__row-actions">
        <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void startPreview("Statement")}>Preview a statement</button>
      </div>

      {preview && (
        <div className="alv-bill__preview" ref={shown} tabIndex={-1} aria-label={replacing ? `Preview of a replacement for receipt ${replacing.documentNumber}` : `Preview of a ${preview.documentType.toLowerCase()}`}>
          <p className="alv-clinical__meta">Preview: not issued yet.{replacing ? ` It will replace receipt ${replacing.documentNumber}.` : ""}</p>
          <div className="alv-bill__print">
            {preview.statement && <StatementView statement={preview.statement} />}
            {preview.receipt && <ReceiptView receipt={preview.receipt} />}
          </div>
          {canWrite && replacing && (
            <div className="alv-dx__field">
              <label htmlFor={`${id}-why`}>Why is receipt {replacing.documentNumber} being replaced?</label>
              <input id={`${id}-why`} type="text" maxLength={500} value={reason} aria-invalid={reasonError ? true : undefined} aria-describedby={reasonError ? `${id}-why-e` : undefined}
                onChange={(e) => { setReason(e.target.value); setReasonError(null); }} />
              {reasonError && <p id={`${id}-why-e`} className="alv-bill__error">{reasonError}</p>}
            </div>
          )}
          {canWrite && (
            <div className="alv-clinical__row-actions">
              <button type="button" className="btn btn-primary" disabled={busy} onClick={() => void issue()}>{replacing ? `Issue the replacement for receipt ${replacing.documentNumber}` : `Issue this ${preview.documentType.toLowerCase()}`}</button>
              <button type="button" className="btn btn-outline-secondary" onClick={() => { setPreview(null); setReplacing(null); }}>Discard preview</button>
            </div>
          )}
        </div>
      )}

      {detail && current && (
        <div className="alv-bill__preview" ref={shown} tabIndex={-1} aria-label={`${detail.document.documentType} ${detail.document.documentNumber}`}>
          <p className="alv-clinical__meta">Issued {showInstant(detail.document.issuedAtUtc)}{detail.document.issuedByName ? ` by ${detail.document.issuedByName}` : ""}. Shown exactly as issued.</p>
          <div className="alv-bill__print">
            <p className="alv-bill__docnumber"><strong>{detail.document.documentType} {detail.document.documentNumber}</strong>{current.replacesNumber ? ` (replaces receipt ${current.replacesNumber})` : ""}{current.supersededByNumber ? ` - superseded by receipt ${current.supersededByNumber}` : ""}</p>
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
            {canWrite && detail.receipt && !current.supersededByNumber && !current.paymentReversed && (
              <button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => void startPreview("Receipt", detail.receipt!.paymentNumber, current)}>Issue a replacement for receipt {current.documentNumber}…</button>
            )}
            <button type="button" className="btn btn-outline-secondary" onClick={() => setDetail(null)}>Close</button>
          </div>
        </div>
      )}

      {issued && issued.length > 0 && (
        <table className="alv-bill__table" aria-label="Issued documents">
          <thead><tr><th scope="col">Document</th><th scope="col">Issued</th><th scope="col">Up to entry</th><th scope="col">By</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
          <tbody>
            {issued.map((d) => (
              <tr key={d.id}>
                <td>{d.documentType} {d.documentNumber}{d.paymentEntryNumber ? ` (payment ${d.paymentEntryNumber})` : ""}</td>
                <td>{showInstant(d.issuedAtUtc)}</td><td>{d.cutOffs.entryNumber}</td><td>{d.issuedByName ?? "Unknown"}</td>
                <td>{documentStatus(d).join("; ")}</td>
                <td><button type="button" className="btn btn-outline-secondary" disabled={busy} onClick={() => { onAction?.(); void show(d.id); }}>View {d.documentType.toLowerCase()} {d.documentNumber}</button></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
