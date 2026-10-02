import { useCallback, useEffect, useState } from "react";
import { Button } from "../../components/Button";
import { PageHeader } from "../../components/PageHeader";
import { EmptyState, ErrorState, LoadingState } from "../../components/StatePatterns";
import { CATEGORY_LABELS, getTemplate, listTemplates } from "../../services/formsApi";
import type { TemplateDetail, TemplateSummary } from "../../services/formsApi";
import { TemplateEditor } from "./TemplateEditor";
import "./Forms.css";

type Selection = { kind: "none" } | { kind: "new" } | { kind: "template"; detail: TemplateDetail };

/**
 * ALV-N010: form/consent template administration (needs ManageFormTemplates). Lists every template, lets an administrator create one or
 * publish a new version of one, and shows each template's version history. Editing never changes a version that was already published.
 */
export function FormTemplatesPage() {
  const [templates, setTemplates] = useState<TemplateSummary[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [selection, setSelection] = useState<Selection>({ kind: "none" });
  const [loadingId, setLoadingId] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      setTemplates(await listTemplates());
      setFailed(false);
    } catch {
      setFailed(true);
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    listTemplates(controller.signal)
      .then(setTemplates)
      .catch(() => {
        if (!controller.signal.aborted) setFailed(true);
      });
    return () => controller.abort();
  }, []);

  async function select(id: string) {
    setLoadingId(id);
    setNotice(null);
    try {
      setSelection({ kind: "template", detail: await getTemplate(id) });
    } catch {
      setFailed(true);
    } finally {
      setLoadingId(null);
    }
  }

  async function saved(detail: TemplateDetail, message?: string) {
    setNotice(message ?? null);
    setSelection({ kind: "template", detail: await getTemplate(detail.template.id).catch(() => detail) });
    void refresh();
  }

  return (
    <>
      <PageHeader title="Form templates" description="Versioned forms and consents. Editing a template publishes a new version; forms already signed keep the version they were signed on." />
      {failed && <ErrorState title="Could not load the form templates" action={<Button onClick={() => void refresh()}>Retry</Button>} />}
      {templates === null && !failed && <LoadingState label="Loading templates…" />}
      {templates !== null && (
        <div className="alv-template-layout">
          <nav aria-label="Form templates">
            <div style={{ marginBottom: "var(--space-3)" }}>
              <Button variant="primary" onClick={() => { setNotice(null); setSelection({ kind: "new" }); }}>New template</Button>
            </div>
            {templates.length === 0 ? (
              <EmptyState title="No templates yet" description="Create the first form or consent template." />
            ) : (
              <ul className="alv-template-list">
                {templates.map((t) => (
                  <li key={t.id}>
                    <button type="button" className="alv-template-list__item" aria-current={selection.kind === "template" && selection.detail.template.id === t.id ? "true" : undefined}
                      onClick={() => void select(t.id)} disabled={loadingId === t.id}>
                      {t.current?.title ?? t.key}
                      <br />
                      <span className="alv-workspace__note">{CATEGORY_LABELS[t.category] ?? t.category} · v{t.current?.versionNumber ?? 0} · {t.isActive ? "Active" : "Inactive"}{t.requiredAtCheckIn ? " · Required at check-in" : ""}</span>
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </nav>
          <div>
            <p className={notice ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{notice}</p>
            {selection.kind === "none" && <EmptyState title="Choose a template to edit, or create a new one" />}
            {selection.kind === "new" && <TemplateEditor key="new" detail={null} onSaved={(d, m) => void saved(d, m)} />}
            {selection.kind === "template" && (
              <>
                <TemplateEditor key={selection.detail.template.id} detail={selection.detail} onSaved={(d, m) => void saved(d, m)} />
                <section aria-labelledby="alv-template-versions" style={{ marginTop: "var(--space-5, 24px)" }}>
                  <h3 id="alv-template-versions" className="alv-workspace__subtitle">Version history</h3>
                  <table className="alv-workspace__table">
                    <caption className="alv-workspace__caption">Published versions, newest first. Published versions are never edited.</caption>
                    <thead><tr><th scope="col">Version</th><th scope="col">Published</th><th scope="col">Title</th><th scope="col">What changed</th></tr></thead>
                    <tbody>
                      {selection.detail.versions.map((v) => (
                        <tr key={v.id}>
                          <td>{v.versionNumber}{v.id === selection.detail.template.current?.id ? " (current)" : ""}</td>
                          <td>{new Date(v.createdAtUtc).toLocaleString()}</td>
                          <td>{v.title}</td>
                          <td>{v.changeNote ?? "—"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </section>
              </>
            )}
          </div>
        </div>
      )}
    </>
  );
}
