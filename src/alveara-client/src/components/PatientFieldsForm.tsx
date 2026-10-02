import { FormField } from "./FormField";
import { NO_EXTRA_REQUIREMENTS } from "./patientFields";
import type { PatientValues } from "./patientFields";
import type { PatientRequirements } from "../services/patientsApi";
import "./PatientFieldsForm.css";

const today = () => new Date().toISOString().slice(0, 10);

interface Props {
  values: PatientValues;
  errors: Record<string, string>;
  disabled?: boolean;
  onChange: (name: keyof PatientValues) => (e: { target: { value: string } }) => void;
  /** Extra fields this practice requires; they are marked * and aria-required like the always-required ones. */
  requirements?: PatientRequirements;
}

/**
 * The demographics and contact fields, shared by the registration form and the patient details editor so the two can
 * never drift apart. Required fields are marked * and aria-required; an error is announced next to its field.
 */
export function PatientFieldsForm({ values, errors, disabled, onChange, requirements = NO_EXTRA_REQUIREMENTS }: Props) {
  const err = (name: keyof PatientValues) => errors[name] || undefined;
  return (
    <>
      <fieldset className="alv-patient-fields" disabled={disabled}>
        <legend>Demographics</legend>
        <FormField label="First name *" value={values.firstName} maxLength={80} autoComplete="off" required aria-required="true" error={err("firstName")} onChange={onChange("firstName")} />
        <FormField label="Middle name" value={values.middleName} maxLength={80} autoComplete="off" error={err("middleName")} onChange={onChange("middleName")} />
        <FormField label="Last name *" value={values.lastName} maxLength={80} autoComplete="off" required aria-required="true" error={err("lastName")} onChange={onChange("lastName")} />
        <FormField label="Date of birth *" type="date" max={today()} value={values.dateOfBirth} required aria-required="true" error={err("dateOfBirth")} onChange={onChange("dateOfBirth")} />
        <FormField label={requirements.requireSex ? "Sex *" : "Sex"} value={values.sex} maxLength={30} autoComplete="off" required={requirements.requireSex} aria-required={requirements.requireSex ? "true" : undefined} hint={requirements.requireSex ? undefined : "Optional"} error={err("sex")} onChange={onChange("sex")} />
      </fieldset>
      <fieldset className="alv-patient-fields" disabled={disabled}>
        <legend>Contact details</legend>
        <FormField label="Phone *" type="tel" value={values.phone} maxLength={40} autoComplete="off" required aria-required="true" error={err("phone")} onChange={onChange("phone")} />
        <FormField label={requirements.requireEmail ? "Email *" : "Email"} type="email" value={values.email} maxLength={200} autoComplete="off" required={requirements.requireEmail} aria-required={requirements.requireEmail ? "true" : undefined} hint={requirements.requireEmail ? undefined : "Optional"} error={err("email")} onChange={onChange("email")} />
        <FormField label="Address *" value={values.addressLine1} maxLength={200} autoComplete="off" required aria-required="true" error={err("addressLine1")} onChange={onChange("addressLine1")} />
        <FormField label="Address line 2" value={values.addressLine2} maxLength={200} autoComplete="off" error={err("addressLine2")} onChange={onChange("addressLine2")} />
        <FormField label="City *" value={values.city} maxLength={100} autoComplete="off" required aria-required="true" error={err("city")} onChange={onChange("city")} />
        <FormField label="State *" value={values.state} maxLength={50} autoComplete="off" required aria-required="true" error={err("state")} onChange={onChange("state")} />
        <FormField label="Postal code *" value={values.postalCode} maxLength={20} autoComplete="off" required aria-required="true" error={err("postalCode")} onChange={onChange("postalCode")} />
      </fieldset>
    </>
  );
}
