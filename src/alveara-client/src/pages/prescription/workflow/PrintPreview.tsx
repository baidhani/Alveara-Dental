import { useEffect, useRef, useState } from "react";
import { ApiError } from "../../../services/authApi";
import { getPrint } from "../../../services/prescriptionWorkflowApi";
import type { PrintContent } from "../../../services/prescriptionWorkflowApi";
import { statusWords } from "./workflowRules";

const day = (iso: string) => new Date(iso).toLocaleDateString();

/** The prescription as it prints: every line is the text the server built from the immutable record (nothing is worked out here), and a cancelled, discontinued or superseded prescription says so on its face. */
export function PrescriptionPrint({ content }: { content: PrintContent }) {
  return (
    <article className="alv-rx__printable" aria-label="Prescription">
      {content.practice && <p className="alv-rx__practice"><strong>{content.practice.name}</strong>{content.practice.addressLine ? <><br />{content.practice.addressLine}</> : null}{content.practice.phone ? <><br />{content.practice.phone}</> : null}</p>}
      <h4>Prescription</h4>
      {content.status !== "Active" && <p className="alv-rx__status" role="note"><strong>{statusWords(content.status)}</strong></p>}
      <p>For: {content.patientName} (born {day(content.patientDateOfBirth)})<br />Prescribed by: {content.prescriberName} on {day(content.prescribedAtUtc)}</p>
      <dl className="alv-rx__summary">
        <dt>Medication</dt><dd>{content.medication}</dd>
        <dt>Dose</dt><dd>{content.dose}, {content.frequency.toLowerCase()}</dd>
        <dt>Instructions</dt><dd className="alv-rx__note">{content.instructions}</dd>
        <dt>Quantity</dt><dd>{content.quantity}</dd>
        <dt>Refills</dt><dd>{content.refills}</dd>
      </dl>
      <p className="alv-clinical__meta">{content.allergyStatement}</p>
      <p className="alv-clinical__meta"><strong>Not checked:</strong> {content.limitationStatement}</p>
      <p className="alv-clinical__meta">Document reference {content.contentHash.slice(0, 12)}</p>
    </article>
  );
}

interface Props { prescriptionId: string; onClose: () => void }

/**
 * ALV-016-C01: the print preview of a finalized prescription. The server builds the content and records that it was prepared (who, when; never the contents); if it cannot, or if the prescription has nothing complete
 * to print, the screen says so and nothing is shown as printable. "Print" opens the browser's print dialog; if that cannot be opened the screen says so rather than appearing to have printed.
 */
export function PrintPreview({ prescriptionId, onClose }: Props) {
  const [content, setContent] = useState<PrintContent | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [note, setNote] = useState<string | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    let live = true;
    getPrint(prescriptionId).then((c) => { if (live) setContent(c); }).catch((err) => {
      if (live) setError(err instanceof ApiError ? err.message : "The prescription could not be prepared for printing: the connection dropped. Nothing was printed. Try again.");
    });
    return () => { live = false; };
  }, [prescriptionId]);
  useEffect(() => { heading.current?.focus(); }, []);

  function print() {
    try { window.print(); setNote(null); } catch { setNote("The print dialog could not be opened. Nothing was printed."); }
  }

  return (
    <section className="alv-rx__preview" aria-labelledby="alv-rx-print-title">
      <h3 id="alv-rx-print-title" ref={heading} tabIndex={-1} className="alv-rx__noprint">Print preview</h3>
      {error && <p role="alert" className="alv-rx__message alv-rx__noprint">{error} Nothing was printed.</p>}
      {!content && !error && <p className="alv-clinical__meta alv-rx__noprint">Preparing the prescription…</p>}
      {content && <PrescriptionPrint content={content} />}
      {note && <p role="alert" className="alv-rx__message alv-rx__noprint">{note}</p>}
      <div className="alv-clinical__row-actions alv-rx__noprint">
        <button type="button" className="btn btn-primary" disabled={!content} onClick={print}>Print</button>
        <button type="button" className="btn btn-outline-secondary" onClick={onClose}>Close preview</button>
      </div>
    </section>
  );
}
