import { useId, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { formFieldErrorsOf, voidPatientForm } from "../../services/formsApi";
import type { PatientFormDetail } from "../../services/formsApi";

/**
 * ALV-N010: discard a draft or void a signed form. A reason is required. Voiding never alters the signed copy - the form simply stops
 * being current and the history records who, when and why.
 */
export function FormVoidPanel({ form, signed, onVoided, onReload }: { form: PatientFormDetail; signed: boolean; onVoided: (f: PatientFormDetail) => void; onReload: () => void }) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const id = useId();
  const verb = signed ? "Void this signed form" : "Discard this draft";

  if (!open) return <Button variant="danger" onClick={() => setOpen(true)}>{verb}</Button>;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      onVoided(await voidPatientForm(form.summary.id, reason, form.rowVersion));
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setError(formFieldErrorsOf(err).reason ?? (err instanceof ApiError ? err.message : "Could not save. Check your connection and try again."));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-workspace__form" onSubmit={submit} noValidate aria-label={verb}>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={onReload} />}
      <p className="alv-workspace__note">
        {signed
          ? "The signed copy is kept exactly as signed. Voiding marks this form as no longer current; to correct it, complete a new form afterwards."
          : "The draft is kept in the history as void."}
      </p>
      <div className="alv-form-field">
        <label htmlFor={id} className="alv-form-field__label">Reason (required)</label>
        <textarea id={id} className="alv-form-field__input alv-form-inputs__textarea" rows={3} value={reason} onChange={(e) => setReason(e.target.value)} aria-invalid={error ? true : undefined} />
      </div>
      {error && <p className="alv-form-field__error" role="alert">{error}</p>}
      <div className="alv-forms__actions">
        <Button type="submit" variant="danger" disabled={busy}>{busy ? "Working…" : "Confirm"}</Button>
        <Button type="button" onClick={() => setOpen(false)} disabled={busy}>Cancel</Button>
      </div>
    </form>
  );
}
