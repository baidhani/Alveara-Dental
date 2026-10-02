import { useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../components/Button";
import { DuplicateComparisonPanel } from "../components/DuplicateComparisonPanel";
import { PatientFieldsForm } from "../components/PatientFieldsForm";
import { EMPTY_PATIENT, missingRequiredFields } from "../components/patientFields";
import type { PatientValues } from "../components/patientFields";
import { PageHeader } from "../components/PageHeader";
import { SafeLink } from "../components/SafeLink";
import { usePatientRequirements } from "../contexts/patientRequirementsStore";
import { useUnsavedChangesWarning } from "../hooks/useUnsavedChangesWarning";
import { ApiError } from "../services/authApi";
import { candidatesOf, existingPatientIdOf, fieldErrorsOf, registerPatient } from "../services/patientsApi";
import type { DuplicateCandidate, PatientRecord } from "../services/patientsApi";
import "./PatientRegistrationPage.css";

interface Duplicates {
  candidates: DuplicateCandidate[];
  blocked: boolean;
  entered: PatientValues;
}

/**
 * STORY-003's registration form, extended by ALV-003-C01 with the duplicate comparison panel.
 *
 * Required fields are named and marked; missing ones are announced inline and focus moves to the first one, so the whole form
 * works from the keyboard. One idempotency key is held per registration attempt and reused if the request is retried, so a
 * dropped connection or a double click can never register the patient twice.
 *
 * Duplicates are caught BEFORE anything is created: an exact match (same name and birth date) is refused and the existing record
 * offered; a likely match opens a side-by-side comparison, and the front desk may "register anyway" only after seeing it
 * (the server verifies every flagged patient was acknowledged). Nothing is ever merged.
 */
export function PatientRegistrationPage() {
  const requirements = usePatientRequirements();
  const [values, setValues] = useState<PatientValues>(EMPTY_PATIENT);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<{ message: string; existingId: string | null } | null>(null);
  const [duplicates, setDuplicates] = useState<Duplicates | null>(null);
  const [saving, setSaving] = useState(false);
  const [registered, setRegistered] = useState<PatientRecord | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const attemptKey = useRef<string>(crypto.randomUUID());

  const dirty = registered === null && JSON.stringify(values) !== JSON.stringify(EMPTY_PATIENT);
  useUnsavedChangesWarning(dirty);

  const onChange = (name: keyof PatientValues) => (e: { target: { value: string } }) => {
    setValues((v) => ({ ...v, [name]: e.target.value }));
    if (errors[name]) setErrors((prev) => ({ ...prev, [name]: "" }));
    if (duplicates) setDuplicates(null); // the comparison was for what was typed before; it must be redone for what is typed now
  };

  function focusFirstInvalid() {
    // Wait for React to render the aria-invalid flags before looking for the first one.
    requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
  }

  async function attempt(acknowledged?: string[]) {
    setSaving(true);
    try {
      setRegistered(await registerPatient(values, attemptKey.current, acknowledged));
      setDuplicates(null);
    } catch (err) {
      const serverFields = fieldErrorsOf(err);
      const candidates = candidatesOf(err);
      if (Object.keys(serverFields).length > 0) {
        setErrors(serverFields);
        setFormError({ message: "Some fields need attention before the patient can be registered.", existingId: null });
        focusFirstInvalid();
      } else if (err instanceof ApiError && err.code === "possible_duplicate" && candidates.length > 0) {
        setDuplicates({ candidates, blocked: false, entered: values });
      } else if (err instanceof ApiError && err.code === "duplicate_patient") {
        setFormError({ message: err.message, existingId: existingPatientIdOf(err) });
        if (candidates.length > 0) setDuplicates({ candidates, blocked: true, entered: values });
      } else {
        // The key is kept, so pressing Register again after a dropped connection is a safe retry.
        setFormError({ message: err instanceof ApiError ? err.message : "Could not register the patient. Check your connection and try again.", existingId: null });
      }
    } finally {
      setSaving(false);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (saving) return;
    setFormError(null);
    setDuplicates(null);
    const found = missingRequiredFields(values, requirements);
    if (Object.keys(found).length > 0) {
      setErrors(found);
      focusFirstInvalid();
      return;
    }
    setErrors({});
    await attempt();
  }

  function registerAnyway() {
    if (saving || !duplicates) return;
    setFormError(null);
    void attempt(duplicates.candidates.map((c) => c.id));
  }

  function registerAnother() {
    attemptKey.current = crypto.randomUUID();
    setValues(EMPTY_PATIENT);
    setErrors({});
    setFormError(null);
    setDuplicates(null);
    setRegistered(null);
  }

  if (registered) {
    return (
      <>
        <PageHeader title="Register patient" />
        <section className="alv-patient-reg__done" role="status" aria-label="Registration complete">
          <h2 className="alv-patient-reg__done-title">Patient registered</h2>
          <p>
            {registered.firstName} {registered.lastName}, born {registered.dateOfBirth}, is now in the system.
          </p>
          <p className="alv-patient-reg__id">Patient ID: {registered.id}</p>
          <div className="alv-patient-reg__done-actions">
            <Button variant="primary" onClick={registerAnother} autoFocus>
              Register another patient
            </Button>
            <SafeLink to={`/patients/${registered.id}`} className="alv-button alv-button--secondary">
              Open patient workspace
            </SafeLink>
            <SafeLink to={`/patients/${registered.id}/household`} className="alv-button alv-button--secondary">
              Add household or guarantor
            </SafeLink>
          </div>
        </section>
      </>
    );
  }

  return (
    <>
      <PageHeader title="Register patient" description="Capture the patient's demographics and contact details. Fields marked * are required. Registrations are audited with your name and the time." />
      <form ref={formRef} className="alv-patient-reg" onSubmit={submit} noValidate aria-label="Register patient">
        {formError && (
          <div className="alv-patient-reg__banner" role="alert">
            <p>{formError.message}</p>
            {formError.existingId && <p>Existing patient ID: {formError.existingId}</p>}
          </div>
        )}
        {duplicates && (
          <DuplicateComparisonPanel
            entered={duplicates.entered}
            candidates={duplicates.candidates}
            blocked={duplicates.blocked}
            busy={saving}
            onRegisterAnyway={registerAnyway}
            onDismiss={() => {
              setDuplicates(null);
              formRef.current?.querySelector<HTMLElement>("input")?.focus();
            }}
          />
        )}
        <PatientFieldsForm values={values} errors={errors} disabled={saving} onChange={onChange} requirements={requirements} />
        <div className="alv-patient-reg__actions">
          <Button type="submit" variant="primary" disabled={saving}>
            {saving ? "Registering…" : "Register patient"}
          </Button>
          {dirty && <span className="alv-patient-reg__dirty">Unsaved changes</span>}
        </div>
      </form>
    </>
  );
}
