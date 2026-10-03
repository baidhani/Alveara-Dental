import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { FormField } from "../../components/FormField";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ENTRY_NOUNS, SEVERITIES, emptyEntry } from "../../services/clinicalApi";
import type { EntryInput } from "../../services/clinicalApi";
import "./Clinical.css";

interface Props {
  kind: string;
  /** The values to start from: blank for a new entry, the stored values when changing one. */
  initial?: EntryInput;
  submitLabel: string;
  busy: boolean;
  /** Sends the entry; resolves to null on success or to the server's per-field messages when it refused. The typed values stay put either way, so a failure never loses them. */
  onSubmit: (input: EntryInput) => Promise<Record<string, string> | null>;
  onCancel: () => void;
}

const FIELD_HINT = "Leave blank if not known.";

/**
 * STORY-005: the fields for one entry of a section - a name, and the fields that belong to that section only (an allergy has a reaction and a severity,
 * a medication a dose and a frequency, a history item free-text details). The typed values live here, in the form, until the server accepts them: a refused
 * or failed save keeps them on screen, and leaving with typed-but-unsaved text asks first.
 */
export function EntryForm({ kind, initial, submitLabel, busy, onSubmit, onCancel }: Props) {
  const [values, setValues] = useState<EntryInput>(initial ?? emptyEntry());
  const [errors, setErrors] = useState<Record<string, string>>({});
  const severityId = useId();
  const detailId = useId();
  const base = initial ?? emptyEntry();
  const dirty = (Object.keys(values) as (keyof EntryInput)[]).some((k) => values[k] !== base[k]);
  useUnsavedChangesWarning(dirty);
  const set = (patch: Partial<EntryInput>) => setValues((v) => ({ ...v, ...patch }));
  const noun = ENTRY_NOUNS[kind] ?? "entry";

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    if (values.name.trim() === "") {
      setErrors({ name: "A name is required." });
      return;
    }
    setErrors({});
    const refused = await onSubmit(values);
    if (refused) setErrors(refused);
  }

  return (
    <form className="alv-clinical__entry-form" onSubmit={submit} noValidate aria-label={`${submitLabel} - ${noun}`}>
      <FormField label="Name" value={values.name} error={errors.name} maxLength={200} onChange={(e) => set({ name: e.target.value })} autoFocus />

      {kind === "Allergy" && (
        <>
          <FormField label="Reaction" value={values.reaction} error={errors.reaction} maxLength={200} hint={FIELD_HINT} onChange={(e) => set({ reaction: e.target.value })} />
          <div className="alv-form-field">
            <label htmlFor={severityId} className="alv-form-field__label">Severity</label>
            <select id={severityId} className="alv-form-field__input" value={values.severity} aria-invalid={errors.severity ? true : undefined} onChange={(e) => set({ severity: e.target.value })}>
              <option value="">Not known</option>
              {SEVERITIES.map((s) => <option key={s} value={s}>{s}</option>)}
            </select>
            {errors.severity && <p className="alv-form-field__error" role="alert">{errors.severity}</p>}
          </div>
        </>
      )}

      {kind === "Medication" && (
        <>
          <FormField label="Dose" value={values.dose} error={errors.dose} maxLength={100} hint={FIELD_HINT} onChange={(e) => set({ dose: e.target.value })} />
          <FormField label="Frequency" value={values.frequency} error={errors.frequency} maxLength={100} hint={FIELD_HINT} onChange={(e) => set({ frequency: e.target.value })} />
        </>
      )}

      <div className="alv-form-field">
        <label htmlFor={detailId} className="alv-form-field__label">Details</label>
        <textarea id={detailId} className="alv-form-field__input alv-clinical__details" rows={2} maxLength={1000} value={values.detail} aria-invalid={errors.detail ? true : undefined} onChange={(e) => set({ detail: e.target.value })} />
        {errors.detail && <p className="alv-form-field__error" role="alert">{errors.detail}</p>}
      </div>

      <div className="alv-clinical__row-actions">
        <Button type="submit" variant="primary" disabled={busy}>{submitLabel}</Button>
        <Button type="button" onClick={onCancel} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
