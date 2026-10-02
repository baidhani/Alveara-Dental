# ALV-003-C01 R01 — Next-story impact

Next scheduled item after this story is reviewed and closed: **item 13 — `ALV-N010` (Versioned Forms, Consents, and E-Signature Foundation)**; then the later stories that list `ALV-003-C01` as a dependency (the scheduling companions). Nothing here starts them. Details in `docs/PATIENT_WORKSPACE.md`.

## What later stories can now rely on
- **A patient exists, with identity, contact details, status and relationships.** Read it through the API (`GET /api/patients/{id}` → `PatientDetail`: identity, `age`, `isActive`, `rowVersion`, `guarantor`, `guaranteeFor`, `household` with members and labels) — the `Patient` row is the authority; do not copy identity fields into other tables, store the patient id.
- **The persistent patient-context workspace.** `PatientContextProvider` (in the shell) owns the patient in context; use `usePatientContext()` (`contexts/patientContextStore.ts`) rather than fetching "the current patient" yourself. Its switch-safety guarantees (previous patient dropped immediately, late responses discarded, StrictMode-safe) are tested; a story that adds patient-scoped state must live under the workspace route (so it is remounted per patient) or key itself by `patient.id`.
- **A tab extension point.** Add a workspace tab by appending to `src/app/patientWorkspaceTabs.ts` (id, label, path, optional `requiredPermission`) and a child `<Route>` under `/patients/:patientId` in `App.tsx`; read the patient from the outlet context. Only list tabs that exist.
- **The header shows only real data.** Add an indicator (balance, alert, unsigned work…) only when your story produces a real value for it; no placeholders.
- **Permissions:** `ViewPatientRecords` (search/detail/history), `RegisterPatients`, `EditPatients`, `ManagePracticeConfiguration` (requirements). Front desk now holds `ViewPatientRecords`; clinical roles and billing can read but not edit patients.
- **Concurrency/audit conventions to reuse:** row version on every patient-scoped write (`PatientWrite`: `ApplyExpectedVersion`, history + PHI-free audit in the same save); `ConcurrencyConflictBanner` for conflicts (see the contrast finding below).
- **Shared building blocks:** `PatientPicker` (find-and-choose a patient, keyboard operable), `PatientFieldsForm`, `DuplicateComparisonPanel`, `SafeLink`; `PatientDirectory.SearchAsync` for server-side search.

## Specific notes for `ALV-N010` (forms/consents/e-signature)
- A form belongs to a patient → add a tab (e.g. "Forms") through the extension point; the signed snapshot should reference `Patient.Id`.
- *Signer identity/relationship*: a signer may be the patient, or a household member / guarantor acting for them. The household relationship labels (`HouseholdRelationships.All`) and the guarantor link exist, but **a signer who is not a registered patient is not modelled** (the guarantor must be a registered patient); N010 must decide whether to add a free-text/external signer or extend the patient model.
- Patient identity fields (`name`, `dateOfBirth`) can change by safe edit; a signed snapshot must therefore capture the values shown at signing rather than reading the live patient later (history exists, but the snapshot is N010's job).
- The practice-requirements setting (`PatientRegistrationSettings`) is a pattern for N010's "required forms" readiness if it needs practice-level choices; keep such settings in their own table so completed stories are not modified.

## Specific notes for scheduling stories
- Appointments should reference `Patient.Id` and must tolerate an **inactive** patient (an inactivated patient keeps their history; decide whether new appointments are refused or warned). The guarantor and household-member pickers search active patients only; the search page offers an "include inactive" filter.
- Patient search (`GET /api/patients?q=…`) returns at most 50 rows and has no paging; a scheduler that needs more should extend `PatientDirectory`, not bypass it.

## Interfaces and migrations introduced
- Migration `AddPatientIdentityWorkspace` (new tables `Households`, `PatientHistory`, `PatientRegistrationSettings`; new columns on `Patients`). Later migrations must not re-create them.
- New permission `EditPatients` (enum member appended — existing ordinals undisturbed); new audit event types and the `patient.duplicate-check` measurement event (schema v1).
- API error codes listed in `R01.md` are a contract the forms depend on.

## Open items / unresolved decisions that touch later work
- **F1 (`ALV-N001` success toast, 4.45:1) and F2 (`ALV-002-C01` conflict banner title, 3.46:1)** are contrast defects in shared components of completed stories. Any later story that shows a success toast, or renders `ConcurrencyConflictBanner` outside the patient workspace, will fail an AA scan until those are corrected through the reopened-story protocol. Gate B should not be evaluated with these open (Gate B's own accessibility evidence will hit them).
- **Gate B rows B1 and B3** now have supporting evidence from this story (registration/household/guarantor workflow; persistent patient-context workspace with a patient-switch test) but remain `NOT YET EVALUABLE`: B1 also depends on the reviewed state of this story, B3's "later modules extend" can only be shown once another module uses the extension point.
- The responsible party who is not a patient, merge of confirmed duplicates, and patient search paging are explicitly out of scope here and have no scheduled story; raise them if the practice needs them.

## Prompt changes relevant to the next item
None required. `ALV-N010`'s dependency list (`ALV-003-C01`, `ALV-002-C01`, `ALV-N009`) is satisfied once this attempt is approved and closed.
