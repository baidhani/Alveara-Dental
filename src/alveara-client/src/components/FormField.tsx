import { useId } from "react";
import type { InputHTMLAttributes } from "react";
import "./FormField.css";

interface FormFieldProps extends InputHTMLAttributes<HTMLInputElement> {
  label: string;
  hint?: string;
  error?: string;
}

/**
 * Standard label + input + validation pattern. Wires aria-describedby to the
 * hint/error text and aria-invalid when an error is present, so screen
 * readers announce the same state sighted users see.
 */
export function FormField({ label, hint, error, id, ...rest }: FormFieldProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const hintId = hint ? `${fieldId}-hint` : undefined;
  const errorId = error ? `${fieldId}-error` : undefined;
  const describedBy = [hintId, errorId].filter(Boolean).join(" ") || undefined;

  return (
    <div className="alv-form-field">
      <label htmlFor={fieldId} className="alv-form-field__label">
        {label}
      </label>
      <input
        id={fieldId}
        className={`alv-form-field__input${error ? " alv-form-field__input--error" : ""}`}
        aria-invalid={error ? true : undefined}
        aria-describedby={describedBy}
        {...rest}
      />
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
