# ALV-003-C01 R01 — Acceptance evidence

Every item of the prompt's "Acceptance / stop condition", then the required tests, then the implementation-scope items. Paths are under `src/`. "Browser" = `alveara-client/e2e/patient-workspace-real-backend.spec.ts` (real Chromium → real API → real LocalDB), results in `artifacts/playwright-real-backend/`.

## Acceptance (7)

### 1. Original STORY-003 tests still pass
All six STORY-003 test files are unchanged from the parent commit and pass: backend `PatientModelTests` 14, `PatientRegistrationServiceTests` 24, `PatientsApiTests` 7; frontend `PatientRegistrationPage.test.tsx` 9, `App.patientRoute.test.tsx` 2; real-backend `patient-registration-real-backend.spec.ts` 7 of 7. See `PARENT_REGRESSION.md` (includes the `git diff` proof of no modification).

### 2. Household and guarantor can be represented independently
- Model: `Alveara.Api/Architecture/Patients/Patient.cs` — `HouseholdId`/`HouseholdRelationship` and `GuarantorPatientId` are separate columns with separate foreign keys.
- Backend: `PatientRelationshipTests.Household_and_guarantor_are_independent_a_guarantor_outside_the_household_and_members_with_different_guarantors` (Cal in Mia's household, Gus — outside it — is Cal's guarantor, Tia in the same household has Mia as guarantor, Mia self-responsible); `A_household_can_exist_with_no_guarantor_links_and_a_guarantor_link_with_no_household`.
- API: `PatientIdentityApiTests.Front_desk_registers_searches_opens_edits_links_and_reads_history_end_to_end`.
- UI: `PatientHousehold.test.tsx` "shows household and guarantor as independent…".
- Browser: "HOUSEHOLD AND GUARANTOR are independent" — read back from the real API: Cal's household members are `Cal Lee, Mia Lee` (not Gus); Cal's guarantor is Gus; Gus has no household and `guaranteeFor = [Cal]`; Mia has no guarantor. Screenshot `14-household-guarantor.png`.

### 3. Likely duplicate is warned without silent merge
- Backend: `PatientDuplicateWarningTests` — each likely rule warns with its reason and registers nothing (`A_likely_duplicate_is_warned_with_the_reason_and_nothing_is_registered`, 4 cases); registering after acknowledgement leaves the existing record's version untouched (`Acknowledging_every_candidate_registers_the_new_patient_and_leaves_the_existing_one_untouched`); a partial/stale acknowledgement is not enough; an exact match is refused even when acknowledged; relatives sharing a surname/phone/email are not warned; the early check registers nothing; simultaneous twin registrations never create an exact duplicate; outcomes recorded as privacy-safe measurement events.
- API: `PatientIdentityApiTests.A_likely_duplicate_returns_409_with_candidates_and_registers_only_once_acknowledged`.
- UI: `PatientRegistrationDuplicates.test.tsx` — comparison panel (name, birth date, phone, email, place, status, reason, link to open the existing patient), Register anyway resubmits with the acknowledged ids and the same idempotency key, exact match has no Register-anyway control, editing a field closes a stale comparison; axe-clean.
- Browser: "DUPLICATE WARNING…" — Anna Park (same birth date and surname as Ann Park) → comparison shown, nothing registered; after Register anyway the existing Ann Park record is unchanged (read back); an exact `ann PARK` is blocked with the existing patient's id. Screenshot `10-duplicate-comparison.png`.
- No merge operation exists in the API or UI.

### 4. Active/inactive state is preserved
- Backend: `PatientEditTests.Inactivating_a_patient_preserves_the_record_and_it_stays_inactive_across_later_edits`, `Reactivating_restores_the_patient_and_setting_the_same_state_again_is_a_no_op`, `A_stale_status_change_is_a_conflict_and_a_guarantor_of_active_patients_cannot_be_inactivated`; `PatientDirectoryTests.Inactive_patients_are_hidden_from_search_unless_asked_for_and_are_never_lost`; inactive patients still appear (flagged) as duplicate candidates.
- UI: `PatientWorkspace.test.tsx` "inactivating asks first, keeps the record, and the header then shows Inactive…", read-only without `EditPatients`.
- Browser: "ACTIVE/INACTIVE" — inactivated, an edit does not reactivate, hidden from default search, shown with the filter, reactivated. Screenshot `15-inactive-search.png`.

### 5. Concurrent edit cannot silently overwrite another user's change
- Backend: `PatientEditTests.A_stale_edit_is_rejected_and_never_overwrites_the_other_users_change` (and no history row for the rejected edit), `Two_simultaneous_edits_from_the_same_version_let_exactly_one_win` (6 simultaneous: 1 saved, 5 conflicts); `PatientRelationshipTests.A_stale_relationship_change_is_a_conflict`; settings: `PatientRequirementsTests.Saving_unchanged_settings_is_a_no_op_and_a_stale_save_is_a_conflict`.
- API: `PatientIdentityApiTests.A_stale_edit_returns_the_shared_409_concurrency_shape_and_keeps_the_other_users_change`; every change needs CSRF and a row version.
- UI: `PatientWorkspace.test.tsx` "a conflicting edit by someone else is never overwritten…" (banner, nothing overwritten, Reload brings the current version); `PatientHousehold.test.tsx` "a stale change shows the shared conflict message".
- Browser: "CONCURRENCY" — a second front-desk user saves Houston; the first user's stale save of Waco is refused with the banner; the API still holds Houston; Reload shows Houston. Screenshot `13-conflict.png`.

### 6. Registration UI is usable keyboard-first
- Browser: "KEYBOARD-FIRST" — a patient registered by typing and Tab/Enter only (including the date field's extra tab stop), then found with the search box focused on arrival and opened with Tab/Enter; STORY-003's keyboard-only registration (7/7 spec) also still passes.
- UI: the patient picker is operable by keyboard (type, Tab to a result, Enter — `PatientHousehold.test.tsx`); required-field errors move focus to the first invalid field (registration and details); labels and `aria-required` on every required field; the comparison panel is a labelled region with a real table (axe-clean).
- Browser axe: 18 scans (register form, comparison panel, search, history, conflict banner, household/guarantor, details, not-found, settings; light and dark) — **0 critical/serious**.

### 7. Switching between two patients cannot leave stale identity or patient-scoped data visible in the shared workspace
- Unit: `contexts/PatientContext.test.tsx` — a select drops the previous patient immediately ("loading:b" in the same render), an older request is aborted and its late answer ignored, clearing during a load means the late answer is never shown, StrictMode mount/unmount/remount still loads (regression test verified to fail against the old code).
- UI: `PatientWorkspace.test.tsx` "switching patients never leaves the previous patient's identity or data visible while the next one loads" (header and page show nothing of Ann while Ben loads) and "discards a slow response for a patient who is no longer the one selected".
- Browser: "PATIENT SWITCH" — Cal's record delayed 1.5 s; while it loads the header reads "Loading patient…" and neither the header nor `<main>` contains "Mia Lee" or her phone; after loading only Cal. Screenshot `16-switch-loading.png`.

## Required tests (from the prompt)
| Required | Evidence |
|---|---|
| Original regression | `PARENT_REGRESSION.md` |
| Household/guarantor tests | `PatientRelationshipTests` (12), `PatientHousehold.test.tsx` (13), API + browser above |
| Duplicate warning tests | `PatientDuplicateWarningTests` (12), `PatientRegistrationDuplicates.test.tsx` (6), API + browser above |
| Concurrency test | item 5 above (backend, API, UI, browser) |
| Patient-context switch/isolation test | item 7 above (unit, UI, browser) |

## Implementation scope
| Scope item | Where proven |
|---|---|
| Family/household relationships | `PatientRelationshipTests`, UI + browser above |
| Separate guarantor/responsible party | same; rules incl. no chains/cycles in `Invalid_guarantors_are_refused_with_invalid_relationship` |
| Active/inactive patient state | item 4 |
| Duplicate candidate warning before create | item 3 |
| Safe edits with history/concurrency | `PatientEditTests` (14), `PatientDirectoryTests.History_lists_edits_newest_first…`, browser edit + History tab (`12-history.png`) |
| Patient search and identity summary | `PatientDirectoryTests` (7: prefix words in any order, birth date, phone in any format, inactive filter, 50-row cap, wildcards/injection treated as data, age on the practice calendar date); UI `PatientWorkspace.test.tsx` search tests; browser "SEARCH" (`11-search.png`) |
| Persistent patient-context workspace/header/navigation extension point | `app/PatientHeader.tsx`, `contexts/PatientContext.tsx`, `app/patientWorkspaceTabs.ts`, `docs/PATIENT_WORKSPACE.md`; tests: header states, "only tabs that exist" (`App.patientWorkspaceRoutes.test.tsx`), browser header across tabs |
| Switching clears/reloads patient-scoped state | item 7 |
| Validation appropriate to configurable practice requirements | `PatientRequirementsTests` (12), `PatientRequirements.test.tsx` (9), browser "CONFIGURABLE REQUIREMENTS" (`17-registration-settings.png`, `18-email-required-prompt.png`) |

## Security, audit and data-integrity requirements (from the prompt)
- *Registration/edit permission checks* — `PatientIdentityApiTests.Clinical_and_billing_roles_may_view_patients_but_not_register_or_change_them` (Dentist, Hygienist, Assistant, Billing: 200 on reads, 403 on every write; 401 anonymous; nothing stored), the permission-matrix theory (8 roles), `PatientRequirementsTests.Everyone_who_can_see_patients_can_read_the_settings_but_only_practice_managers_can_change_them`; browser "PERMISSIONS" (dentist: no register/edit controls, API write 403).
- *Audit create/update/relationship changes* — audit events listed in `R01.md`; `PatientEditTests` (PHI-free: audit names fields, never values), `PatientRelationshipTests.Relationship_audit_entries_name_no_one`, `PatientRequirementsTests.Settings_round_trip…`; browser "AUDIT": 6 registrations, updates, status, household, guarantor and settings changes all present with user and timestamp and none containing patient names, phones, emails or cities.

## Failure paths (from the prompt)
| Failure path | Evidence |
|---|---|
| Likely duplicate | item 3 |
| Conflicting concurrent edit | item 5 |
| Invalid relationship | `Invalid_guarantors_are_refused…`, `Invalid_household_requests_are_refused`, API `Invalid_relationships_come_back_as_400…`, UI server-message test, browser "INVALID RELATIONSHIPS" |
| Missing required data | STORY-003's rules unchanged + practice requirements; edit prompts (`An_edit_that_breaks_a_required_field_names_it_and_changes_nothing`, UI "prompts for a required field that was cleared and sends nothing", browser email-required prompt, server refusal of a direct API call) |

## Measurement
`patient.duplicate-check` events (privacy-safe convention; see `PatientDuplicateWarningTests.Duplicate_detection_outcomes_are_recorded_as_privacy_safe_measurement_events`).
