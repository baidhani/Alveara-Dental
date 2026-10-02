import { useCallback, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "../../components/Button";
import { ConcurrencyConflictBanner } from "../../components/ConcurrencyConflictBanner";
import { FormFieldInputs } from "../../components/FormFieldInputs";
import { SafeLink } from "../../components/SafeLink";
import { ErrorState, LoadingState } from "../../components/StatePatterns";
import { useAuth } from "../../contexts/AuthContext";
import { useUnsavedChangesWarning } from "../../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../../services/authApi";
import type { ConcurrencyConflictProblem } from "../../services/authApi";
import { CATEGORY_LABELS, formFieldErrorsOf, getPatientForm, restartFormOnLatest, saveFormResponses, startPatientForm } from "../../services/formsApi";
import type { PatientFormDetail } from "../../services/formsApi";
import type { PatientDetail } from "../../services/patientsApi";
import { FormSignReview } from "./FormSignReview";
import { FormStatusBadge } from "./FormStatusBadge";
import { FormVoidPanel } from "./FormVoidPanel";
import { FormHistory, SignedFormView } from "./SignedFormView";
import "./Forms.css";

type Load = { kind: "loading" } | { kind: "not-found" } | { kind: "error" } | { kind: "loaded"; form: PatientFormDetail };

/**
 * ALV-N010: one patient form - complete it (draft), review and sign it, or read the signed/void copy with its history.
 * A form that does not belong to the patient in context is never shown (the URL's form id is checked against the patient).
 */
export function PatientFormView({ patient, formId }: { patient: PatientDetail; formId: string }) {
  const { hasPermission } = useAuth();
  const navigate = useNavigate();
  const [load, setLoad] = useState<Load>({ kind: "loading" });
  const [values, setValues] = useState<Record<string, string>>({});
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [mode, setMode] = useState<"edit" | "review">("edit");
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  const adopt = useCallback((form: PatientFormDetail) => {
    setLoad({ kind: "loaded", form });
    setValues(form.responses);
    setErrors({});
    setConflict(null);
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    getPatientForm(formId, controller.signal)
      .then((form) => (form.summary.patientId === patient.id ? adopt(form) : setLoad({ kind: "not-found" })))
      .catch((err) => {
        if (controller.signal.aborted) return;
        setLoad(err instanceof ApiError && err.status === 404 ? { kind: "not-found" } : { kind: "error" });
      });
    return () => controller.abort();
  }, [formId, patient.id, reloadKey, adopt]);

  const form = load.kind === "loaded" ? load.form : null;
  const isDraft = form?.summary.status === "Draft";
  const canComplete = hasPermission("CompleteForms");
  const dirty = isDraft && form !== null && JSON.stringify(values) !== JSON.stringify(form.responses);
  useUnsavedChangesWarning(!!dirty);

  const reload = () => {
    setLoad({ kind: "loading" });
    setMode("edit");
    setReloadKey((k) => k + 1);
  };

  async function persist(): Promise<PatientFormDetail | null> {
    if (!form) return null;
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      const saved = await saveFormResponses(form.summary.id, values, form.rowVersion);
      adopt(saved);
      return saved;
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else if (err instanceof ApiError && err.code === "validation_failed") setErrors(formFieldErrorsOf(err));
      else setError(err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.");
      return null;
    } finally {
      setBusy(false);
    }
  }

  async function saveDraft() {
    if (await persist()) setNotice("Draft saved.");
  }

  async function startReview() {
    if (!form) return;
    const missing: Record<string, string> = {};
    for (const f of form.version.fields) {
      const v = values[f.id] ?? "";
      if (f.required && (f.kind === "checkbox" ? v !== "true" : v.trim() === "")) missing[`responses.${f.id}`] = f.kind === "checkbox" ? "This must be checked." : "This is required.";
    }
    if (Object.keys(missing).length > 0) {
      setErrors(missing);
      setError("Some required answers are missing.");
      return;
    }
    const saved = dirty ? await persist() : form;
    if (saved) setMode("review");
  }

  async function moveToNewer() {
    if (!form) return;
    setBusy(true);
    setError(null);
    try {
      const moved = await restartFormOnLatest(form.summary.id, form.rowVersion);
      navigate(`/patients/${patient.id}/forms/${moved.summary.id}`);
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setError(err instanceof ApiError ? err.message : "Could not move the draft. Try again.");
    } finally {
      setBusy(false);
    }
  }

  async function startCorrected() {
    if (!form) return;
    setBusy(true);
    setError(null);
    try {
      const started = await startPatientForm(patient.id, form.summary.templateId);
      navigate(`/patients/${patient.id}/forms/${started.summary.id}`);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not start a new form. Try again.");
    } finally {
      setBusy(false);
    }
  }

  if (load.kind === "loading") return <LoadingState label="Loading form…" />;
  if (load.kind === "not-found") {
    return <ErrorState title="That form was not found for this patient" action={<SafeLink to={`/patients/${patient.id}/forms`} className="alv-button alv-button--primary">Back to forms</SafeLink>} />;
  }
  if (load.kind === "error" || !form) return <ErrorState title="Could not load the form" action={<Button onClick={reload}>Retry</Button>} />;

  const s = form.summary;
  const patientName = `${patient.firstName} ${patient.lastName}`;

  return (
    <div className="alv-workspace__panel">
      <div>
        <SafeLink to={`/patients/${patient.id}/forms`} className="alv-workspace__link">← All forms for this patient</SafeLink>
        <h2 className="alv-workspace__section-title" style={{ marginTop: "var(--space-2)" }}>{s.title}</h2>
        <div className="alv-forms__meta">
          <FormStatusBadge status={s.status} wasSigned={s.wasSigned} />
          <span>{CATEGORY_LABELS[s.category] ?? s.category}</span>
          <span>Template version {s.templateVersionNumber}</span>
          <span>Started {new Date(s.startedAtUtc).toLocaleString()}</span>
        </div>
      </div>

      {s.status === "Void" && (
        <div className="alv-forms__banner alv-forms__banner--void" role="status">
          <p><strong>This form is void</strong>{s.voidedAtUtc ? ` as of ${new Date(s.voidedAtUtc).toLocaleString()}` : ""}. Reason: {s.voidReason ?? "—"}</p>
          {canComplete && <Button onClick={startCorrected} disabled={busy}>Start a new form</Button>}
        </div>
      )}
      {error && !isDraft && <p className="alv-form-field__error" role="alert">{error}</p>}

      {isDraft && mode === "edit" && (
        <>
          {s.newerVersionAvailable && form.newerVersion && (
            <div className="alv-forms__banner" role="status">
              <p>
                A newer version (version {form.newerVersion.versionNumber}) of this form has been published. This draft stays on version {s.templateVersionNumber}
                and can still be signed as it is.
              </p>
              {canComplete && <Button onClick={moveToNewer} disabled={busy}>Move to version {form.newerVersion.versionNumber}</Button>}
              <p className="alv-workspace__note" style={{ marginTop: "var(--space-2)" }}>Answers that still fit are carried over; this draft is kept in the history as replaced.</p>
            </div>
          )}
          {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={reload} />}
          <p className={notice ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{notice}</p>
          <section aria-labelledby="alv-form-wording">
            <h3 id="alv-form-wording" className="alv-workspace__subtitle">What the form says</h3>
            <div className="alv-forms__wording" tabIndex={0} role="region" aria-label="Form wording">{form.version.body}</div>
          </section>
          <form className="alv-workspace__form" onSubmit={(e) => { e.preventDefault(); void saveDraft(); }} noValidate aria-label="Form answers">
            <FormFieldInputs fields={form.version.fields} values={values} errors={errors} disabled={busy || !canComplete}
              onChange={(id, v) => { setNotice(null); setValues((p) => ({ ...p, [id]: v })); if (errors[`responses.${id}`]) setErrors((p) => ({ ...p, [`responses.${id}`]: "" })); }} />
            {error && <p className="alv-form-field__error" role="alert">{error}</p>}
            {canComplete ? (
              <div className="alv-forms__actions">
                <Button type="submit" disabled={busy || !dirty}>{busy ? "Saving…" : "Save draft"}</Button>
                <Button type="button" variant="primary" onClick={() => void startReview()} disabled={busy}>Review and sign</Button>
                {dirty && <span className="alv-workspace__dirty">Unsaved changes</span>}
              </div>
            ) : (
              <p className="alv-workspace__note">Your role can view this draft but not complete it.</p>
            )}
          </form>
          {canComplete && <FormVoidPanel form={form} signed={false} onVoided={adopt} onReload={reload} />}
        </>
      )}

      {isDraft && mode === "review" && (
        <FormSignReview
          form={form}
          patientName={patientName}
          onBack={(fe) => { if (fe) { setErrors(fe); setError("Some answers need attention."); } setMode("edit"); }}
          onSigned={(f) => { adopt(f); setMode("edit"); setNotice("Form signed."); }}
          onReload={() => { setMode("edit"); reload(); }}
        />
      )}

      {s.status === "Signed" && form.snapshot && (
        <>
          <p className={notice ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{notice}</p>
          <SignedFormView form={form} />
          {hasPermission("VoidForms") && <FormVoidPanel form={form} signed onVoided={adopt} onReload={reload} />}
        </>
      )}
      {s.status === "Void" && form.snapshot && <SignedFormView form={form} />}

      <section aria-labelledby="alv-form-history">
        <h3 id="alv-form-history" className="alv-workspace__subtitle">History</h3>
        <FormHistory events={form.events} />
      </section>
    </div>
  );
}
