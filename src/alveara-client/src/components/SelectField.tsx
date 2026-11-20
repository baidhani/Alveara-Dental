import { useId } from "react";
import type { SelectHTMLAttributes } from "react";
import "./FormField.css";

export interface SelectOption {
  value: string;
  label: string;
}

interface SelectFieldProps extends SelectHTMLAttributes<HTMLSelectElement> {
  label: string;
  options: SelectOption[];
  hint?: string;
  error?: string;
}

/**
 * ALV-N025 (F-032, F-033): the select counterpart of FormField - the same label, hint and error wiring (aria-describedby, aria-invalid, the error announced), so a
 * value list reads exactly like a typed field to sighted and screen-reader users.
 */
export function SelectField({ label, options, hint, error, id, ...rest }: SelectFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const hintId = hint && !error ? `${fieldId}-hint` : undefined;
  const errorId = error ? `${fieldId}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;

  return (
    <div className="alv-form-field">
      <label htmlFor={fieldId} className="alv-form-field__label">
        {label}
      </label>
      <select
        id={fieldId}
        className={`alv-form-field__input${error ? " alv-form-field__input--error" : ""}`}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        {...rest}
      >
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
      {hint && !error && (
        <p id={hintId} className="alv-form-field__hint">
          {hint}
        </p>
      )}
      {error && (
        <p id={errorId} className="alv-form-field__error" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
