/** Client-side mirror of the server's field rules for ConfigEntityPanel; the server remains the authority. */
export interface ConfigField {
  name: string;
  label: string;
  /** ALV-N030: "checkbox" stores "true" when ticked and "" when not. */
  type: "text" | "number" | "select" | "checkbox";
  required?: boolean;
  maxLength?: number;
  min?: number;
  max?: number;
  step?: number;
  hint?: string;
  options?: { value: string; label: string }[];
  /** Options that depend on the record being edited (null while creating), e.g. accounts still free to link. */
  optionsFor?: (editingId: string | null) => { value: string; label: string }[];
  /** Shown only while creating (e.g. which staff member a provider profile belongs to). */
  createOnly?: boolean;
  /**
   * ALV-N030-C01 (brief 18.8): while the named checkbox is ticked this field is set to `value` and cannot be changed, and `hint` says why
   * (e.g. the Practice Owner's role is Administrator). The server is still the control.
   */
  lockedBy?: { field: string; value: string; hint: string };
}

export type FormValues = Record<string, string>;

export function validateFields(fields: ConfigField[], values: FormValues, creating: boolean): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const field of fields) {
    if (field.createOnly && !creating) continue;
    const value = (values[field.name] ?? "").trim();
    if (field.required && value === "") {
      errors[field.name] = `${field.label} is required.`;
      continue;
    }
    if (value === "") continue;
    if (field.maxLength && value.length > field.maxLength) {
      errors[field.name] = `${field.label} must be ${field.maxLength} characters or fewer.`;
    }
    if (field.type === "number") {
      const n = Number(value);
      if (!Number.isFinite(n) || !Number.isInteger(n)) {
        errors[field.name] = `${field.label} must be a whole number.`;
      } else if (field.min !== undefined && n < field.min) {
        errors[field.name] = `${field.label} must be at least ${field.min}.`;
      } else if (field.max !== undefined && n > field.max) {
        errors[field.name] = `${field.label} must be at most ${field.max}.`;
      } else if (field.step && field.min !== undefined && (n - field.min) % field.step !== 0) {
        errors[field.name] = `${field.label} must be in steps of ${field.step}.`;
      }
    }
  }
  return errors;
}
