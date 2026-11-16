# ALV-N021 A2-R01: acceptance evidence

Sources: briefing `docs/design/ALV-N021_PRE_IMPLEMENTATION_BRIEF.md` revision 4 (approved, `BRIEFING-R04.md`), owner decision record `docs/design/ALV-N021_OWNER_DECISION_RECORD.md` (revision 2, signed), decision 02 (`.alveara/reviews/ALV-N021/A2-DECISION-02.md`: T-2 and T-3 in A2), and the owner decisions taken during A2 (section 5). Test names are in `src/Alveara.Api.Tests` unless a client path is given.

## 1. Owner decisions OD-01 to OD-32 (A2) and the test that proves each

OD-33 to OD-36 are separate proposals (not built); OD-37 (PD-06) stays escalated.

| Id | Decision (short) | Proving tests | Commit(s) |
|---|---|---|---|
| OD-01 | one path Scheduled -> ... -> Completed; anything else 409 `illegal_transition` | `VisitPathTableTests.Every_pair_of_states_matches_the_table_under_each_setting_combination`; `VisitStateMachineTests` | `eaaf2f5`, `215436c` |
| OD-02 | no shortcuts, on every route incl. legacy | `VisitPathTableTests.STORY_011s_shortcuts_and_other_skips_are_refused_whatever_the_settings`; `PatientFlowApiTests` legacy routes; flow walkthrough NO SHORTCUTS | `eaaf2f5` |
| OD-03 | Confirm is a setting, default required; a change needs ManagePracticeConfiguration, is audited and applies to the next move only | `VisitMovePolicyTests.With_both_settings_required_...` and `..._off_...`; `VisitWorkflowSettingsTests` (defaults + audit old/new/actor/time, no-op, stale, incomplete, no practice, next move only); `ConfigurationApiTests.The_visit_workflow_settings_are_read_with_the_practice_and_changed_only_with_ManagePracticeConfiguration_and_a_csrf_token`; `ConfigurationHubPage.test.tsx` (2) | `ce6e9da`, `eaaf2f5`, `7207b88` |
| OD-04 | Mark ready is a setting, default required | as OD-03; `VisitActionsTests.With_both_settings_off_check_in_is_offered_from_scheduled_and_seat_from_checked_in` | as OD-03 |
| OD-05 | Seat always required | `VisitPathTableTests`; `VisitStateMachineTests.The_chairside_steps_cannot_be_skipped_once_a_patient_is_in_them` | `eaaf2f5` |
| OD-06 | front-office moves for UpdateVisitFlow holders only | `VisitMovePolicyTests.Front_office_moves_are_refused_to_other_roles`; `VisitWorkflowPermissionTests` | `eaaf2f5` |
| OD-07 | chairside holders; OfficeManager loses it by default | `VisitMovePolicyTests.Chairside_moves_are_refused_to_other_roles`; `VisitWorkflowPermissionTests.The_office_manager_keeps_front_office_moves_but_loses_the_chairside_default` | `ce6e9da` |
| OD-08 | only the booked provider starts and ends treatment, Admin bound too | `VisitMovePolicyTests.Only_the_booked_provider_starts_and_ends_treatment_and_an_unlinked_administrator_is_bound_too` | `eaaf2f5` |
| OD-09 | completion by the front desk or the clinician who started treatment (CompleteOwnStartedVisit, Dentist/Hygienist only) | `VisitMovePolicyTests` (dentist who started, granted hygienist, dentist who did not start or is unlinked, assistant with the grant forced); `VisitWorkflowPermissionTests` (grant limits, invalid stored grant, delegation invalid) | `ce6e9da`, `eaaf2f5` |
| OD-10 | "Ready for checkout" wording and next-actor text | `VisitActionsTests` (next-actor text per state); board walkthrough PARITY | `022982c`, `8969d15` |
| OD-11 | undo check-in with a reason, before seating | `UndoCheckInTests` (10) | `df82656` |
| OD-12 | check-in only on the practice day; minutes late logged | `VisitMovePolicyTests.Check_in_is_allowed_only_on_the_appointments_practice_day`, `An_evening_check_in_is_the_practice_day_although_utc_is_already_tomorrow_and_lateness_is_recorded`; `CheckInDstDayRuleTests` (both 2030 DST days) | `eaaf2f5`, `8969d15` |
| OD-13 | ReassignAppointmentProvider; the booking changes; "Seen by" retired | `ProviderReassignmentTests` (17) | `df82656` |
| OD-14 | reassignment through the booking guards; reason required | `ProviderReassignmentTests.The_booking_guards_apply_a_busy_provider_a_busy_room_and_a_blocked_provider_are_each_refused`, `A_provider_change_without_a_reason_is_refused_and_nothing_is_written` | `df82656` |
| OD-15 | two Urgent notifications, none on retry (M2) | `ProviderReassignmentTests.A_provider_change_moves_the_booking_records_who_why_and_from_where_and_notifies_both_providers_once`, `Asking_again_for_what_the_appointment_already_has_writes_nothing_and_sends_nothing_new` | `ce6e9da`, `df82656` |
| OD-16 | room-only change for reassignment and chairside holders | `ProviderReassignmentTests.A_room_only_change_...` (2); `VisitOccupancyTests` | `df82656` |
| OD-17 | blocked time: confirm + reason over future bookings; refused over active visits | `BlockedTimeOverlapTests`; `VisitsApiTests`; `ConfigurationHubPage.test.tsx` | `4f73a81`, `6c3fcef` |
| OD-18 | four resolutions; check-in refused while flagged | `BlockedTimeOverlapTests`; `VisitActionsTests` | `4f73a81` |
| OD-19 | cancel category (M1), short notice, report | `CancellationCategoryTests`; `AppointmentLifecycleTests.Cancelling_needs_a_category_keeps_the_appointment_on_record_frees_the_time_and_is_audited_without_the_note_text`; `CancelCategoryClientParityTests` | `ce6e9da`, `df82656` |
| OD-20 | no-show final; Rebook offered | `VisitWorkflowSettingsTests.The_only_no_show_route_marks_one_there_is_no_route_that_corrects_or_undoes_a_no_show`; `VisitActionsTests` (rebook); `Calendar.test.tsx` | `b713478`, `7207b88` |
| OD-21 | blank duration prefilled from the type, editable | `pages/calendar/bookingDuration.test.ts`; `Schedule.test.tsx`; `Calendar.test.tsx` | `41844aa` |
| OD-22 | completion needs every checkout item; 409 `checkout_incomplete`, nothing written | `VisitMovePolicyTests.No_completion_route_completes_a_visit_with_an_open_checkout_item_and_nothing_is_written` | `b16a2f5`, `eaaf2f5` |
| OD-23 | the four items | `CheckoutTests` (15) | `b16a2f5` |
| OD-24 | follow-up outcomes; "booked" detected, never by hand | `CheckoutTests.A_real_future_appointment_books_the_follow_up_and_booked_cannot_be_recorded_by_hand`, `Only_the_treating_provider_records_that_no_follow_up_is_clinically_required` | `b16a2f5` |
| OD-25 | financial applicability | `CheckoutTests.A_balance_owed_makes_the_financial_item_apply_and_a_balance_paid_in_full_does_not`, `A_completed_procedure_of_this_visit_with_no_live_charge_makes_the_financial_item_apply`, `With_nothing_owed_...` (a credit balance cannot arise: the ledger refuses it, so the credit clause is tested as paid in full) | `b16a2f5` |
| OD-26 | financial outcomes verified against real ledger rows; checkout writes no ledger row | `CheckoutTests.Payment_recorded_resolves_only_with_a_live_payment_of_the_visit_day_and_reopens_if_it_is_reversed`, `Waived_needs_a_negative_adjustment_of_the_visit_day`, `Waiver_requested_needs_a_reason_and_never_resolves_the_financial_item`, `Recording_departure_and_a_follow_up_decision_makes_the_visit_ready_and_never_touches_the_ledger` | `b16a2f5` |
| OD-27 | CollectPayments (FrontDesk): payment and apply only | `CollectPaymentsApiTests` (403 for each other billing write); `BillingLedgerPanel.test.tsx` | `19b6f07` |
| OD-28 | financial decisions by UpdateVisitFlow or ManageBilling holders | `CheckoutTests.A_role_without_the_duty_is_refused_and_nothing_is_written` | `b16a2f5` |
| OD-29 | clinician completion also needs every item | `VisitMovePolicyTests.No_completion_route_...` (clinician route included) | `eaaf2f5` |
| OD-30 | visits already in Ready for checkout are gated too | `VisitWorkflowSettingsTests.A_visit_left_in_ready_for_checkout_before_A2_is_not_grandfathered_and_cannot_complete_until_resolved` | `7207b88` |
| OD-31 | outcomes append-only (M3) | `CheckoutTests.A_correction_is_a_new_row_that_supersedes_the_old_one_with_history_and_audit`; `VisitCheckoutMigrationTests` | `ce6e9da`, `b16a2f5` |
| OD-32 | one checkout panel; Complete enabled only when every item is resolved | `Checkout.test.tsx`; board walkthrough WHOLE PATH and flow walkthrough COMPLETE (axe, both themes); board PARITY | `447eb3f`, `8969d15` |

## 2. Decision 02 (T-2, T-3)

| Item | Proof | Commit |
|---|---|---|
| T-2 explicit invariant culture at every fixed-format date/time | `TimeCultureGuardTests` (source scan names any unmarked site) | `f82c5ca` |
| T-3 one practice-day boundary rule on the clock | `PracticeDayBoundaryTests`; `VisitBoardTests` | `f82c5ca` |

## 3. Briefing section 1, the A2 intended results

| Item | Status | Where proven |
|---|---|---|
| 2 one workflow; identical role-correct actions on Calendar and Board; next actor; no shortcuts or dead ends | delivered | OD-01..OD-10; `VisitActionsTests`; board PARITY |
| 3 check-in only on the practice day; no completion before it | delivered | OD-12; F-047, F-071 |
| 4 undo check-in, cancel category, final no-show with Rebook | delivered | OD-11, OD-19, OD-20 |
| 5 reassignment, notifications; blocked time cannot silently cover appointments | delivered | OD-13..OD-18 |
| 6 history from/to by whom; Week view, empty states, wording (Group C) | delivered | section 4, Group C |
| 7 checkout gate; CollectPayments | delivered | OD-22..OD-32 |

## 4. Finding classification (briefing section 4, A2 list)

Statuses as the briefing defines them. Owner rulings of 2026-11-16 are in Q-5q and F-071.

### Group A - one workflow, roles, Calendar/Board parity

| Id | Raw problem | Status | Fix / decision | Proving test(s) |
|---|---|---|---|---|
| F-056 | Start and Complete both offered after check-in | DESIGN_DECISION_REQUIRED -> answered OD-01/02, fixed | one path, shortcuts refused on every route | VisitPathTableTests.STORY_011s_shortcuts_and_other_skips_are_refused_whatever_the_settings; flow walkthrough NO SHORTCUTS |
| F-058 | Calendar and Board offer different actions | CONFIRMED, fixed | VisitActionsService is the one source for both | VisitActionsTests (5.4 table); board walkthrough PARITY |
| F-059 | Front desk can start/complete treatment | CONFIRMED, fixed (OD-06/08) | clinical moves need chairside / booked provider | VisitMovePolicyTests.Front_office_moves_..., Only_the_booked_provider_starts_and_ends_treatment_... |
| F-060 | Board "Change room or provider" leaves calendar booking behind | CONFIRMED, fixed (OD-13) | reassignment changes the booking; "Seen by" retired | ProviderReassignmentTests.A_provider_change_moves_the_booking_..., An_old_seen_by_override_is_kept_until_... |
| F-063 | Hygienist completed a visit from Checked in | CONFIRMED, fixed (OD-02/09/22) | no shortcut; completion needs checkout + rights | VisitPathTableTests; VisitMovePolicyTests.A_hygienist_who_started_treatment_may_complete_only_when_granted |
| F-065 | Role split dead end (no one can check out) | CONFIRMED, fixed (OD-09/10) | front desk or treating clinician completes; next-actor text | VisitMovePolicyTests.The_dentist_who_started_treatment_may_complete; board walkthrough WHOLE PATH |
| F-066 | Stale "now in treatment" banner in other session | CONFIRMED, fixed | live board refresh clears stale message | board walkthrough LIVE and CONFLICT |
| Q-065a | Hygienist can start/complete Dr. Dana's visit | answered OD-08 | only the booked provider starts/ends treatment | VisitMovePolicyTests.Only_the_booked_provider_... (also Admin bound) |
| F-057 | No undo check-in | DESIGN_DECISION_REQUIRED -> OD-11, built | Undo check-in with reason, before seating | UndoCheckInTests (10) |
| F-073 | No-show final, no rebook | DESIGN_DECISION_REQUIRED -> OD-20 | no correction (route scan); Rebook + Open patient offered | VisitWorkflowSettingsTests.The_only_no_show_route_...; VisitActionsTests rebook; Calendar.test Rebook |

### Group B - integrity

| Id | Raw problem | Status | Fix / decision | Proving test(s) |
|---|---|---|---|---|
| F-047 | Check-in offered 4 days early | CONFIRMED, fixed (OD-12) | check-in only on the practice-local day | VisitMovePolicyTests.Check_in_is_allowed_only_on_..., CheckInDstDayRuleTests |
| F-053 | Block added over a checked-in visit, no warning | CONFIRMED, fixed (OD-17/18) | active visit -> 409 refused; future -> confirm + flag | BlockedTimeOverlapTests; VisitsApiTests |
| F-061 | Board move ignored training block / busy room | CONFIRMED, fixed (OD-14) | reassignment through booking guards | ProviderReassignmentTests.The_booking_guards_apply_... |
| F-071 | Completed 3 days early; completed visit holds slot | Part 1 early completion: CONFIRMED defect, fixed by the practice-day rule (OD-12) and the checkout gate (OD-22). Part 2 slot retention: CURRENT DESIGN DECISION (owner 2026-11-16) | a completed visit keeps its original scheduled time as the record of what was booked and performed; never silently freed or rewritten. Freeing unused time after an early finish is NOT in A2; if wanted later, a separate operational-capacity proposal (actual-end-time model, conflict rules, rendering of a completed event overlapping a later booking, audit, clinical/operational approval) | VisitMovePolicyTests day rule; flow walkthrough COMPLETE ("a finished visit still holds its time") |

### Group C - screens and wording

| Id | Raw problem | Status | Fix | Proving test(s) |
|---|---|---|---|---|
| F-044 (raw) | duration not prefilled | WITHDRAWN (owner typed it) | row kept | - |
| F-044b | blank duration should show type length | DESIGN_DECISION_REQUIRED -> OD-21, built (6e) | prefilled, editable | bookingDuration.test.ts; Schedule.test; Calendar.test |
| F-045 | no Open patient on appointment panel | CONFIRMED, fixed (6c) | Open patient link | Calendar.test; calendar walkthrough |
| F-046 | history lacks who | CONFIRMED, fixed (6b) | "By" column | AppointmentMoveHistoryTests; AppointmentHistory.test |
| F-048 | non-working day fully shaded, no message | CONFIRMED, fixed (6d-1a) | notice + Next working day | Calendar.test (nextWorkingDay) |
| F-049 | reschedule history only old slot | CONFIRMED, fixed (6b, M4) | "From X to Y, by user" | AppointmentMoveHistoryTests (9), AppointmentMoveDestinationMigrationTests (15) |
| F-050 | cancel reason free text | DESIGN_DECISION_REQUIRED -> OD-19, built (M1) | category + short notice + report | CancellationCategoryTests; AppointmentLifecycleTests.Cancelling_needs_a_category_... |
| F-051 | cancelled panel: no Rebook / Open patient | CONFIRMED, fixed (6c) | Next steps group | VisitActionsTests; Calendar.test |
| F-052 | week view no shading / no labelled blocked time | CONFIRMED, fixed (6d-1a, 6d-1b, M5) | shading + safe labels, never the raw reason | CalendarBlockedTimeApiTests (23), BlockedTimeCalendarLabelMigrationTests; calendar walkthrough |
| F-054 | narrow week blocks unreadable | CONFIRMED, fixed (6d-1a) | narrow layout | Calendar.test; calendar walkthrough screenshots |
| F-055 | two status chips, raw history wording | CONFIRMED, fixed (6b) | one chip, human wording | AppointmentHistory.test; Calendar.test |
| F-062 | "Seen by" row disagrees with booking | CONFIRMED, fixed with F-060 | Seen by retired | ProviderReassignmentTests.An_old_seen_by_override_... |
| F-064 | Component Showcase in staff menus | CONFIRMED, fixed (6d-3) | development builds only | moduleRegistry.test.ts |
| F-068 | no time picker in Firefox | NOT_REPRODUCED (Firefox-only); hint added (6d-3) | TIME_HINT on start-time fields | Schedule.test / Calendar.test hint assertions |
| F-069 | schedule list: no status, no links | CONFIRMED, fixed (6d-2) | Status column, appointment + patient links | Schedule.test; schedule walkthrough (axe both themes) |
| F-070 | "Appointment confirmed." on booking | CONFIRMED, fixed (6d-2) | "Appointment booked." | Schedule.test; Calendar.test |
| F-072 | same-patient refusal lacks detail | CONFIRMED, fixed (6d-2) | one refusal format | explainRefusal tests; Schedule.test; chain 3.10 |
| F-074 | repeated event name, no user | CONFIRMED, fixed (6b) | names suppressed, By column | AppointmentHistory.test |
| F-076 | empty filtered week, no message | CONFIRMED, fixed (6d-1a) | "No appointments for this selection." | Calendar.test |
| F-077 | uneven week header | CONFIRMED, fixed (6d-1a) | two-line headers | Calendar.test; screenshot |

### Withdrawn, kept with a test

| Id | Status | Proving test |
|---|---|---|
| F-170 | WITHDRAWN (delegation by design) | PermissionPolicyWriteTests.The_office_manager_grants_and_removes_document_import_for_a_working_role_and_the_history_says_it_was_delegated; ...The_office_manager_cannot_go_beyond_the_closed_delegation_table |
| D-01a | WITHDRAWN (front desk reads catalog, cannot change) | ProcedureCatalogApiTests.Whoever_can_see_billing_can_read_the_catalog (FrontDesk true); Only_billing_the_practice_manager_and_the_administrator_can_change_the_catalog (FrontDesk false) |

### Questions checked against the Permission Matrix (no grant change)

| Id | Question | Matrix answer | Record | Test |
|---|---|---|---|---|
| Q-5a | dentist sees Billing and Forms | Dentist holds ViewBilling, CompleteForms, ViewSignedForms | by design | PermissionMatrixTests.Dentist_can_manage_clinical_notes_but_not_security_administration (+ matrix default) |
| Q-5q | front desk sees no safety banner | FrontDesk lacks ViewSafetyIndicator (unchanged in A2) | DEFERRED_TO_ALV-N022 (owner 2026-11-16), not by design: ALV-N022 decides whether the front desk gets a minimal non-clinical safety-presence indicator (e.g. "Clinical safety alert — notify the treating team") while allergy names, alert narrative, diagnoses and other clinical details stay withheld unless separately authorized. The current denial test stays as proof of current behaviour | PermissionMatrixTests.FrontDesk_can_manage_appointments_but_not_clinical_or_security_permissions |
| Q-5au | billing opens History/Forms/Documents | Billing holds ViewPatientRecords, ViewSignedForms, ViewDocuments; no clinical permissions | by design | PermissionMatrixTests.Billing_can_manage_billing_but_not_clinical_notes; BillingChargeSourceApiTests.A_billing_only_reader_..._cannot_read_the_clinical_record |
| Q-5be | front desk sees Billing tab | FrontDesk holds ViewBilling (+ CollectPayments, OD-27) | by design | CollectPaymentsApiTests |
| Q-5bh | Permission Matrix in manager menu | OfficeManager holds ViewPermissionMatrix, not ManagePermissionGrants (only the closed delegation table) | by design | PermissionMatrixTests.OfficeManager_can_view_the_audit_log_and_permission_matrix_but_cannot_manage_security_administration |
| Q-5ar | hygienist prints a prescription | print route needs ViewPrescriptions, which Hygienist holds | recorded with PD-06, stays escalated (OD-37) | none in A2 (out of scope) |

## 5. Owner decisions taken during A2 (each recorded before the work it governs)

| Date | Decision |
|---|---|
| 2026-11-14 | walkthrough runner `-InjectedClockUtc` option (shared test infrastructure) |
| 2026-11-15 | test-only chain-host clock-advance route `POST /api/chain/clock/advance` for chain case 3.09 (contract in `src/Alveara.Api.ChainHost/ClockAdvanceEndpoint.cs`); narrowly scoped inside A2; does not reopen ALV-N019 |
| 2026-11-15 | F-064 is in A2; the three partial proof rows (DST day rule, Calendar/Board parity, simultaneous completion) get tests, not disclosures |
| 2026-11-15 | migration M4 (move destinations on AppointmentEvents): from and to recorded atomically with the move, never derived later; rows from before M4 show only "Was ..." |
| 2026-11-15 | F-052 blocked time on the calendar: migration M5 (controlled calendar label) and a ViewSchedule read route that never returns the private reason; legacy rows show "Lunch" only for "Lunch"/"Staff lunch", otherwise "Blocked time" |
| 2026-11-16 | build the OD-03/04 settings change and add the OD-20 and OD-30 tests (gaps found by the OD mapping) |
| 2026-11-16 | Q-5q `DEFERRED_TO_ALV-N022`; F-071 slot retention is a current design decision |
| 2026-11-16 | the three pre-A2 leftovers and the mocked Playwright configuration fixed in separate commits |
| 2026-11-16 | migration M6 (the merge guard on Appointments covers CancelCategory) |

## 6. Story acceptance

A2 does not claim the story's five criteria by itself: A1 delivered the date part, A2 delivers the workflow, scheduling and checkout part, and decision 02 placed T-1 and T-4 in a new attempt A3 after an approved A3 briefing. The story is not complete after A2.
