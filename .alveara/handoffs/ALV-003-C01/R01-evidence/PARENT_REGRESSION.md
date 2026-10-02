# ALV-003-C01 R01 — Parent regression (STORY-003)

`STORY-003` — "Implement patient registration workflow" — is `COMPLETE`, portal-verified at `014ef9d` (3 of 3 criteria; `.colaberry/progress.json`). This companion must keep its completion contract passing. This attempt did so **without touching any STORY-003 test**.

## Proof the parent's tests were not modified
`git diff --stat bacf6ed fdafb00 -- <the six files>` (parent baseline to implementation commit) is empty for:

- `src/Alveara.Api.Tests/PatientModelTests.cs` (14 tests)
- `src/Alveara.Api.Tests/PatientRegistrationServiceTests.cs` (24)
- `src/Alveara.Api.Tests/PatientsApiTests.cs` (7)
- `src/alveara-client/src/pages/PatientRegistrationPage.test.tsx` (9)
- `src/alveara-client/src/App.patientRoute.test.tsx` (2)
- `src/alveara-client/e2e/patient-registration-real-backend.spec.ts` (7)

(One accidental overwrite of `App.patientRoute.test.tsx` during the attempt was reverted from git before any commit; the new route tests live in a separate file, `App.patientWorkspaceRoutes.test.tsx`.) Of STORY-003's test and test-configuration files, the only one touched is `playwright.config.ts` (one added `testIgnore` entry so the new real-backend spec is excluded from the mocked run, exactly as STORY-003's own was); `playwright.patients.config.ts` is unchanged. STORY-003's *product* source files were extended, as described below.

## Results on the final code
| Parent suite | Result |
|---|---|
| Backend `PatientModelTests` + `PatientRegistrationServiceTests` + `PatientsApiTests` | **45 / 45** (inside the 492/492 full run) |
| Frontend `PatientRegistrationPage.test.tsx` + `App.patientRoute.test.tsx` | **11 / 11** (inside 319/319) |
| Real-backend walkthrough `patient-registration-real-backend.spec.ts` | **7 / 7** (`artifacts/playwright-real-backend/run-output-story003-original-walkthrough.txt`) |
| Mocked browser suite | 40 / 40 |

## STORY-003 Done-means, mapped
| STORY-003 acceptance | Still proven by (unchanged tests) |
|---|---|
| Given a new patient, when they provide their information, then the system captures demographics and contact details | `PatientRegistrationServiceTests.A_new_patient_is_captured_with_demographics_and_contact_details`; `PatientsApiTests.Front_desk_registers_a_patient_and_the_details_come_back_and_can_be_read_again`; `PatientRegistrationPage.test.tsx` "captures demographics and contact details…"; walkthrough ACCEPTANCE 1 |
| Given a patient with incomplete information, when they attempt to register, then the system prompts for required fields | `An_empty_registration_names_every_required_field_and_stores_nothing`, `One_missing_field_is_reported_alone`, `Incomplete_registration_returns_400_with_a_message_per_missing_field`, page test "prompts for every missing required field…", walkthrough ACCEPTANCE 2 |
| Trust: all registration actions are logged with user and timestamp | `Registration_writes_an_audit_entry_with_the_user_and_timestamp_and_no_patient_details`, `The_audit_log_attributes_the_registration_to_the_front_desk_user_with_a_timestamp`, walkthrough ACCEPTANCE 3 |
| Failure paths: duplicate entry, data validation failure, audit failure, concurrency | `Registering_the_same_person_again_is_refused…`, the malformed-value theory, `If_the_audit_write_fails_the_registration_is_not_stored_either`, `Eight_simultaneous_registrations_…`, `Simultaneous_retries_…` |

## What changed *around* the parent, and why it did not break it
- `PatientRegistrationService` now delegates field validation to the shared `PatientInput` (same rules, same messages) and keeps raising `PatientRegistrationException`; the likely-duplicate warning only fires for matches with the **same birth date**, so STORY-003's "same name, different birth date" cases (father/son) still register without acknowledgement — the first draft of the duplicate rules violated this and was corrected after the parent regression failed (recorded in `TEST_RESULTS.md`).
- `RegisterPatientRequest` gained one optional trailing member (`AcknowledgedDuplicateIds`), so every STORY-003 call site compiles unchanged. `PatientRegistrationService`'s constructor gained optional parameters, so `new PatientRegistrationService(db, clock)` still works.
- The registration form now renders its fields from the shared `PatientFieldsForm` with identical labels, markup roles and focus behavior; the registration page uses `SafeLink`, which degrades to a plain anchor with no router above it, so the parent's router-less page tests still render.
- The duplicate banner text and "Existing patient ID: …" line that STORY-003's tests and walkthrough assert are preserved; the comparison panel is added beside them.
- Registration's own permission (`RegisterPatients`) and the matrix grants for it are unchanged. `GET /api/patients/{id}` now requires `ViewPatientRecords` (front desk holds it); STORY-003's tests read as the front desk.
