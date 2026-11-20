import { FormField } from "./FormField";
import { SelectField } from "./SelectField";
import type { SelectOption } from "./SelectField";
import { NO_EXTRA_REQUIREMENTS } from "./patientFields";
import type { PatientValues } from "./patientFields";
import { useDemographicOptions } from "./useDemographicOptions";
import type { PatientRequirements } from "../services/patientsApi";
import "./PatientFieldsForm.css";
import { sessionToday } from "../time/practiceTime";

// ALV-N021 A1/A3: the latest date of birth allowed is the practice's today (no limit is shown without a practice zone; the server checks it anyway)
const today = () => sessionToday() ?? undefined;

/** ALV-N025 (DEM-2): the prompt beside a stored value that is not on its list - kept until a person chooses a supported one. */
export const NOT_IN_LIST_HINT = "Not in the list - please choose a supported value.";

interface Props {
  values: PatientValues;
  errors: Record<string, string>;
  disabled?: boolean;
  onChange: (name: keyof PatientValues) => (e: { target: { value: string } }) => void;
  /** Extra fields this practice requires; they are marked * and aria-required like the always-required ones. */
  requirements?: PatientRequirements;
}

/** The options of a list, with a stored value that is not on it kept (and marked) so it is shown as it is, never silently replaced. */
function withStored(options: SelectOption[], stored: string): { options: SelectOption[]; oldValue: boolean } {
  if (stored === "" || options.some((o) => o.value === stored)) return { options, oldValue: false };
  return { options: [...options, { value: stored, label: `${stored} (not in the list)` }], oldValue: true };
}

/**
 * The demographics and contact fields, shared by the registration form and the patient details editor so the two can
 * never drift apart. Required fields are marked * and aria-required; an error is announced next to its field.
 *
 * ALV-N025 (F-032, F-033; owner decisions DEM-2..DEM-4): Sex is a list; Country is a list (ISO codes, the practice's default when none is chosen); State is the US list
 * of codes for the US and free text "State / Province / Region" elsewhere. A stored value not on its list stays selected, marked, with a prompt to choose.
 */
export function PatientFieldsForm({ values, errors, disabled, onChange, requirements = NO_EXTRA_REQUIREMENTS }: Props) {
  const options = useDemographicOptions();
  const err = (name: keyof PatientValues) => errors[name] || undefined;
  const country = values.country || options.defaultCountry;
  const usList = country === "US" && options.usStates.length > 0;

  const sex = withStored(
    [{ value: "", label: requirements.requireSex ? "Choose…" : "Not specified" }, ...options.sexValues.map((v) => ({ value: v, label: v }))],
    values.sex ?? "",
  );
  const state = withStored([{ value: "", label: "Choose…" }, ...options.usStates.map((s) => ({ value: s.code, label: `${s.name} (${s.code})` }))], values.state ?? "");
  const countries = withStored(
    [...options.countries].sort((a, b) => a.name.localeCompare(b.name)).map((c) => ({ value: c.code, label: c.name })),
    country,
  );

  return (
    <>
      <fieldset className="alv-patient-fields" disabled={disabled}>
        <legend>Demographics</legend>
        <FormField label="First name *" value={values.firstName} maxLength={80} autoComplete="off" required aria-required="true" error={err("firstName")} onChange={onChange("firstName")} />
        <FormField label="Middle name" value={values.middleName} maxLength={80} autoComplete="off" error={err("middleName")} onChange={onChange("middleName")} />
        <FormField label="Last name *" value={values.lastName} maxLength={80} autoComplete="off" required aria-required="true" error={err("lastName")} onChange={onChange("lastName")} />
        <FormField label="Date of birth *" type="date" max={today()} value={values.dateOfBirth} required aria-required="true" error={err("dateOfBirth")} onChange={onChange("dateOfBirth")} />
        <SelectField
          label={requirements.requireSex ? "Sex *" : "Sex"}
          options={sex.options}
          value={values.sex ?? ""}
          required={requirements.requireSex}
          aria-required={requirements.requireSex ? "true" : undefined}
          hint={sex.oldValue ? NOT_IN_LIST_HINT : requirements.requireSex ? undefined : "Optional"}
          error={err("sex")}
          onChange={onChange("sex")}
        />
      </fieldset>
      <fieldset className="alv-patient-fields" disabled={disabled}>
        <legend>Contact details</legend>
        <FormField label="Phone *" type="tel" value={values.phone} maxLength={40} autoComplete="off" required aria-required="true" error={err("phone")} onChange={onChange("phone")} />
        <FormField label={requirements.requireEmail ? "Email *" : "Email"} type="email" value={values.email} maxLength={200} autoComplete="off" required={requirements.requireEmail} aria-required={requirements.requireEmail ? "true" : undefined} hint={requirements.requireEmail ? undefined : "Optional"} error={err("email")} onChange={onChange("email")} />
        <FormField label="Address *" value={values.addressLine1} maxLength={200} autoComplete="off" required aria-required="true" error={err("addressLine1")} onChange={onChange("addressLine1")} />
        <FormField label="Address line 2" value={values.addressLine2} maxLength={200} autoComplete="off" error={err("addressLine2")} onChange={onChange("addressLine2")} />
        <FormField label="City *" value={values.city} maxLength={100} autoComplete="off" required aria-required="true" error={err("city")} onChange={onChange("city")} />
        <SelectField label="Country *" options={countries.options} value={country} required aria-required="true" error={err("country")} onChange={onChange("country")} />
        {usList ? (
          <SelectField label="State *" options={state.options} value={values.state ?? ""} required aria-required="true" hint={state.oldValue ? NOT_IN_LIST_HINT : undefined} error={err("state")} onChange={onChange("state")} />
        ) : (
          // the US list not loaded (yet): State typed as text, still labelled as a US state; any other country: free text "State / Province / Region"
          <FormField label={country === "US" ? "State *" : "State / Province / Region *"} value={values.state} maxLength={50} autoComplete="off" required aria-required="true" error={err("state")} onChange={onChange("state")} />
        )}
        <FormField label="Postal code *" value={values.postalCode} maxLength={20} autoComplete="off" required aria-required="true" error={err("postalCode")} onChange={onChange("postalCode")} />
      </fieldset>
    </>
  );
}
