# ALV-N021 A1-R01: acceptance evidence

Raw finding text: `docs/design/ALV-N021_SOURCE_EVIDENCE_APPENDIX.md` (section numbers below). Server tests are in `src/Alveara.Api.Tests`, client tests in `src/alveara-client/src`.

## 1. Finding records (brief section 4: each recorded with its proving test)

| Finding | Raw (appendix) | Record | Cause found | Change | Proving tests | Negative control |
|---|---|---|---|---|---|---|
| **F-107** (S1) printed DOB one day early | A.35 | **CONFIRMED** | `PrintPreview.tsx` formatted the date-only `patientDateOfBirth` with `new Date(iso).toLocaleDateString()`: `"1985-03-14"` is read as midnight UTC = 13 March evening in Chicago | `formatDateOnly` (`components/dateOnly.ts`) builds the date from its parts; used by the print and `documentText.formatDate` | `dateOnly.test.tsx`: "keeps the calendar day in %s" (Chicago, Honolulu, UTC, Kiritimati), "prints the date of birth the header shows, in %s" (4 zones), "negative control: parsing the same text as an instant moves the day in a zone behind UTC", "shows what is not a real date as it came" | Old line restored in `PrintPreview.tsx`: 3 failed (Chicago, Honolulu, Kiritimati; UTC rightly passes); restored byte-identical |
| DOB limit (new, F-107 kind) | not in register | **CONFIRMED in classification** (owner approved fixing in A1) | `PatientFieldsForm` max date = `toISOString().slice(0,10)` = UTC day | `todayDateOnly()` | `dateOnly.test.tsx`: "is the viewer's own day on a Chicago evening, not the UTC day", "is the viewer's own day on a morning east of UTC" | inline in both tests: the old expression gives tomorrow / yesterday |
| **F-118** (S3) performed date one day late | A.36 | **CONFIRMED** | `BillingService.Timeline.cs:38` formatted the UTC instant as `yyyy-MM-dd` (UTC day) | `clock.ToPracticeLocal(...)` | `BillingTimelineAgingTests.A_charge_names_the_practice_day_its_procedure_was_performed_not_the_UTC_day` (22:42 Chicago, asserts the UTC day differs) | old line: 1 failed; restored byte-identical |
| Perio chart label (same class as F-118, found in the A1 read-only check) | brief section 3 | **CONFIRMED** | `DiagnosisService.Structure.cs:117` used the UTC day | `clock.ToPracticeLocal(...)` | `DiagnosisStructureServiceTests.A_linked_periodontal_chart_is_named_by_the_practice_day_it_was_recorded_not_the_UTC_day` | old line: 1 failed; restored byte-identical |
| **F-184** (S3) answer time replaced by scheduling time | A.39 | **CONFIRMED** | Rows are append-only (no data loss: the original answer row is kept); the view used the latest row of any kind for "Entered by ... on" | `AnswerRow`: latest row whose acceptance differs from the row before; `AnswerRecordedByName/AtUtc` + `DecisionRevision` from it; client shows "Last changed" apart | `TreatmentPlanStateServiceTests.When_the_answer_was_entered_stays_put_through_scheduling_and_completion_and_moves_only_with_a_new_answer`; client `TreatmentPlanAnswers.test.tsx` "keeps when the answer was entered after a later scheduling, and shows the later change apart", "shows no separate last change while the answer is the latest change" | view on latest row again: 1 failed; restored byte-identical |
| **F-218** (S4) unschedule moves the time | A.40 | **DUPLICATE** of F-184 | same cause | same fix | same tests (unscheduling is a step of the server test) | same |
| **F-100** (S3) "8:00:00 AM" looked rounded | A.34 | **NOT_REPRODUCED** | The answer's time is set by the server clock at the save; no form asks for a time and nothing rounds it. An exact :00 time is a coincidence of the clock, not a default | none | `TreatmentPlanStateServiceTests.The_time_an_answer_is_entered_is_the_clock_at_the_save_to_the_second_not_a_rounded_or_default_time` (an instant with seconds and milliseconds comes back exactly) | n/a (no change) |
| **Q-5an** "9:11:00 PM" zero seconds | A.45 | **NOT_REPRODUCED** | Prescribed time is the server clock at the save | none | `PrescriptionServiceTests.The_prescribed_time_is_the_clock_at_the_save_to_the_second_not_a_rounded_time` | n/a |
| **F-030** 5-hour gap observation | A.1 | **Verification of correct behaviour** (A1-DECISION-01: not a defect; the raw entry closes it) | New record's history time matched the PC clock; the earlier gap was a testing pause | none | `ProcedureCatalogLifecycleTests.A_new_procedure_records_the_exact_instant_it_was_saved_unshifted_by_the_practice_zone` (8:54:10 PM Chicago = 01:54:10 UTC next day; returned exact, offset zero, seconds kept) | n/a |
| **F-133** due times | A.37 | **WITHDRAWN** (row kept) | Owner confirmed the times shown are the times entered | none | n/a | n/a |

## 2. A1-DECISION-01 contract, item by item

| # | Requirement | Where | Proof |
|---|---|---|---|
| 1 | Dispatch rebuilds on the stored version for reprint and export; v1 keeps fields, order, formatting, UTC aging and version value; no rewritten hashes or rows; unknown version fails explicitly | `BillingService.Documents.cs` reprint/export pass `d.TemplateVersion`; `FinancialDocumentBuilder.RequireSupported` | `A_version_1_statement_and_receipt_reprint_and_export_byte_for_byte_as_the_original_builder_made_them`; `A_document_with_an_unknown_template_version_is_refused_not_rebuilt_with_the_current_one`; `An_unknown_template_version_is_refused_for_statements_and_receipts(0, 3, -1)` |
| 2 | v2 subtracts practice-local dates, each instant at its own offset, clamped at zero; thresholds unchanged | `FinancialDocumentBuilder.PracticeBasis`, `AgingAt` | `An_evening_service_ages_one_day_more_by_practice_days_than_by_UTC_days`; `Version_2_buckets_turn_on_practice_day_edges` (30/31, 60/61, 90/91); `Each_instant_is_converted_with_its_own_offset_across_the_spring_change` / `_autumn_change`; `A_year_end_service_counts_from_the_practice_day_it_happened`; `A_zone_ahead_of_UTC_moves_a_morning_service_to_the_next_practice_day` (Tokyo) and the evening test (Chicago, behind UTC); `Version_2_changes_only_ages_buckets_version_and_basis_never_an_amount` |
| 3 | Freeze the basis with the immutable envelope; bind it to preview/issue; validate against cut-offs; zone change before issue needs a fresh preview; after issue no change | `DocumentAgingBasis` in `EnvelopeJson` and in the statement content (hash binds it); `AgingAt` validates policy, instant, charge coverage | `A_version_2_statement_stores_its_frozen_basis_and_reprints_identically_after_the_practice_zone_changes`; `A_version_2_statement_rebuilt_from_its_stored_basis_is_identical_whatever_the_zone_is_later`; `A_practice_zone_change_between_preview_and_issue_makes_the_preview_stale`; `A_basis_that_does_not_belong_to_the_document_is_refused` |
| 4 | Preview/issue equality and idempotent replay; unissued v1 preview stale; issued v1 retried with its key returns the original | issue: prior-key replay check runs before the version check | `A_version_1_preview_made_before_the_upgrade_is_stale_and_must_be_previewed_again`; `Retrying_an_issue_that_already_happened_under_version_1_returns_that_original_document`; existing `BillingDocumentServiceTests` (stale-version token now V=3) |
| 5 | Live aging uses the current policy and agrees with a new statement; every builder caller documented | callers below | `Live_aging_and_a_statement_made_at_the_same_instant_agree` |
| 6 | v1 receipts byte/hash identical | receipts dispatch on stored version; new fields omitted when null | `Every_captured_version_1_document_rebuilds_with_identical_canonical_bytes_and_hash` (12 documents, statements and receipts); `A_version_2_receipt_differs_from_version_1_only_in_the_version_it_carries` |
| 7 | Existing envelope storage; no unreviewed migration | `EnvelopeJson` (existing column) | no migration in the diff (`CHANGED_FILES.md`) |

**Builder callers and the version each uses (item 5):**

| Caller | Version |
|---|---|
| `BillingService.Documents` preview (`PreviewAsync` → `BuildAsync`) | current (2) |
| issue (`BuildAsync` with `token.V`) | must equal current, else stale (after the prior-key replay) |
| reprint / export (`BuildAsync` with `d.TemplateVersion` and the stored envelope) | the stored version (1 or 2); others refused |
| `BillingService.Timeline` live aging (`AgingAt` with `PracticeBasis`) | current policy (practice days) |

**Focused proof list of the decision:**

| Required | Test |
|---|---|
| v1 statement and receipt from the pre-change builder reprint identical (baseline/original-builder evidence) | fixture captured at `7bf310e` before any builder change (12 documents); `Every_captured_version_1_document_rebuilds_with_identical_canonical_bytes_and_hash`; `The_verbatim_original_builder_copy_reproduces_the_captured_bytes`; the DB test hashes v1 rows with the original copy |
| v2: evening, 30/31, 60/61, 90/91, both DST changes, year boundary, ahead/behind UTC, amounts unchanged | item 2 above |
| v2 reprint identical after zone change; mismatch between preview and issue rejected | item 3 above |
| unsupported version, stale v1 preview, issued v1 replay, export compatibility, live/new agreement | items 1, 4, 5 |
| Negative control: boundary-affected v1 routed through v2 fails the reprint check | `Negative_control_a_boundary_affected_version_1_statement_built_by_the_version_2_rule_no_longer_matches`; `Negative_control_a_version_1_row_rebuilt_as_version_2_does_not_match_and_is_not_shown` |

## 3. Brief section 4 plan for A1

| Plan line | Done |
|---|---|
| F-107, F-118 reproduced in a zone behind UTC and across the date line before any change | Proven by negative controls: with the old code restored, the F-107 tests fail in Chicago, Honolulu and Kiritimati (+14) and the F-118 test fails at 22:42 Chicago; both pass on the fix |
| F-184/F-218 answer time and last-change time stored and shown separately | Both were already stored (append-only rows); now both are returned and shown |
| F-100, Q-5an traced | NOT_REPRODUCED with seconds-kept tests |
| F-030 regression test | added |
| F-133 WITHDRAWN row kept | kept |
| About 94 client date sites to classify (brief section 3) | `DATE_SITE_CLASSIFICATION.md`: 148 matches in 93 files; one wrong-day site found and fixed |

## 4. Story acceptance
ALV-N021 has 5 acceptance criteria for the whole story. This attempt claims none of them as passed: A1 covers the date part of the first criterion ("resolve the mapped date defects including F-107/F-118"); the rest is A2.
