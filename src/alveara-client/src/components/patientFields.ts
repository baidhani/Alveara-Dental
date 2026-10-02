import type { PatientRequirements, RegisterPatientInput } from "../services/patientsApi";

export type PatientValues = RegisterPatientInput;

export const EMPTY_PATIENT: PatientValues = {
  firstName: "", middleName: "", lastName: "", dateOfBirth: "", sex: "",
  phone: "", email: "", addressLine1: "", addressLine2: "", city: "", state: "", postalCode: "",
};

/** The fields the server requires. Mirrored here so the front desk is prompted before a round trip; the server stays the authority. */
export const REQUIRED_PATIENT_FIELDS: { name: keyof PatientValues; label: string }[] = [
  { name: "firstName", label: "First name" },
  { name: "lastName", label: "Last name" },
  { name: "dateOfBirth", label: "Date of birth" },
  { name: "phone", label: "Phone" },
  { name: "addressLine1", label: "Address" },
  { name: "city", label: "City" },
  { name: "state", label: "State" },
  { name: "postalCode", label: "Postal code" },
];

export const NO_EXTRA_REQUIREMENTS: PatientRequirements = { requireEmail: false, requireSex: false };

/** The fields that are required for THIS practice: the always-required ones plus any the practice has chosen to require. */
export function requiredFieldsFor(requirements: PatientRequirements = NO_EXTRA_REQUIREMENTS): { name: keyof PatientValues; label: string }[] {
  return [
    ...REQUIRED_PATIENT_FIELDS,
    ...(requirements.requireEmail ? [{ name: "email" as const, label: "Email" }] : []),
    ...(requirements.requireSex ? [{ name: "sex" as const, label: "Sex" }] : []),
  ];
}

export function missingRequiredFields(values: PatientValues, requirements: PatientRequirements = NO_EXTRA_REQUIREMENTS): Record<string, string> {
  const errors: Record<string, string> = {};
  for (const { name, label } of requiredFieldsFor(requirements)) {
    if (values[name].trim() === "") errors[name] = `${label} is required.`;
  }
  return errors;
}
