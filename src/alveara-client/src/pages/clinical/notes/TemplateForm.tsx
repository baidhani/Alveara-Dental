import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../../components/Button";
import { FormField } from "../../../components/FormField";
import { useUnsavedChangesWarning } from "../../../hooks/useUnsavedChangesWarning";
import { NOTE_LABELS, NOTE_SECTIONS } from "../../../services/clinicalNotesApi";
import type { NoteTemplate, TemplateInput } from "../../../services/clinicalNotesApi";
import "../Clinical.css";

interface Row {
  included: boolean;
  required: boolean;
  starterText: string;
}

const blankRows = (): Record<string, Row> => Object.fromEntries(NOTE_SECTIONS.map((s) => [s, { included: false, required: false, starterText: "" }]));

function rowsOf(template: NoteTemplate | undefined): Record<string, Row> {
  const rows = blankRows();
  for (const s of template?.sections ?? []) rows[s.section] = { included: true, required: s.required, starterText: s.starterText ?? "" };
  return rows;
}

interface Props {
  /** The template being changed, or undefined for a new one. */
  template?: NoteTemplate;
  busy: boolean;
  /** Sends the template; resolves to null on success or to the server's per-field messages when it refused. Typed values stay put either way. */
  onSubmit: (input: TemplateInput) => Promise<Record<string, string> | null>;
  onCancel: () => void;
}

/**
 * ALV-005-C01: the form for one note template - a name, a description, and for each note section whether the template includes it, whether it must be written before the note
 * can be signed, and optional starter text. Nothing typed is lost to a refused or failed save, and leaving with unsaved changes asks first.
 */
export function TemplateForm({ template, busy, onSubmit, onCancel }: Props) {
  const initial = { name: template?.name ?? "", description: template?.description ?? "", rows: rowsOf(template) };
  const [name, setName] = useState(initial.name);
  const [description, setDescription] = useState(initial.description);
  const [rows, setRows] = useState(initial.rows);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const descId = useId();
  const dirty = name !== initial.name || description !== initial.description || JSON.stringify(rows) !== JSON.stringify(initial.rows);
  useUnsavedChangesWarning(dirty);
  const setRow = (section: string, patch: Partial<Row>) => setRows((r) => ({ ...r, [section]: { ...r[section], ...patch } }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const found: Record<string, string> = {};
    if (name.trim() === "") found.name = "A name is required.";
    if (!NOTE_SECTIONS.some((s) => rows[s].included)) found.sections = "Choose at least one note section.";
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const refused = await onSubmit({
      name, description,
      sections: NOTE_SECTIONS.filter((s) => rows[s].included).map((s) => ({ section: s, required: rows[s].required, starterText: rows[s].starterText })),
    });
    if (refused) setErrors(refused);
  }

  return (
    <form className="alv-clinical__entry-form alv-clinical__template-form" onSubmit={submit} noValidate aria-label={template ? `Change template ${template.name}` : "New note template"}>
      <FormField label="Template name" value={name} error={errors.name} maxLength={120} onChange={(e) => setName(e.target.value)} autoFocus />
      <div className="alv-form-field">
        <label htmlFor={descId} className="alv-form-field__label">Description</label>
        <textarea id={descId} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={description} aria-invalid={errors.description ? true : undefined} onChange={(e) => setDescription(e.target.value)} />
        {errors.description && <p className="alv-form-field__error" role="alert">{errors.description}</p>}
      </div>
      <fieldset className="alv-clinical__fieldset" aria-describedby={errors.sections ? "alv-template-sections-error" : undefined}>
        <legend className="alv-form-field__label">Note sections</legend>
        {errors.sections && <p id="alv-template-sections-error" className="alv-form-field__error" role="alert">{errors.sections}</p>}
        {NOTE_SECTIONS.map((section) => {
          const row = rows[section];
          return (
            <div key={section} className="alv-clinical__template-row">
              <label className="alv-clinical__check">
                <input type="checkbox" checked={row.included} onChange={(e) => setRow(section, { included: e.target.checked, required: e.target.checked ? row.required : false })} />
                Include {NOTE_LABELS[section]}
              </label>
              {row.included && (
                <>
                  <label className="alv-clinical__check">
                    <input type="checkbox" checked={row.required} onChange={(e) => setRow(section, { required: e.target.checked })} />
                    {NOTE_LABELS[section]} is required before signing
                  </label>
                  <div className="alv-form-field">
                    <label htmlFor={`${descId}-${section}`} className="alv-form-field__label">Starter text for {NOTE_LABELS[section]}</label>
                    <textarea id={`${descId}-${section}`} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={2000} value={row.starterText} onChange={(e) => setRow(section, { starterText: e.target.value })} />
                  </div>
                </>
              )}
            </div>
          );
        })}
      </fieldset>
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>{template ? "Save template" : "Create template"}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
