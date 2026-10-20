import { useEffect, useId, useState } from "react";
import { Link } from "react-router-dom";
import { Button } from "../../components/Button";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { listCategories, listLibrary, contentUrl } from "../../services/documentsApi";
import type { CategoryView, DocumentLibrary, LibraryEntry } from "../../services/documentsApi";
import type { PatientDetail } from "../../services/patientsApi";
import { DocumentEditForm } from "./DocumentEditForm";
import { DocumentImportForm } from "./DocumentImportForm";
import { INLINE_TYPES, formatBytes, formatDate, formatDateTime } from "./documentText";
import "./Documents.css";

/**
 * STORY-010: the patient workspace's Documents tab - imported documents and signed forms in one list, each labelled for what it is. Signed forms are read-only entries that link to the form; documents can be
 * opened (only the types the server allows the browser to show) or downloaded, and by staff who may import they can be edited. Wording and states are truthful: a failed load is an error, never an empty list.
 */
export function DocumentsPanel({ patient, canImport }: { patient: PatientDetail; canImport: boolean }) {
  const filterId = useId();
  const [library, setLibrary] = useState<DocumentLibrary | null>(null);
  const [categories, setCategories] = useState<CategoryView[]>([]);
  const [filter, setFilter] = useState("");
  const [failed, setFailed] = useState(false);
  const [version, setVersion] = useState(0);
  const [editing, setEditing] = useState<string | null>(null);
  const [saved, setSaved] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    listCategories(true, controller.signal).then(setCategories).catch(() => { if (!controller.signal.aborted) setCategories([]); });
    return () => controller.abort();
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    listLibrary(patient.id, filter || undefined, controller.signal)
      .then((l) => { setLibrary(l); setFailed(false); })
      .catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [patient.id, filter, version]);

  const reload = () => setVersion((v) => v + 1);
  const editingEntry = library?.entries.find((e) => e.id === editing && e.document);

  return (
    <section aria-labelledby="alv-documents-title">
      <h2 id="alv-documents-title" className="alv-workspace__section-title">Documents</h2>
      {failed && <ErrorState title="Could not load the documents" action={<Button onClick={reload}>Retry</Button>} />}
      {!failed && library === null && <LoadingState label="Loading documents…" />}
      {library && !failed && (
        <>
          {canImport && (
            <DocumentImportForm patientId={patient.id} categories={categories.filter((c) => c.isActive)} maxBytes={library.maxUploadBytes} onImported={reload} />
          )}
          <div className="alv-documents__filter alv-form-field">
            <label htmlFor={filterId} className="alv-form-field__label">Filter by category</label>
            <select id={filterId} className="alv-form-field__input" value={filter} onChange={(e) => setFilter(e.target.value)}>
              <option value="">All categories</option>
              {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          {saved && <p className="alv-documents__status" role="status">{saved}</p>}
          {editingEntry?.document && (
            <DocumentEditForm
              key={editingEntry.document.id + editingEntry.document.rowVersion}
              document={editingEntry.document}
              categories={categories}
              onSaved={() => { setSaved(`Saved changes to ${editingEntry.title}.`); setEditing(null); reload(); }}
              onCancel={() => setEditing(null)}
              onReload={() => { setEditing(null); reload(); }}
            />
          )}
          {library.entries.length === 0 ? (
            <EmptyState title="No documents yet" description="Imported documents and signed forms for this patient appear here." />
          ) : (
            <table className="alv-documents__table" aria-label="Documents">
              <thead>
                <tr><th scope="col">Name</th><th scope="col">Category</th><th scope="col">Date</th><th scope="col">Size</th><th scope="col">Imported by</th><th scope="col">Actions</th></tr>
              </thead>
              <tbody>
                {library.entries.map((entry) => <Row key={`${entry.kind}-${entry.id}`} entry={entry} patientId={patient.id} canEdit={canImport} onEdit={() => { setSaved(null); setEditing(entry.id); }} />)}
              </tbody>
            </table>
          )}
        </>
      )}
    </section>
  );
}

function Row({ entry, patientId, canEdit, onEdit }: { entry: LibraryEntry; patientId: string; canEdit: boolean; onEdit: () => void }) {
  const isForm = entry.kind === "SignedForm";
  return (
    <tr>
      <td>
        {entry.title}
        {entry.isDuplicate && <span className="alv-documents__badge">Duplicate</span>}
        {entry.isVoided && <span className="alv-documents__badge alv-documents__badge--void">Voided</span>}
      </td>
      <td>{entry.categoryName}</td>
      <td>{formatDate(entry.date)}</td>
      <td>{entry.sizeBytes === null ? "—" : formatBytes(entry.sizeBytes)}</td>
      <td title={formatDateTime(entry.atUtc)}>{entry.importedByName ?? "—"}</td>
      <td className="alv-documents__row-actions">
        {isForm ? (
          <Link to={`/patients/${patientId}/forms/${entry.patientFormId}`} aria-label={`View form ${entry.title}`}>View form</Link>
        ) : (
          <>
            {INLINE_TYPES.has(entry.detectedType) && (
              <a href={contentUrl(entry.id)} target="_blank" rel="noopener noreferrer" aria-label={`Open ${entry.title}`}>Open</a>
            )}
            <a href={contentUrl(entry.id, true)} aria-label={`Download ${entry.title}`}>Download</a>
            {canEdit && <Button type="button" onClick={onEdit} aria-label={`Edit ${entry.title}`}>Edit</Button>}
          </>
        )}
      </td>
    </tr>
  );
}
