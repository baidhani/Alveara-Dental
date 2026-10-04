import { useCallback, useEffect, useState } from "react";
import { Button } from "../../../components/Button";
import { ConcurrencyConflictBanner } from "../../../components/ConcurrencyConflictBanner";
import { SafeLink } from "../../../components/SafeLink";
import { EmptyState, ErrorState, LoadingState } from "../../../components/StatePatterns";
import { useAuth } from "../../../contexts/AuthContext";
import { ApiError, isConcurrencyConflict } from "../../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../../services/authApi";
import { clinicalFieldErrorsOf, isNetworkFailure } from "../../../services/clinicalApi";
import { NOTE_LABELS, createTemplate, listTemplates, setTemplateActive, updateTemplate } from "../../../services/clinicalNotesApi";
import type { NoteTemplate, TemplateInput } from "../../../services/clinicalNotesApi";
import type { PatientDetail } from "../../../services/patientsApi";
import { TemplateForm } from "./TemplateForm";
import "../Clinical.css";

type Mode = { kind: "list" } | { kind: "new" } | { kind: "edit"; id: string };
type Status = { kind: "idle" } | { kind: "saving" } | { kind: "saved"; text: string } | { kind: "failed"; text: string };

const sectionsOf = (t: NoteTemplate) => t.sections.map((s) => `${NOTE_LABELS[s.section]}${s.required ? " (required)" : ""}`).join(", ");

/**
 * ALV-005-C01: the clinical note templates - which note sections a note has, which must be written before it can be signed, and optional starter text. They belong to the
 * clinical-documentation domain and are configured with their own permission (ManageClinicalTemplates); everyone who may read clinical documentation can see them. A template
 * is taken out of use, never deleted, and changing one never changes an encounter that already used it. A stale edit shows the shared conflict banner and a reload that
 * refreshes in place, so the form stays mounted.
 */
export function TemplatesPage({ patient }: { patient: PatientDetail }) {
  const { hasPermission } = useAuth();
  const canConfigure = hasPermission("ManageClinicalTemplates");
  const [templates, setTemplates] = useState<NoteTemplate[] | null>(null);
  const [failed, setFailed] = useState(false);
  const [mode, setMode] = useState<Mode>({ kind: "list" });
  const [status, setStatus] = useState<Status>({ kind: "idle" });
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const list = await listTemplates(true, signal);
    setTemplates(list);
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    refresh(controller.signal).catch(() => { if (!controller.signal.aborted) setFailed(true); });
    return () => controller.abort();
  }, [refresh]);

  const busy = status.kind === "saving";

  async function run(change: () => Promise<unknown>, savedText: string): Promise<Record<string, string> | null> {
    setStatus({ kind: "saving" });
    try {
      await change();
      await refresh();
      setStatus({ kind: "saved", text: savedText });
      return null;
    } catch (err) {
      if (isConcurrencyConflict(err)) {
        setConflict(err.body);
        setStatus({ kind: "failed", text: "Not saved: someone else changed this template." });
      } else if (isNetworkFailure(err)) setStatus({ kind: "failed", text: "Not saved: the connection dropped. What you typed is still here; try again." });
      else setStatus({ kind: "failed", text: `Not saved: ${err instanceof ApiError ? err.message : "something went wrong."}` });
      return clinicalFieldErrorsOf(err);
    }
  }

  if (failed) return <ErrorState title="Could not load the note templates" description="Check your connection and reload the page." />;
  if (templates === null) return <LoadingState label="Loading the note templates…" />;
  const editing = mode.kind === "edit" ? templates.find((t) => t.id === mode.id) : undefined;

  return (
    <section className="alv-clinical" aria-labelledby="alv-templates-title">
      <SafeLink to={`/patients/${patient.id}/clinical`} className="alv-workspace__link alv-clinical__back">Back to clinical documentation</SafeLink>
      <h2 id="alv-templates-title" className="alv-workspace__section-title">Note templates</h2>
      <p className="alv-clinical__meta">A template names the sections of a note, which of them must be written before the note can be signed, and optional starter text.</p>
      <p className="alv-clinical__status" role="status" aria-live="polite">
        {status.kind === "saving" ? "Saving…" : status.kind === "idle" ? (canConfigure ? "Changes are saved when you save the template." : "You can read the templates but your role cannot change them.") : status.text}
      </p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => void refresh().then(() => setStatus({ kind: "idle" })).catch(() => setStatus({ kind: "failed", text: "Could not reload. Check your connection and try again." }))} />}

      {canConfigure && mode.kind === "list" && <div className="alv-clinical__start"><Button type="button" variant="primary" onClick={() => setMode({ kind: "new" })} disabled={busy}>New template</Button></div>}

      {mode.kind === "new" && (
        <TemplateForm
          busy={busy}
          onCancel={() => setMode({ kind: "list" })}
          onSubmit={async (input: TemplateInput) => {
            const refused = await run(() => createTemplate(input), `Template ${input.name.trim()} created.`);
            if (!refused) setMode({ kind: "list" });
            return refused;
          }}
        />
      )}
      {mode.kind === "edit" && editing && (
        <TemplateForm
          template={editing}
          busy={busy}
          onCancel={() => setMode({ kind: "list" })}
          onSubmit={async (input) => {
            const refused = await run(() => updateTemplate(editing.id, input, editing.rowVersion), `Template ${input.name.trim()} saved.`);
            if (!refused) setMode({ kind: "list" });
            return refused;
          }}
        />
      )}

      {templates.length === 0 ? (
        <EmptyState title="No note templates yet" description={canConfigure ? "Create one to give notes their sections and starter text." : "A dentist or administrator can create templates."} />
      ) : (
        <ul className="alv-clinical__list">
          {templates.map((t) => (
            <li key={t.id} className="alv-clinical__list-item">
              <span className="alv-clinical__list-link">{t.name}</span>
              <span className={`alv-clinical__badge alv-clinical__badge--${t.isActive ? "finalized" : "draft"}`}>{t.isActive ? "In use" : "Out of use"}</span>
              <span className="alv-clinical__meta">{sectionsOf(t)}{t.description ? ` · ${t.description}` : ""}</span>
              {canConfigure && mode.kind === "list" && (
                <span className="alv-clinical__row-actions">
                  <Button type="button" onClick={() => setMode({ kind: "edit", id: t.id })} disabled={busy} aria-label={`Change template ${t.name}`}>Change</Button>
                  <Button type="button" onClick={() => void run(() => setTemplateActive(t.id, !t.isActive, t.rowVersion), `Template ${t.name} ${t.isActive ? "taken out of use" : "put back in use"}.`)} disabled={busy} aria-label={`${t.isActive ? "Take out of use" : "Put back in use"}: ${t.name}`}>
                    {t.isActive ? "Take out of use" : "Put back in use"}
                  </Button>
                </span>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
