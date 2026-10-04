import { useId, useState } from "react";
import type { FormEvent, ReactNode } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ALERT_CATEGORIES, CATEGORY_LABELS, SEVERITIES } from "../../services/safetyApi";
import type { AlertInput } from "../../services/safetyApi";
import "./Safety.css";

export type SaveResult = Record<string, string> | null;

/**
 * A small form with ONE text box and a confirm button - the shape of every step that needs a reason or a note (resolve, reopen, cancel, receive). When `required` is set the server
 * refuses without it and so does this form (before sending). Typed text stays until the server has accepted it, so a refusal or a dropped connection never loses it.
 */
export function ReasonForm({ label, hint, required, submitLabel, ariaLabel, busy, onSubmit, onCancel, children }: {
  label: string; hint?: string; required: boolean; submitLabel: string; ariaLabel: string; busy: boolean;
  onSubmit: (text: string, extra: Record<string, string>) => Promise<SaveResult>; onCancel: () => void; children?: (extra: Record<string, string>, set: (key: string, value: string) => void, errors: Record<string, string>) => ReactNode;
}) {
  const [text, setText] = useState("");
  const [extra, setExtra] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const id = useId();
  useUnsavedChangesWarning(text.trim() !== "" || Object.values(extra).some((v) => v.trim() !== ""));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (required && text.trim() === "") {
      setErrors({ reason: "Say why." });
      return;
    }
    setErrors({});
    const refused = await onSubmit(text.trim(), extra);
    if (refused) setErrors(refused);
  }

  return (
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label={ariaLabel}>
      {children?.(extra, (k, v) => setExtra((e) => ({ ...e, [k]: v })), errors)}
      <div className="alv-form-field">
        <label htmlFor={id} className="alv-form-field__label">{label}</label>
        <textarea id={id} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={500} value={text} aria-invalid={errors.reason || errors.note ? true : undefined} onChange={(e) => setText(e.target.value)} />
        {hint && !errors.reason && !errors.note && <p className="alv-form-field__hint">{hint}</p>}
        {(errors.reason || errors.note) && <p className="alv-form-field__error" role="alert">{errors.reason ?? errors.note}</p>}
      </div>
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>{submitLabel}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}

interface AlertFormProps {
  /** Present when changing an alert; absent when stating a new one (the category is fixed once stated). */
  initial?: AlertInput;
  submitLabel: string;
  busy: boolean;
  onSubmit: (input: AlertInput) => Promise<SaveResult>;
  onCancel: () => void;
}

/**
 * ALV-N011: the fields of one alert - what it is, how serious, and WHERE THE INFORMATION CAME FROM (required: an alert that does not say so is refused). Nothing is pre-filled
 * with a guess. Typed values survive a refused or failed save, and leaving with unsaved text asks first.
 */
export function AlertForm({ initial, submitLabel, busy, onSubmit, onCancel }: AlertFormProps) {
  const base: AlertInput = initial ?? { category: "Condition", title: "", detail: "", severity: "", sourceNote: "" };
  const [values, setValues] = useState<AlertInput>(base);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const categoryId = useId();
  const severityId = useId();
  const detailId = useId();
  const dirty = (Object.keys(values) as (keyof AlertInput)[]).some((k) => values[k] !== base[k]);
  useUnsavedChangesWarning(dirty);
  const set = (patch: Partial<AlertInput>) => setValues((v) => ({ ...v, ...patch }));

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    const found: Record<string, string> = {};
    if (values.title.trim() === "") found.title = "A title is required.";
    if (values.severity === "") found.severity = "Choose how serious it is.";
    if (values.sourceNote.trim() === "") found.sourceNote = "Say where this information came from.";
    setErrors(found);
    if (Object.keys(found).length > 0) return;
    const refused = await onSubmit(values);
    if (refused) setErrors(refused);
  }

  return (
    <form className="alv-safety__form" onSubmit={submit} noValidate aria-label={submitLabel}>
      {!initial && (
        <div className="alv-form-field">
          <label htmlFor={categoryId} className="alv-form-field__label">What kind of alert</label>
          <select id={categoryId} className="alv-form-field__input" value={values.category} aria-invalid={errors.category ? true : undefined} onChange={(e) => set({ category: e.target.value })}>
            {ALERT_CATEGORIES.map((c) => <option key={c} value={c}>{CATEGORY_LABELS[c]}</option>)}
          </select>
          {errors.category && <p className="alv-form-field__error" role="alert">{errors.category}</p>}
        </div>
      )}
      <FormField label="Title" value={values.title} error={errors.title} maxLength={200} onChange={(e) => set({ title: e.target.value })} autoFocus />
      <div className="alv-form-field">
        <label htmlFor={severityId} className="alv-form-field__label">Severity</label>
        <select id={severityId} className="alv-form-field__input" value={values.severity} aria-invalid={errors.severity ? true : undefined} onChange={(e) => set({ severity: e.target.value })}>
          <option value="">Choose…</option>
          {SEVERITIES.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
        {errors.severity && <p className="alv-form-field__error" role="alert">{errors.severity}</p>}
      </div>
      <div className="alv-form-field">
        <label htmlFor={detailId} className="alv-form-field__label">Details</label>
        <textarea id={detailId} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={1000} value={values.detail} aria-invalid={errors.detail ? true : undefined} onChange={(e) => set({ detail: e.target.value })} />
        {errors.detail && <p className="alv-form-field__error" role="alert">{errors.detail}</p>}
      </div>
      <FormField label="Source of this information" value={values.sourceNote} error={errors.sourceNote} maxLength={300} hint="For example: reported by the patient at intake, or a letter from the cardiologist." onChange={(e) => set({ sourceNote: e.target.value })} />
      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>{submitLabel}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
