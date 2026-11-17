import { useEffect, useId, useState } from "react";
import { listLibrary } from "../../services/documentsApi";
import { formatDate } from "../documents/documentText";

interface Option { id: string; text: string }

/**
 * ALV-N022 A3 (F-090, D-1 B): choose one of THIS patient's active documents. Only the patient's own documents that are not inactive are offered (the server checks the same and
 * the database refuses another patient's document). The document date is a calendar date, so it is shown as the Documents tab shows it, never through a time zone. Nothing is typed as free text any more.
 */
export function DocumentPicker({ patientId, label, value, onChange, error, optional = false }:
  { patientId: string; label: string; value: string; onChange: (id: string) => void; error?: string; optional?: boolean }) {
  const id = useId();
  const [options, setOptions] = useState<Option[] | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    listLibrary(patientId, {}, controller.signal)
      .then((lib) => setOptions(lib.entries.filter((e) => e.kind === "Document" && !e.isInactive).map((e) => ({ id: e.id, text: `${e.title} (${formatDate(e.date)})` }))))
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patientId]);

  if (failed) return <p className="alv-form-field__error" role="alert">This patient&apos;s documents could not be loaded. Try again.</p>;
  if (options === null) return <p className="alv-clinical__meta">Loading this patient&apos;s documents…</p>;
  if (options.length === 0) return <p className="alv-clinical__meta">This patient has no active documents. Import the document on the Documents tab first{optional ? ", or leave it for later" : ""}.</p>;
  return (
    <div className="alv-form-field">
      <label htmlFor={id} className="alv-form-field__label">{label}</label>
      <select id={id} value={value} onChange={(e) => onChange(e.target.value)} aria-invalid={error ? true : undefined} aria-describedby={error ? `${id}-error` : undefined}>
        <option value="">{optional ? "Not yet available" : "Choose a document"}</option>
        {options.map((o) => <option key={o.id} value={o.id}>{o.text}</option>)}
      </select>
      {error && <p id={`${id}-error`} className="alv-form-field__error">{error}</p>}
    </div>
  );
}
