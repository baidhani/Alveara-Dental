import { useId, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormAnswers } from "../../components/FormFieldInputs";
import { FormField } from "../../components/FormField";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { ATTESTATION, SIGNER_RELATIONSHIPS, formFieldErrorsOf, isNetworkFailure, newIdempotencyKey, signPatientForm } from "../../services/formsApi";
import type { PatientFormDetail } from "../../services/formsApi";
import "./Forms.css";

interface Props {
  form: PatientFormDetail;
  patientName: string;
  onBack: (errors?: Record<string, string>) => void;
  onSigned: (f: PatientFormDetail) => void;
  /** Re-read the form from the server (used after a conflict, an "already signed" answer, or an unconfirmed attempt). */
  onReload: () => void;
}

/**
 * ALV-N010: the pre-sign review. It shows exactly what will be signed - the template version's wording and the answers as saved - and
 * the signer's identity, relationship and typed signature. The signature goes out with ONE idempotency key made when this screen opened
 * and reused for every retry of it, so a dropped connection followed by "Retry" can never sign twice: the server replays the first
 * result. If the outcome is unknown (no response at all) the screen says so and keeps the same key.
 */
export function FormSignReview({ form, patientName, onBack, onSigned, onReload }: Props) {
  const [signerName, setSignerName] = useState("");
  const [relationship, setRelationship] = useState("");
  const [note, setNote] = useState("");
  const [signature, setSignature] = useState("");
  const [attested, setAttested] = useState(false);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [unconfirmed, setUnconfirmed] = useState(false);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const key = useRef(newIdempotencyKey());
  const ids = { name: useId(), rel: useId(), note: useId(), att: useId() };

  async function sign(event: FormEvent) {
    event.preventDefault();
    if (busy) return;
    setBusy(true);
    setFormError(null);
    setUnconfirmed(false);
    setErrors({});
    try {
      onSigned(
        await signPatientForm(
          form.summary.id,
          { signerName, relationship, relationshipNote: note, signatureText: signature, attested, templateVersionId: form.version.id, rowVersion: form.rowVersion },
          key.current,
        ),
      );
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else if (err instanceof ApiError && err.code === "already_signed") onReload(); // someone (or an earlier attempt) already signed it: show the signed copy
      else if (err instanceof ApiError && err.code === "validation_failed") {
        const fe = formFieldErrorsOf(err);
        if (Object.keys(fe).some((k) => k.startsWith("responses."))) onBack(fe); // an answer needs attention: back to the form
        else setErrors(fe);
      } else if (isNetworkFailure(err)) setUnconfirmed(true);
      else setFormError(err instanceof ApiError ? err.message : "Could not sign. Try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="alv-workspace__form" onSubmit={sign} noValidate aria-label="Review and sign">
      <h2 className="alv-workspace__section-title">Review before signing</h2>
      <p className="alv-workspace__note">
        {form.summary.title} - version {form.summary.templateVersionNumber}. Check the wording and the answers below; once signed, this copy cannot be changed.
      </p>
      {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={onReload} />}

      <section aria-labelledby={`${ids.att}-wording`}>
        <h3 id={`${ids.att}-wording`} className="alv-workspace__subtitle">What the form says</h3>
        <div className="alv-forms__wording" tabIndex={0} role="region" aria-label="Form wording">{form.version.body}</div>
      </section>
      <section aria-labelledby={`${ids.att}-answers`}>
        <h3 id={`${ids.att}-answers`} className="alv-workspace__subtitle">Answers for {patientName}</h3>
        <FormAnswers fields={form.version.fields} responses={form.responses} />
      </section>

      <fieldset className="alv-patient-fields">
        <legend>Who is signing</legend>
        <FormField id={ids.name} label="Signer's full name *" value={signerName} onChange={(e) => setSignerName(e.target.value)} error={errors.signerName} autoComplete="off" />
        <div className="alv-form-field">
          <label htmlFor={ids.rel} className="alv-form-field__label">Relationship to the patient *</label>
          <select id={ids.rel} className={`alv-form-field__input${errors.relationship ? " alv-form-field__input--error" : ""}`} value={relationship}
            onChange={(e) => setRelationship(e.target.value)} aria-invalid={errors.relationship ? true : undefined} aria-describedby={errors.relationship ? `${ids.rel}-error` : undefined}>
            <option value="">Choose…</option>
            {SIGNER_RELATIONSHIPS.map((r) => <option key={r} value={r}>{r === "Self" ? "Self (the patient)" : r}</option>)}
          </select>
          {errors.relationship && <p id={`${ids.rel}-error`} className="alv-form-field__error" role="alert">{errors.relationship}</p>}
        </div>
        {relationship === "Other" && (
          <FormField id={ids.note} label="Describe the relationship *" value={note} onChange={(e) => setNote(e.target.value)} error={errors.relationshipNote} />
        )}
        <FormField label="Type your name as your signature *" value={signature} onChange={(e) => setSignature(e.target.value)} error={errors.signatureText} autoComplete="off" />
      </fieldset>

      <div className="alv-forms__attestation">
        <label className="alv-workspace__checkbox" htmlFor={`${ids.att}-box`}>
          <input id={`${ids.att}-box`} type="checkbox" checked={attested} onChange={(e) => setAttested(e.target.checked)} aria-invalid={errors.attested ? true : undefined} />
          <span>{ATTESTATION}</span>
        </label>
        {errors.attested && <p className="alv-form-field__error" role="alert">{errors.attested}</p>}
      </div>
      <p className="alv-forms__legal">
        A typed signature records who signed this practice's form and when. It does not by itself establish that the form is legally sufficient for every
        situation; the wording of each form is the practice's own.
      </p>

      {unconfirmed && (
        <div className="alv-forms__banner" role="alert">
          <p>We could not confirm whether the signature was saved - the connection dropped before an answer came back.</p>
          <p>You can press Sign again: the same signature is recognised, so it cannot be recorded twice. Or check the form's current status first.</p>
          <Button type="button" onClick={onReload}>Check status</Button>
        </div>
      )}
      {formError && <p className="alv-form-field__error" role="alert">{formError}</p>}
      <div className="alv-forms__actions">
        <Button type="submit" variant="primary" disabled={busy}>{busy ? "Signing…" : unconfirmed ? "Sign again" : "Sign form"}</Button>
        <Button type="button" onClick={() => onBack()} disabled={busy}>Back to edit</Button>
      </div>
    </form>
  );
}
