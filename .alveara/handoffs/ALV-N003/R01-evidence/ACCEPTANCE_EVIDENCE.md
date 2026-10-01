# ALV-N003 R01 — Acceptance Evidence

## Acceptance / stop condition

| # | Acceptance item | Proving evidence |
|---|---|---|
| 1 | Provider, operatory and appointment type/duration can be configured and immediately consumed by scheduling | `SchedulingConfigurationTests.Configured_provider_operatory_and_appointment_type_are_immediately_consumable_by_scheduling` (snapshot empty → populated the instant configuration is written, incl. 60-minute default duration and the provider's weekly window); `ConfigurationApiTests.End_to_end_configuration_is_audited_and_consumed_by_the_scheduling_read_model` (same over HTTP + availability check); `ConfigurationApiTests.The_scheduling_read_model_needs_only_ViewSchedule…`; real-browser Playwright test 8 (scheduling preview shows the configured provider, hours, operatory, and "E2E Exam - 45 min"); `ConfigurationHubPage.test.tsx` preview tests |
| 2 | Provider availability and blocked time are persisted in practice-local time semantics | `StaffProviderServiceTests.Weekly_availability_is_persisted_as_practice_local_wall_clock_times` (stored `TimeOnly` re-read in a fresh context); `…Blocked_time_is_entered_in_practice_local_time_and_stored_as_the_correct_utc_instant` (CST 09:00 → 15:00Z, CDT 09:00 → 14:00Z, round-trips to 09:00 local); `…Blocked_time_in_a_DST_gap_or_overlap_is_rejected_not_guessed` (2026-03-08 02:30 gap, 2026-11-01 01:30 overlap); `…The_same_weekly_window_means_the_same_wall_clock_hours_in_winter_and_summer`; `SchedulingConfigurationTests.Availability_check_honors_working_hours_blocked_time_and_slot_boundaries`; UI: DST-gap server message surfaced in `ConfigurationHubPage.test.tsx` |
| 3 | User accounts, staff profiles and provider profiles remain distinct but linkable | `StaffProviderServiceTests.Account_staff_and_provider_are_distinct_records_that_link_explicitly_and_optionally` (three distinct ids; staff without a login; provider → staff link); `…Linking_a_staff_profile_to_a_missing_disabled_or_already_linked_account_is_rejected` (failure path: link to inactive/missing account; nothing partial persisted); `…A_link_to_an_account_that_is_later_disabled_keeps_resolving_and_is_not_silently_severed`; `…Changing_the_account_link_is_audited_as_its_own_event`; `…The_link_picker_offers_enabled_accounts_plus_already_linked_ones_and_says_who_holds_them`; provider needs active staff / one per staff |
| 4 | Inactive referenced configuration remains historically resolvable | `StaffProviderServiceTests.An_inactivated_provider_still_resolves_historically_with_its_staff_name_and_availability`; `PracticeConfigurationServiceTests.An_inactivated_operatory_is_hidden_from_default_lists_but_still_resolvable_and_reactivatable` and `A_location_with_active_operatories_cannot_be_inactivated_and_an_inactive_one_is_still_resolvable`; `SchedulingConfigurationTests.Inactive_configuration_is_no_longer_offered_to_scheduling_but_remains_resolvable`; destructive delete is impossible: `…Referenced_configuration_cannot_be_destructively_deleted_at_the_database`, `…A_provider_with_availability_cannot_be_hard_deleted`, `ConfigurationApiTests.There_is_no_destructive_delete_route…`; real-browser: inactivated operatory leaves the preview but shows under "Show inactive" |
| 5 | Material changes are audited | `PracticeConfigurationServiceTests.Appointment_types_have_unique_names_and_the_duration_change_is_audited` (entity type, actor, "from 30 to 45"); `…A_configuration_change_and_its_audit_entry_commit_or_fail_together` (stale edit rolls back both); `StaffProviderServiceTests` link-change and blocked-time add/remove events; `ConfigurationApiTests.End_to_end…` (`ConfigurationCreated` for all six entity types + `ProviderAvailabilityReplaced` readable through the audit-log API); real-browser test 8 finds the `Operatory`/`ConfigurationCreated`, `ConfigurationInactivated` and `ProviderAvailabilityReplaced` rows in the audit viewer |

## Required tests (per the prompt)

| Required test | Where |
|---|---|
| Configuration CRUD/validation tests | `PracticeConfigurationServiceTests`, `StaffProviderServiceTests`, `ConfigurationApiTests` (validation codes), `ConfigEntityPanel.test.tsx` |
| Account↔staff/provider linkage tests | `StaffProviderServiceTests` (5 tests above) |
| Referenced-record inactivation tests | `PracticeConfigurationServiceTests`, `StaffProviderServiceTests`, `SchedulingConfigurationTests` |
| Timezone/availability tests | `StaffProviderServiceTests` (blocked time, DST, winter/summer), `SchedulingConfigurationTests` |
| Scheduling-consumer integration smoke test | `SchedulingConfigurationTests`, `ConfigurationApiTests.End_to_end…`, Playwright test 8 |

## Failure paths named by the prompt

| Failure path | Handling / evidence |
|---|---|
| Duplicate/conflicting staff/provider/operatory identifiers where prohibited | 409 `staff_name_taken`, `provider_exists`, `operatory_name_taken`, `appointment_type_name_taken`, `location_name_taken`, `account_already_linked`; database unique indexes backstop a race (`duplicate_configuration`); tests above |
| Invalid appointment duration/availability ranges | `invalid_duration`, `invalid_range`, `invalid_time`, `availability_overlap`; theory tests; UI blocks before calling the server |
| Attempt to destructively delete referenced configuration | no DELETE route (405), FK `Restrict` (DB refuses), inactivation-order rules (409) |
| User-account link points to inactive/missing account | `account_inactive` / `account_not_found` (400) |

## UI/UX deliverables

| Deliverable | Evidence |
|---|---|
| Admin Configuration hub for practice/staff/providers/location/operatories/appointment types/availability | `ConfigurationHubPage` (7 tabs), `DEMO_EVIDENCE.md`, Playwright test 8 |
| Search/filter appropriate to the small v1 scope | search box + "Show inactive" in `ConfigEntityPanel`; tested |
| Inline validation and unsaved-change protection | `validateFields`/`validateWindows`; prompt on cancel/row/tab switch + `beforeunload`; tested |
| Preview scheduling-relevant configuration | Scheduling preview tab (same read model scheduling consumes) |
| Loading/empty/error/permission states; accessibility | all four states per panel; axe: no violations on three representative screens |
