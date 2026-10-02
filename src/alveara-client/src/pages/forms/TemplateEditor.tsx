import { useEffect, useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormField } from "../../components/FormField";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import {
  CATEGORY_LABELS, FIELD_KINDS, FORM_CATEGORIES, KIND_LABELS, createTemplate, emptyField, formFieldErrorsOf, publishTemplateVersion, setTemplateActive,
} from "../../services/formsApi";
import type { FormField as FieldDef, TemplateDetail, TemplateInput } from "../../services/formsApi";
import "./Forms.css";

const blank = (): TemplateInput => ({ key: "", category: "Privacy", title: "", body: "", fields: [], changeNote: "" });

const toInput = (d: TemplateDetail): TemplateInput => ({
  key: d.template.key, category: d.template.category, title: d.template.current?.title ?? "", body: d.template.current?.body ?? "",
  fields: (d.template.current?.fields ?? []).map((f) => ({ ...f, options: f.options ?? null })), changeNote: "",
});

/**
 * ALV-N010: create a template, or edit one by PUBLISHING A NEW VERSION (earlier versions - and every form already signed on them - are
 * untouched). The form always starts from the server's current version; a save carries the template's row version, so a second
 * administrator's concurrent edit is reported, never overwritten.
 */
export function TemplateEditor({ detail, onSaved }: { detail: TemplateDetail | null; onSaved: (d: TemplateDetail, notice?: string) => void }) {
  const creating = detail === null;
  const [input, setInput] = useState<TemplateInput>(() => (detail ? toInput(detail) : blank()));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const catId = useId();
  const bodyId = useId();

  const version = detail?.template.rowVersion;
  useEffect(() => {
    setInput(detail ? toInput(detail) : blank());
    setErrors({});
    setError(null);
    setConflict(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reset exactly when the selected template or its server version changes
  }, [detail?.template.id, version]);

  const baseline = JSON.stringify(detail ? toInput(detail) : blank());
  const dirty = JSON.stringify(input) !== baseline;
  useUnsavedChangesWarning(dirty);

  const set = <K extends keyof TemplateInput>(k: K, v: TemplateInput[K]) => {
    setInput((p) => ({ ...p, [k]: v }));
  };
  const setField = (i: number, patch: Partial<FieldDef>) => set("fields", input.fields.map((f, n) => (n === i ? { ...f, ...patch } : f)));

  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setErrors({});
    setError(null);
    const payload: TemplateInput = {
      ...input,
      fields: input.fields.map((f) => ({ ...f, options: f.kind === "choice" ? (f.options ?? []).map((o) => o.trim()).filter(Boolean) : null })),
    };
    try {
      const saved = creating ? await createTemplate(payload) : await publishTemplateVersion(detail.template.id, payload, detail.template.rowVersion);
      const published = creating || saved.template.versionCount > detail.template.versionCount;
      onSaved(saved, creating ? "Template created as version 1." : published ? `Version ${saved.template.current?.versionNumber} published.` : "Nothing changed, so no new version was published.");
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else if (err instanceof ApiError && err.code === "validation_failed") {
        setErrors(formFieldErrorsOf(err));
        setError("Some details need attention.");
      } else setError(err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.");
    } finally {
      setBusy(false);
    }
  }

  async function toggleActive() {
    if (!detail || busy) return;
    setBusy(true);
    setError(null);
    try {
      const saved = await setTemplateActive(detail.template.id, !detail.template.isActive, detail.template.rowVersion);
      onSaved(saved, saved.template.isActive ? "Template reactivated." : "Template inactivated. Forms already started or signed are unaffected.");
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setError(err instanceof ApiError ? err.message : "Could not save. Try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-workspace__form" onSubmit={save} noValidate aria-label={creating ? "New form template" : "Edit form template"}>
      <h2 className="alv-workspace__section-title">{creating ? "New template" : `Edit ${detail.template.key}`}</h2>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={() => onSaved(detail!)} />}
      {!creating && (
        <p className="alv-workspace__note">
          Saving changes publishes version {detail.template.versionCount + 1}. Earlier versions, and forms already signed on them, are never changed.
        </p>
      )}
      <fieldset className="alv-patient-fields" disabled={busy}>
        <legend>Template</legend>
        <FormField label="Key (cannot be changed later) *" value={input.key} onChange={(e) => set("key", e.target.value)} error={errors.key} disabled={!creating} hint="Lowercase letters, digits and hyphens, for example privacy-notice." />
        <div className="alv-form-field">
          <label htmlFor={catId} className="alv-form-field__label">Category *</label>
          <select id={catId} className={`alv-form-field__input${errors.category ? " alv-form-field__input--error" : ""}`} value={input.category} onChange={(e) => set("category", e.target.value)} disabled={!creating}>
            {FORM_CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_LABELS[c]}</option>)}
          </select>
          {errors.category && <p className="alv-form-field__error" role="alert">{errors.category}</p>}
        </div>
        <FormField label="Title *" value={input.title} onChange={(e) => set("title", e.target.value)} error={errors.title} />
        <div className="alv-form-field">
          <label htmlFor={bodyId} className="alv-form-field__label">Form text shown to the signer *</label>
          <textarea id={bodyId} className={`alv-form-field__input alv-form-inputs__textarea${errors.body ? " alv-form-field__input--error" : ""}`} rows={8} value={input.body}
            onChange={(e) => set("body", e.target.value)} aria-invalid={errors.body ? true : undefined} />
          {errors.body && <p className="alv-form-field__error" role="alert">{errors.body}</p>}
        </div>
        <FormField label="What changed (optional)" value={input.changeNote} onChange={(e) => set("changeNote", e.target.value)} />
      </fieldset>

      <div className="alv-template-fields">
        <h3 className="alv-workspace__subtitle">Questions the signer answers</h3>
        {errors.fields && <p className="alv-form-field__error" role="alert">{errors.fields}</p>}
        {input.fields.length === 0 && <p className="alv-workspace__note">No questions - the form is read and signed as it is.</p>}
        {input.fields.map((f, i) => (
          <fieldset key={i} className="alv-template-field" disabled={busy}>
            <legend>Question {i + 1}</legend>
            <FormField label="Question text *" value={f.label} onChange={(e) => setField(i, { label: e.target.value })} error={errors[`fields[${i}].label`]} />
            <FormField label="Field id *" value={f.id} onChange={(e) => setField(i, { id: e.target.value })} error={errors[`fields[${i}].id`]} hint="Lowercase, e.g. nickname" />
            <div className="alv-form-field">
              <label className="alv-form-field__label" htmlFor={`${catId}-kind-${i}`}>Type</label>
              <select id={`${catId}-kind-${i}`} className="alv-form-field__input" value={f.kind} onChange={(e) => setField(i, { kind: e.target.value })}>
                {FIELD_KINDS.map((k) => <option key={k} value={k}>{KIND_LABELS[k]}</option>)}
              </select>
              {errors[`fields[${i}].kind`] && <p className="alv-form-field__error" role="alert">{errors[`fields[${i}].kind`]}</p>}
            </div>
            <label className="alv-workspace__checkbox">
              <input type="checkbox" checked={f.required} onChange={(e) => setField(i, { required: e.target.checked })} />
              Required to sign
            </label>
            {f.kind === "choice" && (
              <div className="alv-form-field">
                <label className="alv-form-field__label" htmlFor={`${catId}-opt-${i}`}>Options (one per line) *</label>
                <textarea id={`${catId}-opt-${i}`} className="alv-form-field__input alv-form-inputs__textarea" rows={3} value={(f.options ?? []).join("\n")}
                  onChange={(e) => setField(i, { options: e.target.value.split("\n") })} />
                {errors[`fields[${i}].options`] && <p className="alv-form-field__error" role="alert">{errors[`fields[${i}].options`]}</p>}
              </div>
            )}
            <div>
              <Button type="button" variant="danger" onClick={() => set("fields", input.fields.filter((_, n) => n !== i))}>Remove question {i + 1}</Button>
            </div>
          </fieldset>
        ))}
        <div><Button type="button" onClick={() => set("fields", [...input.fields, emptyField()])} disabled={busy}>Add a question</Button></div>
      </div>

      <p className="alv-forms__legal">
        The wording of each form is the practice's own. This system records what was shown and who signed it; it does not make any wording legally sufficient, and the
        practice's legal review of its forms is outside this software.
      </p>
      {error && <p className="alv-form-field__error" role="alert">{error}</p>}
      <div className="alv-forms__actions">
        <Button type="submit" variant="primary" disabled={busy || (!creating && !dirty)}>{busy ? "Saving…" : creating ? "Create template" : "Publish new version"}</Button>
        {!creating && <Button type="button" onClick={() => void toggleActive()} disabled={busy}>{detail.template.isActive ? "Inactivate template" : "Reactivate template"}</Button>}
        {dirty && <span className="alv-workspace__dirty">Unsaved changes</span>}
      </div>
    </form>
  );
}
