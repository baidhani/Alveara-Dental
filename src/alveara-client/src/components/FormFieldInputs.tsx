import { useId } from "react";
import { FormField } from "./FormField";
import type { FormField as FormFieldDefinition } from "../services/formsApi";
import "./FormFieldInputs.css";

interface InputsProps {
  fields: FormFieldDefinition[];
  values: Record<string, string>;
  /** Server/client messages keyed `responses.<fieldId>`. */
  errors?: Record<string, string>;
  disabled?: boolean;
  onChange: (fieldId: string, value: string) => void;
}

const label = (f: FormFieldDefinition) => (f.required ? `${f.label} *` : f.label);

/**
 * ALV-N010: renders the answer inputs for one template version's fields (checkbox, short/long text, choice, date). Required fields
 * are marked; a message for a field is announced and tied to its input. Used while a form is a draft.
 */
export function FormFieldInputs({ fields, values, errors = {}, disabled, onChange }: InputsProps) {
  return (
    <div className="alv-form-inputs">
      {fields.map((f) => (
        <FieldInput key={f.id} field={f} value={values[f.id] ?? ""} error={errors[`responses.${f.id}`]} disabled={disabled} onChange={(v) => onChange(f.id, v)} />
      ))}
    </div>
  );
}

function FieldInput({ field, value, error, disabled, onChange }: { field: FormFieldDefinition; value: string; error?: string; disabled?: boolean; onChange: (v: string) => void }) {
  const id = useId();
  const errorId = error ? `${id}-error` : undefined;

  if (field.kind === "checkbox") {
    return (
      <div className="alv-form-inputs__row">
        <label className="alv-workspace__checkbox" htmlFor={id}>
          <input id={id} type="checkbox" checked={value === "true"} disabled={disabled} aria-invalid={error ? true : undefined} aria-describedby={errorId} onChange={(e) => onChange(e.target.checked ? "true" : "false")} />
          {label(field)}
        </label>
        {error && <p id={errorId} className="alv-form-field__error" role="alert">{error}</p>}
      </div>
    );
  }
  if (field.kind === "longText") {
    return (
      <div className="alv-form-field">
        <label htmlFor={id} className="alv-form-field__label">{label(field)}</label>
        <textarea id={id} className={`alv-form-field__input alv-form-inputs__textarea${error ? " alv-form-field__input--error" : ""}`} rows={4} value={value} disabled={disabled}
          aria-invalid={error ? true : undefined} aria-describedby={errorId} onChange={(e) => onChange(e.target.value)} />
        {error && <p id={errorId} className="alv-form-field__error" role="alert">{error}</p>}
      </div>
    );
  }
  if (field.kind === "choice") {
    return (
      <div className="alv-form-field">
        <label htmlFor={id} className="alv-form-field__label">{label(field)}</label>
        <select id={id} className={`alv-form-field__input${error ? " alv-form-field__input--error" : ""}`} value={value} disabled={disabled}
          aria-invalid={error ? true : undefined} aria-describedby={errorId} onChange={(e) => onChange(e.target.value)}>
          <option value="">Choose…</option>
          {(field.options ?? []).map((o) => <option key={o} value={o}>{o}</option>)}
        </select>
        {error && <p id={errorId} className="alv-form-field__error" role="alert">{error}</p>}
      </div>
    );
  }
  return <FormField label={label(field)} type={field.kind === "date" ? "date" : "text"} value={value} disabled={disabled} error={error} onChange={(e) => onChange(e.target.value)} />;
}

/** The entered answers as a read-only list - what the signer reviews, and what a signed snapshot shows. */
export function FormAnswers({ fields, responses }: { fields: FormFieldDefinition[]; responses: Record<string, string> }) {
  return (
    <dl className="alv-form-answers">
      {fields.map((f) => {
        const raw = responses[f.id];
        const shown = f.kind === "checkbox" ? (raw === "true" ? "Yes" : "No") : raw && raw.length > 0 ? raw : "—";
        return (
          <div key={f.id} className="alv-form-answers__row">
            <dt>{f.label}</dt>
            <dd>{shown}</dd>
          </div>
        );
      })}
    </dl>
  );
}
