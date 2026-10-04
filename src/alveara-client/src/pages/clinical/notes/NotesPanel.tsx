import { useEffect, useId, useState } from "react";
import { Button } from "../../../components/Button";
import { NOTE_LABELS, NOTE_SECTIONS, listTemplates } from "../../../services/clinicalNotesApi";
import type { NoteTemplate } from "../../../services/clinicalNotesApi";
import type { EncounterDetail } from "../../../services/clinicalApi";
import type { SaveResult } from "../SectionPanel";
import { NoteEditor } from "./NoteEditor";
import "../Clinical.css";

interface Props {
  encounter: EncounterDetail;
  /** True for an unsigned draft and a clinician who may change it. */
  editable: boolean;
  busy: boolean;
  onSave: (section: string, body: string) => Promise<SaveResult>;
  onApplyTemplate: (templateId: string) => Promise<SaveResult>;
}

/**
 * ALV-005-C01: the encounter's notes - SOAP, progress and treatment notes - and the template that shapes them. A clinician can start from a template (it names the sections
 * and which are required before signing, and may give starter text), and can add any other note section. Each note saves itself (see <see cref="NoteEditor"/>). A finalized
 * or signed encounter shows its notes read-only; nothing is drawn for a finalized encounter that has no notes.
 */
export function NotesPanel({ encounter, editable, busy, onSave, onApplyTemplate }: Props) {
  const [templates, setTemplates] = useState<NoteTemplate[]>([]);
  const [choice, setChoice] = useState("");
  const [extra, setExtra] = useState<string[]>([]);
  const [addChoice, setAddChoice] = useState("");
  const [templateError, setTemplateError] = useState<string | null>(null);
  const templateId = useId();
  const addId = useId();

  // the templates a clinician may start from; fetched whenever the encounter can still take one (never gated on a permission that may arrive late)
  const wantsTemplates = encounter.status === "Draft" && encounter.templateId === null;
  useEffect(() => {
    if (!wantsTemplates) return;
    const controller = new AbortController();
    listTemplates(false, controller.signal).then(setTemplates).catch(() => { if (!controller.signal.aborted) setTemplates([]); });
    return () => controller.abort();
  }, [wantsTemplates]);

  const byName = new Map(encounter.notes.map((n) => [n.section, n]));
  const shown = NOTE_SECTIONS.filter((s) => byName.has(s) || extra.includes(s));
  const addable = NOTE_SECTIONS.filter((s) => !shown.includes(s));
  const starterUnchanged = (section: string) => encounter.missingNotes.includes(section) && (byName.get(section)?.body.trim() ?? "") !== "";

  if (!editable && shown.length === 0) return null;

  return (
    <section className="alv-clinical__section" aria-labelledby="alv-clinical-notes-title">
      <header className="alv-clinical__section-head">
        <h3 id="alv-clinical-notes-title" className="alv-clinical__section-title">Notes</h3>
        {encounter.templateName && <span className="alv-clinical__meta">Template: {encounter.templateName}</span>}
      </header>

      {editable && encounter.templateId === null && templates.length > 0 && (
        <div className="alv-clinical__template-picker">
          <div className="alv-form-field">
            <label htmlFor={templateId} className="alv-form-field__label">Start from a template</label>
            <select id={templateId} className="alv-form-field__input" value={choice} onChange={(e) => setChoice(e.target.value)}>
              <option value="">No template</option>
              {templates.map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}
            </select>
          </div>
          <Button
            type="button"
            disabled={busy || choice === ""}
            onClick={async () => {
              setTemplateError(null);
              const refused = await onApplyTemplate(choice);
              if (refused) setTemplateError(refused.templateId ?? "That template could not be used.");
              else setChoice("");
            }}
          >
            Use template
          </Button>
          {templateError && <p className="alv-form-field__error" role="alert">{templateError}</p>}
        </div>
      )}

      {shown.length === 0 && editable && <p className="alv-clinical__meta">No notes yet. Start from a template or add a note section.</p>}

      {shown.map((section) => {
        const note = byName.get(section);
        return (
          <NoteEditor
            key={section}
            section={section}
            saved={note?.body ?? ""}
            required={note?.required ?? false}
            starterUnchanged={starterUnchanged(section)}
            editable={editable}
            updatedBy={note?.updatedByName ?? null}
            onSave={onSave}
          />
        );
      })}

      {editable && addable.length > 0 && (
        <div className="alv-clinical__template-picker">
          <div className="alv-form-field">
            <label htmlFor={addId} className="alv-form-field__label">Add a note section</label>
            <select id={addId} className="alv-form-field__input" value={addChoice} onChange={(e) => setAddChoice(e.target.value)}>
              <option value="">Choose a section</option>
              {addable.map((s) => <option key={s} value={s}>{NOTE_LABELS[s]}</option>)}
            </select>
          </div>
          <Button type="button" disabled={addChoice === ""} onClick={() => { setExtra((x) => [...x, addChoice]); setAddChoice(""); }}>Add section</Button>
        </div>
      )}
    </section>
  );
}
