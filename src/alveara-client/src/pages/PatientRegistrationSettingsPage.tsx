import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../components/Button";
import { ConcurrencyConflictBanner } from "../components/ConcurrencyConflictBanner";
import { PageHeader } from "../components/PageHeader";
import { ErrorState, LoadingState } from "../components/StatePatterns";
import { PATIENT_REQUIREMENTS_CHANGED } from "../contexts/PatientRequirementsContext";
import { useUnsavedChangesWarning } from "../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../services/authApi";
import type { ConcurrencyConflictProblem } from "../services/authApi";
import { getRegistrationSettings, saveRegistrationSettings } from "../services/patientsApi";
import type { RegistrationSettings } from "../services/patientsApi";
import "./PatientWorkspace.css";

type State = { kind: "loading" } | { kind: "error" } | { kind: "loaded"; settings: RegistrationSettings };

/**
 * ALV-003-C01: the practice's patient registration requirements. Name, date of birth, phone and address are always required; the practice
 * may also require an email address and/or the patient's sex. The server enforces the choice wherever a patient is saved. A change applies
 * the next time a patient is registered or edited (existing patients are not invalidated). Saves carry the version they were loaded from.
 */
export function PatientRegistrationSettingsPage() {
  const [state, setState] = useState<State>({ kind: "loading" });
  const [requireEmail, setRequireEmail] = useState(false);
  const [requireSex, setRequireSex] = useState(false);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);

  const [reloadKey, setReloadKey] = useState(0);
  const reload = () => {
    setConflict(null);
    setState({ kind: "loading" });
    setReloadKey((k) => k + 1);
  };

  useEffect(() => {
    const controller = new AbortController();
    getRegistrationSettings(controller.signal)
      .then((settings) => {
        setState({ kind: "loaded", settings });
        setRequireEmail(settings.requireEmail);
        setRequireSex(settings.requireSex);
      })
      .catch(() => {
        if (!controller.signal.aborted) setState({ kind: "error" });
      });
    return () => controller.abort();
  }, [reloadKey]);

  const dirty = state.kind === "loaded" && (requireEmail !== state.settings.requireEmail || requireSex !== state.settings.requireSex);
  useUnsavedChangesWarning(dirty);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (state.kind !== "loaded" || saving || !dirty) return;
    setSaving(true);
    setError(null);
    setNotice(null);
    try {
      const settings = await saveRegistrationSettings(requireEmail, requireSex, state.settings.rowVersion);
      setState({ kind: "loaded", settings });
      setNotice("Registration requirements saved.");
      window.dispatchEvent(new Event(PATIENT_REQUIREMENTS_CHANGED));
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setError(err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <PageHeader
        title="Patient registration requirements"
        description="Choose which extra details the front desk must collect. Name, date of birth, phone and address are always required."
      />
      {state.kind === "loading" && <LoadingState label="Loading requirements…" />}
      {state.kind === "error" && <ErrorState title="Could not load the requirements" action={<Button onClick={reload}>Retry</Button>} />}
      {state.kind === "loaded" && (
        <form className="alv-workspace__form" onSubmit={save} noValidate aria-label="Patient registration requirements">
          {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={reload} />}
          <p className={notice ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{notice}</p>
          <fieldset className="alv-patient-fields" disabled={saving}>
            <legend>Also require</legend>
            <label className="alv-workspace__checkbox">
              <input type="checkbox" checked={requireEmail} onChange={(e) => setRequireEmail(e.target.checked)} />
              An email address
            </label>
            <label className="alv-workspace__checkbox">
              <input type="checkbox" checked={requireSex} onChange={(e) => setRequireSex(e.target.checked)} />
              The patient's sex
            </label>
          </fieldset>
          <p className="alv-workspace__note">
            A change applies the next time a patient is registered or edited. Patients already registered are not changed; editing one who lacks a newly
            required detail will ask for it.
          </p>
          {error && <p className="alv-form-field__error" role="alert">{error}</p>}
          <div className="alv-workspace__actions">
            <Button type="submit" variant="primary" disabled={!dirty || saving}>
              {saving ? "Saving…" : "Save requirements"}
            </Button>
            {dirty && <span className="alv-workspace__dirty">Unsaved changes</span>}
          </div>
        </form>
      )}
    </>
  );
}
