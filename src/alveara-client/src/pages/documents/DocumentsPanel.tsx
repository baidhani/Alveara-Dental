import { useCallback, useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { contentUrl, listCategories, listLibrary, problemsOf, reactivateDocument } from "../../services/documentsApi";
import type { CategoryView, DocumentLibrary, LibraryFilter } from "../../services/documentsApi";
import { listEncounters } from "../../services/clinicalApi";
import type { EncounterSummary } from "../../services/clinicalApi";
import type { PatientDetail } from "../../services/patientsApi";
import { DocumentDetailsPanel } from "./DocumentDetailsPanel";
import { DocumentEditForm } from "./DocumentEditForm";
import { DocumentFilters } from "./DocumentFilters";
import type { FilterErrors } from "./documentFilter";
import { DocumentGrid, DocumentTable } from "./DocumentEntries";
import type { EntryHandlers } from "./DocumentEntries";
import { DocumentImportForm } from "./DocumentImportForm";
import { DocumentInactivateForm } from "./DocumentInactivateForm";
import "./Documents.css";

const FILTER_FIELDS: Record<string, keyof FilterErrors> = { category: "category", encounter: "encounter", toothKey: "toothKey", regionKey: "regionKey", from: "from", to: "to", text: "text", status: "status" };

/**
 * STORY-010 and ALV-010-C01: the patient workspace's Documents tab - imported documents and signed forms in one library, each labelled for what it is. The library can be filtered (category, encounter, tooth or
 * region, dates, status, a word), shown as a list or as thumbnails, and each document can be previewed (pictures), opened or downloaded, read (its history, with every inactivation and its reason) and, by staff
 * who may import, edited, made inactive (never deleted) and reactivated. Signed forms are read-only entries that link to the form. Wording and states are truthful: a failed load is an error, never an empty list.
 */
export function DocumentsPanel({ patient, canImport }: { patient: PatientDetail; canImport: boolean }) {
  const [library, setLibrary] = useState<DocumentLibrary | null>(null);
  const [categories, setCategories] = useState<CategoryView[]>([]);
  const [encounters, setEncounters] = useState<EncounterSummary[]>([]);
  const [filter, setFilter] = useState<LibraryFilter>({});
  const [filterErrors, setFilterErrors] = useState<FilterErrors>({});
  const [view, setView] = useState<"list" | "grid">("list");
  const [failed, setFailed] = useState(false);
  const [version, setVersion] = useState(0);
  const [editing, setEditing] = useState<string | null>(null);
  const [inactivating, setInactivating] = useState<string | null>(null);
  const [previewing, setPreviewing] = useState<string | null>(null);
  const [details, setDetails] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const controller = new AbortController();
    listCategories(true, controller.signal).then(setCategories).catch(() => { if (!controller.signal.aborted) setCategories([]); });
    listEncounters(patient.id, controller.signal).then((e) => setEncounters(Array.isArray(e) ? e : [])).catch(() => { if (!controller.signal.aborted) setEncounters([]); });
    return () => controller.abort();
  }, [patient.id]);

  useEffect(() => {
    const controller = new AbortController();
    listLibrary(patient.id, filter, controller.signal)
      .then((l) => { setLibrary(l); setFailed(false); setFilterErrors({}); })
      .catch((err) => {
        if (controller.signal.aborted) return;
        const problems = err instanceof ApiError && err.code === "metadata_invalid" ? problemsOf(err) : [];
        if (problems.length > 0) { setFilterErrors(Object.fromEntries(problems.filter((p) => FILTER_FIELDS[p.field]).map((p) => [FILTER_FIELDS[p.field], p.message]))); return; }
        setFailed(true);
      });
    return () => controller.abort();
  }, [patient.id, filter, version]);

  const reload = useCallback(() => setVersion((v) => v + 1), []);
  const find = (id: string | null) => (id ? library?.entries.find((e) => e.id === id && e.document) ?? null : null);
  const editingEntry = find(editing);
  const inactivatingEntry = find(inactivating);
  const previewEntry = find(previewing);

  async function reactivate(id: string) {
    const entry = find(id);
    if (!entry?.document) return;
    setMessage(null); setError(null); setConflict(null);
    try {
      await reactivateDocument(id, entry.document.rowVersion);
      setMessage(`${entry.title} is active again.`);
      reload();
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setError(err instanceof ApiError ? err.message : "Could not reach the server. Nothing was changed. Check your connection and try again.");
    }
  }

  const open = (set: (id: string | null) => void, id: string) => { setMessage(null); setEditing(null); setInactivating(null); setPreviewing(null); setDetails(null); set(id); };
  const handlers: EntryHandlers = {
    canEdit: canImport,
    onEdit: (id) => open(setEditing, id),
    onInactivate: (id) => open(setInactivating, id),
    onReactivate: (id) => void reactivate(id),
    onPreview: (id) => open(setPreviewing, id),
    onDetails: (id) => open(setDetails, id),
  };

  return (
    <section aria-labelledby="alv-documents-title">
      <h2 id="alv-documents-title" className="alv-workspace__section-title">Documents</h2>
      {failed && <ErrorState title="Could not load the documents" action={<Button onClick={reload}>Retry</Button>} />}
      {!failed && library === null && <LoadingState label="Loading documents…" />}
      {library && !failed && (
        <>
          {canImport && (
            <DocumentImportForm patientId={patient.id} categories={categories.filter((c) => c.isActive)} encounters={encounters} maxBytes={library.maxUploadBytes} onImported={reload} />
          )}
          <DocumentFilters categories={categories} encounters={encounters} errors={filterErrors} onApply={(f) => { setFilterErrors({}); setFilter(f); }} />
          <fieldset className="alv-documents__view" role="radiogroup" aria-label="View">
            <legend className="alv-documents__view-legend">View</legend>
            <label><input type="radio" name="alv-documents-view" checked={view === "list"} onChange={() => setView("list")} /> List</label>
            <label><input type="radio" name="alv-documents-view" checked={view === "grid"} onChange={() => setView("grid")} /> Thumbnails</label>
          </fieldset>
          {message && <p className="alv-documents__status" role="status">{message}</p>}
          {error && <p className="alv-form-field__error" role="alert">{error}</p>}
          {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => { setConflict(null); reload(); }} />}
          {editingEntry?.document && (
            <DocumentEditForm
              key={editingEntry.document.id + editingEntry.document.rowVersion}
              document={editingEntry.document}
              categories={categories}
              encounters={encounters}
              onSaved={() => { setMessage(`Saved changes to ${editingEntry.title}.`); setEditing(null); reload(); }}
              onCancel={() => setEditing(null)}
              onReload={() => { setEditing(null); reload(); }}
            />
          )}
          {inactivatingEntry?.document && (
            <DocumentInactivateForm
              key={inactivatingEntry.document.id + inactivatingEntry.document.rowVersion}
              document={inactivatingEntry.document}
              onDone={() => { setMessage(`${inactivatingEntry.title} is now inactive.`); setInactivating(null); reload(); }}
              onCancel={() => setInactivating(null)}
              onReload={() => { setInactivating(null); reload(); }}
            />
          )}
          {previewEntry && (
            <section className="alv-documents__preview" aria-label={`Preview of ${previewEntry.title}`}>
              <div className="alv-documents__actions"><h3 className="alv-documents__details-title">{previewEntry.title}</h3><Button type="button" onClick={() => setPreviewing(null)}>Close preview</Button></div>
              <img src={contentUrl(previewEntry.id)} alt={previewEntry.title} className="alv-documents__preview-image" />
            </section>
          )}
          {details && library.entries.find((e) => e.id === details) && (
            <DocumentDetailsPanel documentId={details} title={library.entries.find((e) => e.id === details)!.title} onClose={() => setDetails(null)} />
          )}
          {library.entries.length === 0 ? (
            <EmptyState title="No documents yet" description="Imported documents and signed forms for this patient appear here." />
          ) : view === "list" ? (
            <DocumentTable entries={library.entries} patientId={patient.id} encounters={encounters} handlers={handlers} />
          ) : (
            <DocumentGrid entries={library.entries} patientId={patient.id} encounters={encounters} handlers={handlers} />
          )}
        </>
      )}
    </section>
  );
}
