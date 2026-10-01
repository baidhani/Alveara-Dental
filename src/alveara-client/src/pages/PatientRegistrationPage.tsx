import { useRef, useState } from "react";
import type { FormEvent } from "react";
import { Button } from "../components/Button";
import { FormField } from "../components/FormField";
import { PageHeader } from "../components/PageHeader";
import { useUnsavedChangesWarning } from "../hooks/useUnsavedChangesWarning";
import { ApiError } from "../services/authApi";
import { existingPatientIdOf, fieldErrorsOf, registerPatient } from "../services/patientsApi";
import type { PatientRecord, RegisterPatientInput } from "../services/patientsApi";
import "./PatientRegistrationPage.css";

type Values = RegisterPatientInput;
const EMPTY: Values = {
  firstName: "", middleName: "", lastName: "", dateOfBirth: "", sex: "",
  phone: "", email: "", addressLine1: "", addressLine2: "", city: "", state: "", postalCode: "",
};

/** Mirrors the server's required fields so the front desk is prompted before a round trip; the server stays the authority. */
const REQUIRED: { name: keyof Values; label: string }[] = [
  { name: "firstName", label: "First name" },
  { name: "lastName", label: "Last name" },
  { name: "dateOfBirth", label: "Date of birth" },
  { name: "phone", label: "Phone" },
  { name: "addressLine1", label: "Address" },
  { name: "city", label: "City" },
  { name: "state", label: "State" },
  { name: "postalCode", label: "Postal code" },
];

const today = () => new Date().toISOString().slice(0, 10);

function clientErrors(values: Values): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const { name, label } of REQUIRED) {
    if (values[name].trim() === "") errors[name] = `${label} is required.`;
  }
  return errors;
}

/**
 * STORY-003's registration form. Required fields are named and marked; missing ones are announced
 * inline and focus moves to the first one, so the whole form works from the keyboard. One
 * idempotency key is held per registration attempt and reused if the request is retried, so a
 * dropped connection or a double click can never register the patient twice. Household and
 * guarantor details are deliberately absent: ALV-003-C01 owns them.
 */
export function PatientRegistrationPage() {
  const [values, setValues] = useState<Values>(EMPTY);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState<{ message: string; existingId: string | null } | null>(null);
  const [saving, setSaving] = useState(false);
  const [registered, setRegistered] = useState<PatientRecord | null>(null);
  const formRef = useRef<HTMLFormElement>(null);
  const attemptKey = useRef<string>(crypto.randomUUID());

  const dirty = registered === null && JSON.stringify(values) !== JSON.stringify(EMPTY);
  useUnsavedChangesWarning(dirty);

  const set = (name: keyof Values) => (e: { target: { value: string } }) => {
    setValues((v) => ({ ...v, [name]: e.target.value }));
    if (errors[name]) setErrors((prev) => ({ ...prev, [name]: "" }));
  };

  function focusFirstInvalid() {
    // Wait for React to render the aria-invalid flags before looking for the first one.
    requestAnimationFrame(() => formRef.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus());
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (saving) return;
    setFormError(null);
    const found = clientErrors(values);
    if (Object.keys(found).length > 0) {
      setErrors(found);
      focusFirstInvalid();
      return;
    }
    setErrors({});
    setSaving(true);
    try {
      const patient = await registerPatient(values, attemptKey.current);
      setRegistered(patient);
    } catch (err) {
      const serverFields = fieldErrorsOf(err);
      if (Object.keys(serverFields).length > 0) {
        setErrors(serverFields);
        setFormError({ message: "Some fields need attention before the patient can be registered.", existingId: null });
        focusFirstInvalid();
      } else if (err instanceof ApiError && err.code === "duplicate_patient") {
        setFormError({ message: err.message, existingId: existingPatientIdOf(err) });
      } else {
        // The key is kept, so pressing Register again after a dropped connection is a safe retry.
        setFormError({ message: err instanceof ApiError ? err.message : "Could not register the patient. Check your connection and try again.", existingId: null });
      }
    } finally {
      setSaving(false);
    }
  }

  function registerAnother() {
    attemptKey.current = crypto.randomUUID();
    setValues(EMPTY);
    setErrors({});
    setFormError(null);
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
          <Button variant="primary" onClick={registerAnother} autoFocus>
            Register another patient
          </Button>
        </section>
      </>
    );
  }

  const err = (name: keyof Values) => errors[name] || undefined;

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
        <fieldset className="alv-patient-reg__section" disabled={saving}>
          <legend>Demographics</legend>
          <FormField label="First name *" value={values.firstName} maxLength={80} autoComplete="off" required aria-required="true" error={err("firstName")} onChange={set("firstName")} />
          <FormField label="Middle name" value={values.middleName} maxLength={80} autoComplete="off" error={err("middleName")} onChange={set("middleName")} />
          <FormField label="Last name *" value={values.lastName} maxLength={80} autoComplete="off" required aria-required="true" error={err("lastName")} onChange={set("lastName")} />
          <FormField label="Date of birth *" type="date" max={today()} value={values.dateOfBirth} required aria-required="true" error={err("dateOfBirth")} onChange={set("dateOfBirth")} />
          <FormField label="Sex" value={values.sex} maxLength={30} autoComplete="off" hint="Optional" error={err("sex")} onChange={set("sex")} />
        </fieldset>
        <fieldset className="alv-patient-reg__section" disabled={saving}>
          <legend>Contact details</legend>
          <FormField label="Phone *" type="tel" value={values.phone} maxLength={40} autoComplete="off" required aria-required="true" error={err("phone")} onChange={set("phone")} />
          <FormField label="Email" type="email" value={values.email} maxLength={200} autoComplete="off" hint="Optional" error={err("email")} onChange={set("email")} />
          <FormField label="Address *" value={values.addressLine1} maxLength={200} autoComplete="off" required aria-required="true" error={err("addressLine1")} onChange={set("addressLine1")} />
          <FormField label="Address line 2" value={values.addressLine2} maxLength={200} autoComplete="off" error={err("addressLine2")} onChange={set("addressLine2")} />
          <FormField label="City *" value={values.city} maxLength={100} autoComplete="off" required aria-required="true" error={err("city")} onChange={set("city")} />
          <FormField label="State *" value={values.state} maxLength={50} autoComplete="off" required aria-required="true" error={err("state")} onChange={set("state")} />
          <FormField label="Postal code *" value={values.postalCode} maxLength={20} autoComplete="off" required aria-required="true" error={err("postalCode")} onChange={set("postalCode")} />
        </fieldset>
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
