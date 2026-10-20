import { useCallback, useEffect, useState } from "react";
import type { ReactNode } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormField } from "../../components/FormField";
import { PageHeader } from "../../components/PageHeader";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { addBlocked, addCategory, editCategory, getSettings, listBlocked, listCategories, problemsOf, removeBlocked, setSizeLimit } from "../../services/documentsApi";
import type { BlockedExtension, CategoryView, DocumentSettings } from "../../services/documentsApi";
import "./Documents.css";

const MB = 1024 * 1024;
type Load = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; categories: CategoryView[]; settings: DocumentSettings; blocked: BlockedExtension[] };
type Field = "name" | "limit" | "extension";

/**
 * STORY-010: the administrator's Document settings: which categories documents can be filed under (renamed or stopped, never deleted; at least one stays in use), the largest file that can be imported, and
 * the file types nobody can import (the built-in ones are shown as locked and offer no way to remove them). Each change is sent as it is made with the version that was read; a refusal is shown where it
 * happened with what was typed kept, and a change made by someone else is explained and reloaded on request. The server decides what is allowed; this screen only draws the controls.
 */
export function DocumentSettingsPage() {
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [status, setStatus] = useState<string | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<Field, string>>>({});
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [busy, setBusy] = useState(false);
  const [newCategory, setNewCategory] = useState("");
  const [renaming, setRenaming] = useState<{ id: string; text: string } | null>(null);
  const [limit, setLimit] = useState("");
  const [newType, setNewType] = useState("");

  const reload = useCallback(async () => {
    try {
      const [categories, settings, blocked] = await Promise.all([listCategories(true), getSettings(), listBlocked()]);
      setLoad({ kind: "loaded", categories, settings, blocked });
      setLimit(String(Math.round(settings.maxUploadBytes / MB)));
    } catch {
      setLoad({ kind: "error" });
    }
  }, []);

  useEffect(() => { void reload(); }, [reload]);

  /** Runs one change: clears the old messages, reloads what the server now holds on success, and shows a refusal where it belongs. */
  async function act(change: () => Promise<string>, field?: Field) {
    if (busy) return;
    setBusy(true);
    setStatus(null);
    setActionError(null);
    setFieldErrors({});
    setConflict(null);
    try {
      const done = await change();
      await reload();
      setStatus(done);
    } catch (err) {
      if (isConcurrencyConflict(err)) { setConflict(err.body); return; }
      if (!(err instanceof ApiError)) { setActionError("Could not reach the server. Nothing was changed. Check your connection and try again."); return; }
      const problem = problemsOf(err)[0];
      if (field && (problem || err.status === 409)) setFieldErrors({ [field]: problem?.message ?? err.message });
      else setActionError(err.message);
    } finally {
      setBusy(false);
    }
  }

  if (load.kind === "loading") return <LoadingState label="Loading document settings…" />;
  if (load.kind === "error") return <ErrorState title="Could not load the document settings" action={<Button onClick={() => { setLoad({ kind: "loading" }); void reload(); }}>Retry</Button>} />;

  const { categories, settings, blocked } = load;
  const minMb = Math.round(settings.minAllowedBytes / MB);
  const maxMb = Math.round(settings.maxAllowedBytes / MB);

  function saveLimit() {
    if (!/^\d+$/.test(limit.trim()) || Number(limit) < minMb || Number(limit) > maxMb) {
      setFieldErrors({ limit: `Enter a whole number of megabytes from ${minMb} to ${maxMb}.` });
      return;
    }
    void act(async () => { await setSizeLimit(Number(limit) * MB, settings.rowVersion); return "Size limit saved."; }, "limit");
  }

  function saveCategory() {
    if (!newCategory.trim()) { setFieldErrors({ name: "Enter a name for the category." }); return; }
    void act(async () => { const c = await addCategory(newCategory); setNewCategory(""); return `Added ${c.name}.`; }, "name");
  }

  return (
    <div>
      <PageHeader title="Document settings" description="The categories documents are filed under, the largest file that can be imported, and the file types nobody can import." />
      {status && <p className="alv-documents__status" role="status">{status}</p>}
      {actionError && <p className="alv-form-field__error" role="alert">{actionError}</p>}
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => { setConflict(null); setRenaming(null); void reload(); }} />}

      <Section title="Categories" intro="A category is renamed or stopped, never deleted, so documents keep the category they were filed under. At least one category stays in use.">
        <table className="alv-documents__table" aria-label="Categories">
          <thead><tr><th scope="col">Category</th><th scope="col">Status</th><th scope="col">Actions</th></tr></thead>
          <tbody>
            {categories.map((c) => (
              <tr key={c.id}>
                <td>
                  {renaming?.id === c.id ? (
                    <FormField label={`New name for ${c.name}`} value={renaming.text} onChange={(e) => setRenaming({ id: c.id, text: e.target.value })} />
                  ) : c.name}
                </td>
                <td>{c.isActive ? "In use" : "Not in use"}</td>
                <td className="alv-documents__row-actions">
                  {renaming?.id === c.id ? (
                    <>
                      <Button type="button" variant="primary" disabled={busy} onClick={() => void act(async () => { await editCategory(c.id, { name: renaming.text, rowVersion: c.rowVersion }); setRenaming(null); return `Renamed ${c.name}.`; })}>Save name</Button>
                      <Button type="button" onClick={() => setRenaming(null)}>Cancel</Button>
                    </>
                  ) : (
                    <>
                      <Button type="button" aria-label={`Rename ${c.name}`} onClick={() => setRenaming({ id: c.id, text: c.name })}>Rename</Button>
                      <Button type="button" aria-label={c.isActive ? `Stop using ${c.name}` : `Use ${c.name} again`} disabled={busy}
                        onClick={() => void act(async () => { await editCategory(c.id, { isActive: !c.isActive, rowVersion: c.rowVersion }); return c.isActive ? `${c.name} is no longer in use.` : `${c.name} is in use again.`; })}>
                        {c.isActive ? "Stop using" : "Use again"}
                      </Button>
                    </>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        <form className="alv-documents__inline" noValidate onSubmit={(e) => { e.preventDefault(); saveCategory(); }}>
          <FormField label="New category name" value={newCategory} error={fieldErrors.name} onChange={(e) => setNewCategory(e.target.value)} />
          <Button type="submit" variant="primary" disabled={busy}>Add category</Button>
        </form>
      </Section>

      <Section title="Largest file" intro="Files larger than this are refused before they are stored.">
        <form className="alv-documents__inline" noValidate onSubmit={(e) => { e.preventDefault(); saveLimit(); }}>
          <FormField label="Largest file (MB)" type="number" value={limit} hint={`Between ${minMb} and ${maxMb} MB.`} error={fieldErrors.limit} onChange={(e) => setLimit(e.target.value)} />
          <Button type="submit" variant="primary" disabled={busy}>Save size limit</Button>
        </form>
      </Section>

      <Section title="Blocked file types" intro="Nobody can import these types. The built-in types protect every installation and cannot be unblocked; you can block more.">
        <ul className="alv-documents__types" aria-label="Blocked file types">
          {blocked.map((b) => (
            <li key={b.extension}>
              <span className="alv-documents__type">.{b.extension}</span>
              {b.isBuiltIn ? <span>Locked (built in)</span> : (
                <Button type="button" aria-label={`Unblock .${b.extension}`} disabled={busy} onClick={() => void act(async () => { await removeBlocked(b.extension); return `Unblocked .${b.extension}.`; })}>Unblock</Button>
              )}
            </li>
          ))}
        </ul>
        <form className="alv-documents__inline" noValidate onSubmit={(e) => { e.preventDefault(); void act(async () => { const added = await addBlocked(newType); setNewType(""); return `Blocked .${added.extension}.`; }, "extension"); }}>
          <FormField label="File type to block" value={newType} hint="For example swf" error={fieldErrors.extension} onChange={(e) => setNewType(e.target.value)} />
          <Button type="submit" variant="primary" disabled={busy}>Block file type</Button>
        </form>
      </Section>
    </div>
  );
}

function Section({ title, intro, children }: { title: string; intro: string; children: ReactNode }) {
  return (
    <section className="alv-documents__section" aria-label={title}>
      <h2 className="alv-workspace__section-title">{title}</h2>
      <p className="alv-workspace__note">{intro}</p>
      {children}
    </section>
  );
}
