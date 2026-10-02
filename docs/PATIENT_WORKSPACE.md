# Patient identity workspace (ALV-003-C01)

How patients, households, guarantors, duplicates and the shared patient header work, for the stories that build on them. STORY-003 (registration) is the
base; this extends it without changing its contract.

## The model
- **Patient** holds demographics and contact details, `IsActive`, a row version, and two independent links: **household** and **guarantor**.
- **Household** is a grouping (`Patient.HouseholdId` + `HouseholdRelationship`). Joining names an existing patient as the anchor; a household is created
  for the anchor if it has none; a household nobody belongs to is removed. A patient is in at most one household.
- **Guarantor** (`Patient.GuarantorPatientId`) is the patient financially responsible. `null` means self-responsible. The guarantor must be an active patient
  who is themselves self-responsible (no chains, hence no cycles); a patient who guarantees others cannot be given a guarantor of their own. The guarantor
  need not be in the household, and household members may have different guarantors. A guarantor must be a registered patient (a responsible party who is
  not a patient is not modelled yet).
- Patients are **inactivated, never deleted**; every relationship is `Restrict`. A guarantor of active patients cannot be inactivated.
- **History** (`PatientHistoryEntry`) has one row per changed field with old/new value, who and when. The **audit log** records that a change happened, with
  field *names* only - it stays PHI-free. History rows live in the patient record's own table behind `ViewPatientRecords`.

## Duplicates (never merged)
- **Exact** (same normalized name and birth date): refused outright with a pointer to the existing patient. A unique index on `DuplicateKey` makes this hold
  even when two registrations race.
- **Likely** (all require the same birth date): same last name; same first name with a last name one or two typos away; same phone digits; same email.
  The front desk sees a side-by-side comparison with the reason and may register anyway; the server requires every flagged patient to have been acknowledged.
  A shared surname, phone or email alone is a normal family and is not flagged.
- Outcomes are recorded as `patient.duplicate-check` measurement events (category and count only).

## Concurrency and idempotency
Every edit, status change and relationship change carries the row version it read; a stale one is the shared 409 `concurrency_conflict`. Registration is
idempotent by its `Idempotency-Key`. No-op saves change nothing (no history, audit or version bump).

## Practice requirements
A practice may require email and/or sex (`PatientRegistrationSettings`, one row, `/admin/patient-registration`). The server applies them on registration,
edit and the early duplicate check; the forms mark them. Changing a requirement does not invalidate existing patients - it applies the next time one is
registered or edited.

## The shared patient header and workspace (the extension point)
- `PatientContextProvider` (in the app shell) owns the patient in context. `selectPatient` drops the previous patient *immediately*; a response is applied
  only if it is still the latest request, so a slow answer for patient A can never replace patient B; the workspace renders nothing for a patient other than
  the URL's.
- `PatientHeader` shows only what the system knows (name, birth date and age, sex, phone, inactive flag, guarantor). **Add nothing here that does not exist**:
  no placeholder balances, alerts, diagnoses or counts. A later story adds its own indicator when it can produce a real value.
- To add a tab to the workspace: append an entry to `src/app/patientWorkspaceTabs.ts` (id, label, path, optional `requiredPermission`) and a child `<Route>`
  under `/patients/:patientId` in `App.tsx`. Read the patient from the outlet context (`useOutletContext`); never fetch your own copy of "the current patient".
  Panels are remounted per patient (the workspace is keyed by patient id), so their local state cannot leak between patients.

## Permissions
`RegisterPatients` (register, duplicate check), `EditPatients` (edit, status, household, guarantor), `ViewPatientRecords` (search, detail, history, requirement
settings read), `ManagePracticeConfiguration` (change requirements). Front desk holds the first three; the server re-checks every call.
