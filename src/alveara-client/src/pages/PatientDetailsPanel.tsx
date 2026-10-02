import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../components/Button";
import { ConcurrencyConflictBanner } from "../components/ConcurrencyConflictBanner";
import { PatientFieldsForm } from "../components/PatientFieldsForm";
import { missingRequiredFields } from "../components/patientFields";
import type { PatientValues } from "../components/patientFields";
import { SafeLink } from "../components/SafeLink";
import { useAuth } from "../contexts/AuthContext";
import { usePatientRequirements } from "../contexts/patientRequirementsStore";
import { useUnsavedChangesWarning } from "../hooks/useUnsavedChangesWarning";
import { ApiError, isConcurrencyConflict } from "../services/authApi";
import type { ConcurrencyConflictProblem } from "../services/authApi";
import { existingPatientIdOf, fieldErrorsOf, setPatientActive, updatePatient } from "../services/patientsApi";
import type { PatientDetail } from "../services/patientsApi";
import "./PatientWorkspace.css";

const toValues = (p: PatientDetail): PatientValues => ({
  firstName: p.firstName, middleName: p.middleName ?? "", lastName: p.lastName, dateOfBirth: p.dateOfBirth, sex: p.sex ?? "",
  phone: p.phone, email: p.email ?? "", addressLine1: p.addressLine1, addressLine2: p.addressLine2 ?? "", city: p.city, state: p.state, postalCode: p.postalCode,
});

/**
 * ALV-003-C01: view and safely edit one patient's demographics and contact details, and inactivate/reactivate them.
 * Every save carries the version the form was loaded from: if someone else changed the patient meanwhile the save is
 * refused and the user is told (never silently overwritten); "Reload" re-reads the current record. Inactivating keeps the
 * record, its history and its relationships. Without EditPatients the form is read-only.
 */
export function PatientDetailsPanel({ patient, onChanged }: { patient: PatientDetail; onChanged: () => void }) {
  const { hasPermission } = useAuth();
  const canEdit = hasPermission("EditPatients");
  const requirements = usePatientRequirements();
  const [values, setValues] = useState<PatientValues>(() => toValues(patient));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<{ message: string; existingId: string | null } | null>(null);
  const [conflict, setConflict] = useState<ConcurrencyConflictProblem | null>(null);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const formRef = useRef<HTMLFormElement>(null);

  // The form always starts from, and returns to, the server's current version of THIS patient.
  const version = patient.rowVersion;
  useEffect(() => {
    setValues(toValues(patient));
    setErrors({});
    setFormError(null);
    setConflict(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- reset exactly when the server's version of the record changes
  }, [patient.id, version]);

  const dirty = JSON.stringify(values) !== JSON.stringify(toValues(patient));
  useUnsavedChangesWarning(dirty);

  const onChange = (name: keyof PatientValues) => (e: { target: { value: string } }) => {
    setNotice(null);
    setValues((v) => ({ ...v, [name]: e.target.value }));
    if (errors[name]) setErrors((prev) => ({ ...prev, [name]: "" }));
  };

  function focusFirstInvalid() {
    requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
  }

  async function save(event: FormEvent) {
    event.preventDefault();
    if (saving || !canEdit) return;
    setFormError(null);
    setNotice(null);
    const found = missingRequiredFields(values, requirements);
    if (Object.keys(found).length > 0) {
      setErrors(found);
      focusFirstInvalid();
      return;
    }
    setErrors({});
    setSaving(true);
    try {
      await updatePatient(patient.id, values, version);
      setNotice("Patient details saved.");
      onChanged();
    } catch (err) {
      const fields = fieldErrorsOf(err);
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else if (Object.keys(fields).length > 0) {
        setErrors(fields);
        setFormError({ message: "Some fields need attention before the changes can be saved.", existingId: null });
        focusFirstInvalid();
      } else setFormError({ message: err instanceof ApiError ? err.message : "Could not save. Check your connection and try again.", existingId: existingPatientIdOf(err) });
    } finally {
      setSaving(false);
    }
  }

  async function toggleActive() {
    if (saving || !canEdit) return;
    const next = !patient.isActive;
    if (!next && !window.confirm(`Inactivate ${patient.firstName} ${patient.lastName}? Their record, history and relationships are kept.`)) return;
    setFormError(null);
    setNotice(null);
    setSaving(true);
    try {
      await setPatientActive(patient.id, next, version);
      setNotice(next ? "Patient reactivated." : "Patient inactivated.");
      onChanged();
    } catch (err) {
      if (isConcurrencyConflict(err)) setConflict(err.body);
      else setFormError({ message: err instanceof ApiError ? err.message : "Could not change the status. Check your connection and try again.", existingId: null });
    } finally {
      setSaving(false);
    }
  }

  return (
    <section aria-labelledby="alv-details-title">
      <h2 id="alv-details-title" className="alv-workspace__section-title">Details</h2>
      {!canEdit && <p className="alv-workspace__note">You can view this patient but not change them.</p>}
      {/* Saved confirmations are inline, announced politely, and use the same text-on-tint treatment as the status badges. */}
      <p className={notice ? "alv-workspace__saved" : "alv-workspace__saved-slot"} role="status">{notice}</p>
      <form ref={formRef} className="alv-workspace__form" onSubmit={save} noValidate aria-label="Patient details">
        {conflict && <ConcurrencyConflictBanner problem={conflict} onReload={onChanged} />}
        {formError && (
          <div className="alv-workspace__banner" role="alert">
            <p>{formError.message}</p>
            {formError.existingId && (
              <p>
                <SafeLink to={`/patients/${formError.existingId}`} className="alv-workspace__link">Open the existing patient</SafeLink>
              </p>
            )}
          </div>
        )}
        <PatientFieldsForm values={values} errors={errors} disabled={saving || !canEdit} onChange={onChange} requirements={requirements} />
        {canEdit && (
          <div className="alv-workspace__actions">
            <Button type="submit" variant="primary" disabled={!dirty || saving}>
              {saving ? "Saving…" : "Save changes"}
            </Button>
            <Button type="button" onClick={() => { setValues(toValues(patient)); setErrors({}); setFormError(null); }} disabled={!dirty || saving}>
              Discard changes
            </Button>
            {dirty && <span className="alv-workspace__dirty">Unsaved changes</span>}
          </div>
        )}
      </form>

      {canEdit && (
        <div className="alv-workspace__status">
          <h3 className="alv-workspace__subtitle">Patient status</h3>
          <p className="alv-workspace__note">
            {patient.isActive
              ? "This patient is active. Inactivating hides them from search by default; nothing is deleted."
              : "This patient is inactive. Their record and history are kept; reactivate them to use them as a guarantor or household anchor."}
          </p>
          <Button type="button" variant={patient.isActive ? "danger" : "primary"} onClick={toggleActive} disabled={saving}>
            {patient.isActive ? "Inactivate patient" : "Reactivate patient"}
          </Button>
        </div>
      )}
    </section>
  );
}
