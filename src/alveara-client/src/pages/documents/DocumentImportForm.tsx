import { useId, useRef, useState } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { ApiError } from "../../services/authApi";
import { duplicateOf, importDocument, newKey, problemsOf } from "../../services/documentsApi";
import type { CategoryView, DocumentContext, DuplicateSummary } from "../../services/documentsApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { DocumentContextFields } from "./DocumentContextFields";
import type { ContextErrors } from "./documentContext";
import { FILE_ERROR_CODES, formatBytes, formatDate, formatDateTime, todayValue } from "./documentText";

type Errors = Partial<Record<"file" | "category" | "documentDate" | "description", string>> & ContextErrors;

/**
 * STORY-010: the import form. Every wrong entry is named beside its box and what was typed stays; one attempt keeps one save key, so a retry after a failure can never import the file twice, and a changed
 * entry starts a new attempt. A file the patient already has is warned about, naming the document that exists, and is added only by pressing "Add anyway", which sends the same file again with the confirmation.
 */
export function DocumentImportForm({ patientId, categories, encounters, maxBytes, onImported }: { patientId: string; categories: CategoryView[]; encounters: EncounterSummary[]; maxBytes: number; onImported: () => void }) {
  const categoryId = useId();
  const descriptionId = useId();
  const [file, setFile] = useState<File | null>(null);
  const [fileVersion, setFileVersion] = useState(0);
  const [category, setCategory] = useState("");
  const [date, setDate] = useState(todayValue());
  const [description, setDescription] = useState("");
  const [context, setContext] = useState<DocumentContext>({ encounterId: "", toothKey: "", regionKey: "" });
  const [progress, setProgress] = useState<number | null>(null);
  const abort = useRef<AbortController | null>(null);
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [status, setStatus] = useState<string | null>(null);
  const [duplicate, setDuplicate] = useState<DuplicateSummary | null>(null);
  const [busy, setBusy] = useState(false);
  const key = useRef<string | null>(null);

  const changed = <T,>(set: (v: T) => void) => (v: T) => { key.current = null; setDuplicate(null); set(v); };

  async function send(confirmDuplicateOf?: string) {
    if (busy) return;
    const found: Errors = {};
    if (!file) found.file = "Choose a file to import.";
    else if (file.size > maxBytes) found.file = `That file is larger than the ${formatBytes(maxBytes)} limit. Choose a smaller file.`;
    if (!category) found.category = "Choose a category.";
    if (!date) found.documentDate = "Enter the date of the document.";
    setErrors(found);
    setFormError(null);
    setStatus(null);
    if (Object.keys(found).length > 0 || !file) return;

    setBusy(true);
    setProgress(0);
    abort.current = new AbortController();
    key.current ??= newKey();
    try {
      const result = await importDocument(patientId, { file, categoryId: category, documentDate: date, description, ...context, key: key.current, confirmDuplicateOf }, setProgress, abort.current.signal);
      setStatus(`Imported ${result.document.originalFileName}.`);
      setFile(null); setFileVersion((v) => v + 1); setDescription(""); setDuplicate(null);
      key.current = null;
      onImported();
    } catch (err) {
      const existing = duplicateOf(err);
      if (existing) { setDuplicate(existing); return; }
      setDuplicate(null);
      if (err instanceof DOMException && err.name === "AbortError") { setFormError("The upload was cancelled. Nothing was imported. You can import the file again."); return; }
      if (!(err instanceof ApiError)) { setFormError("Could not reach the server. Nothing was imported. Check your connection and try again."); return; }
      const problems = problemsOf(err);
      const byField: Errors = {};
      for (const p of problems) if (p.field in { category: 1, documentDate: 1, description: 1, encounter: 1, toothKey: 1, regionKey: 1 }) byField[p.field as keyof Errors] = p.message;
      if (FILE_ERROR_CODES.has(err.code)) byField.file = err.message;
      setErrors(byField);
      if (Object.keys(byField).length === 0) setFormError(err.message);
    } finally {
      setBusy(false);
      setProgress(null);
      abort.current = null;
    }
  }

  return (
    <form className="alv-documents__import" aria-label="Import a document" noValidate onSubmit={(e) => { e.preventDefault(); void send(); }}>
      <FormField
        key={fileVersion}
        label="File"
        type="file"
        hint={`Largest file: ${formatBytes(maxBytes)}. Programs and web pages cannot be imported.`}
        error={errors.file}
        onChange={(e) => changed(setFile)(e.target.files?.[0] ?? null)}
        disabled={busy}
      />
      <div className="alv-form-field">
        <label htmlFor={categoryId} className="alv-form-field__label">Category</label>
        <select
          id={categoryId}
          className={`alv-form-field__input${errors.category ? " alv-form-field__input--error" : ""}`}
          value={category}
          aria-invalid={errors.category ? true : undefined}
          aria-describedby={errors.category ? `${categoryId}-error` : undefined}
          onChange={(e) => changed(setCategory)(e.target.value)}
          disabled={busy}
        >
          <option value="">Choose a category…</option>
          {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        {errors.category && <p id={`${categoryId}-error`} className="alv-form-field__error" role="alert">{errors.category}</p>}
      </div>
      <FormField label="Date of document" type="date" value={date} error={errors.documentDate} onChange={(e) => changed(setDate)(e.target.value)} disabled={busy} />
      <DocumentContextFields value={context} onChange={changed(setContext)} encounters={encounters} errors={errors} disabled={busy} />
      <div className="alv-form-field alv-documents__wide">
        <label htmlFor={descriptionId} className="alv-form-field__label">Description (optional)</label>
        <textarea
          id={descriptionId}
          className={`alv-form-field__input alv-documents__textarea${errors.description ? " alv-form-field__input--error" : ""}`}
          rows={2}
          value={description}
          aria-invalid={errors.description ? true : undefined}
          aria-describedby={errors.description ? `${descriptionId}-error` : undefined}
          onChange={(e) => changed(setDescription)(e.target.value)}
          disabled={busy}
        />
        {errors.description && <p id={`${descriptionId}-error`} className="alv-form-field__error" role="alert">{errors.description}</p>}
      </div>

      {duplicate && (
        <div className="alv-documents__duplicate" role="alert" aria-labelledby={`${descriptionId}-dup`}>
          <p id={`${descriptionId}-dup`} className="alv-documents__duplicate-title">This patient already has this file</p>
          <p>
            {duplicate.categoryName}, dated {formatDate(duplicate.documentDate)}, imported by {duplicate.importedByName} on {formatDateTime(duplicate.importedAtUtc)}. Nothing has been imported yet.
          </p>
          <div className="alv-documents__actions">
            <Button type="button" variant="primary" onClick={() => void send(duplicate.documentId)} disabled={busy}>Add anyway</Button>
            <Button type="button" onClick={() => setDuplicate(null)} disabled={busy}>Cancel</Button>
          </div>
        </div>
      )}
      {progress !== null && (
        <div className="alv-documents__wide alv-documents__progress">
          <progress aria-label="Upload progress" value={progress} max={1} />
          <span>Uploading… {Math.round(progress * 100)}%</span>
          <Button type="button" onClick={() => abort.current?.abort()}>Cancel upload</Button>
        </div>
      )}
      {formError && <p className="alv-form-field__error alv-documents__wide" role="alert">{formError}</p>}
      {status && <p className="alv-documents__status alv-documents__wide" role="status">{status}</p>}
      <div className="alv-documents__actions alv-documents__wide">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Importing…" : "Import document"}</Button>
      </div>
    </form>
  );
}
