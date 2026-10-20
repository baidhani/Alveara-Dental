import { useId, useState } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormField } from "../../components/FormField";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import { editDocument, problemsOf } from "../../services/documentsApi";
import type { CategoryView, DocumentContext, DocumentView } from "../../services/documentsApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import { DocumentContextFields } from "./DocumentContextFields";
import type { ContextErrors } from "./documentContext";

type Errors = Partial<Record<"category" | "documentDate" | "description", string>> & ContextErrors;

/**
 * STORY-010: editing a document's category, date and description. The file itself cannot be changed. The form sends the version it was opened with, so a change made by someone else in between is refused and
 * explained rather than overwritten; a deactivated category the document already has stays selectable so saving other changes does not force a move.
 */
export function DocumentEditForm({ document, categories, encounters, onSaved, onCancel, onReload }: {
  document: DocumentView; categories: CategoryView[]; encounters: EncounterSummary[]; onSaved: () => void; onCancel: () => void; onReload: () => void;
}) {
  const categoryId = useId();
  const descriptionId = useId();
  const [category, setCategory] = useState(document.categoryId);
  const [date, setDate] = useState(document.documentDate);
  const [description, setDescription] = useState(document.description ?? "");
  const [context, setContext] = useState<DocumentContext>({ encounterId: document.encounterId ?? "", toothKey: document.toothKey ?? "", regionKey: document.regionKey ?? "" });
  const [errors, setErrors] = useState<Errors>({});
  const [formError, setFormError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<{ entityType: string; entityId: string } | null>(null);
  const [busy, setBusy] = useState(false);
  const options = categories.filter((c) => c.isActive || c.id === document.categoryId);

  async function save() {
    if (busy) return;
    setBusy(true);
    setErrors({});
    setFormError(null);
    try {
      await editDocument(document.id, { categoryId: category, documentDate: date, description, ...context, rowVersion: document.rowVersion });
      onSaved();
    } catch (err) {
      if (isConcurrencyConflict(err)) { setConflict(err.body); return; }
      if (!(err instanceof ApiError)) { setFormError("Could not reach the server. Nothing was changed. Check your connection and try again."); return; }
      const byField: Errors = {};
      for (const p of problemsOf(err)) if (p.field in { category: 1, documentDate: 1, description: 1, encounter: 1, toothKey: 1, regionKey: 1 }) byField[p.field as keyof Errors] = p.message;
      setErrors(byField);
      if (Object.keys(byField).length === 0) setFormError(err.message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-documents__edit" aria-label="Edit document" noValidate onSubmit={(e) => { e.preventDefault(); void save(); }}>
      <p className="alv-documents__edit-title">Editing {document.originalFileName}</p>
      {conflict && <ConcurrencyConflictBanner problem={{ error: "concurrency_conflict", ...conflict }} onReload={onReload} />}
      <div className="alv-form-field">
        <label htmlFor={categoryId} className="alv-form-field__label">Category</label>
        <select
          id={categoryId}
          className={`alv-form-field__input${errors.category ? " alv-form-field__input--error" : ""}`}
          value={category}
          aria-invalid={errors.category ? true : undefined}
          aria-describedby={errors.category ? `${categoryId}-error` : undefined}
          onChange={(e) => setCategory(e.target.value)}
        >
          {options.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
        </select>
        {errors.category && <p id={`${categoryId}-error`} className="alv-form-field__error" role="alert">{errors.category}</p>}
      </div>
      <FormField label="Date of document" type="date" value={date} error={errors.documentDate} onChange={(e) => setDate(e.target.value)} />
      <DocumentContextFields value={context} onChange={setContext} encounters={encounters} errors={errors} />
      <div className="alv-form-field alv-documents__wide">
        <label htmlFor={descriptionId} className="alv-form-field__label">Description (optional)</label>
        <textarea
          id={descriptionId}
          className={`alv-form-field__input alv-documents__textarea${errors.description ? " alv-form-field__input--error" : ""}`}
          rows={2}
          value={description}
          aria-invalid={errors.description ? true : undefined}
          aria-describedby={errors.description ? `${descriptionId}-error` : undefined}
          onChange={(e) => setDescription(e.target.value)}
        />
        {errors.description && <p id={`${descriptionId}-error`} className="alv-form-field__error" role="alert">{errors.description}</p>}
      </div>
      {formError && <p className="alv-form-field__error alv-documents__wide" role="alert">{formError}</p>}
      <div className="alv-documents__actions alv-documents__wide">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Saving…" : "Save changes"}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
