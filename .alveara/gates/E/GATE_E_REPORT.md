# Alveara Dental - Gate E (Operations, Documents, and Data Ready) - Evaluation Report, Stage 1

**Stage 1 decision: all nine criteria PASS on the Stage 1 evidence, subject to independent review. Gate E is NOT passed:** the fresh full serial backend regression (Stage 2) has not been run and is required before the final decision. Thresholds, methods and environments are the predeclared E1-E8 rows in `.alveara/QUALITY_GATES.md` plus row E9, added on 2026-10-30 to align the table with the plan's Gate E prompt (Disclosure 1).

Date: 2026-10-29/30 (America/Chicago). Evidence run on `173051da01b1eababfb9bf10773283cfa3a6f74d` (clean tree before and after; product code identical to `22f66e9` plus the test-only repair `2be1d73`). Evidence commit: `c3d56e3` (evidence short sha `c3d56e3`; it adds only the files under `.alveara/gates/E/evidence/`).

## Preconditions

- Items 39-53 (15 Gate E contributor stories) are `COMPLETE` in `.alveara/EXECUTION_STATUS.json` with `blockingIssues` empty and no revalidation pending (checked 2026-10-30).
- Gate D is PASS, independently approved, closure `df99323` (review `.alveara/gates/D/reviews/d052330.md`).
- The Stage 1 evidence run was on one clean tree (`git status` empty before and after: `evidence/summary.txt`). The full regression is NOT reused from ALV-N006 (lesson of finding C-REV-01); it is Stage 2.
- One item asks for review in this stage: the test-only repair `2be1d73` of the billing-ledger walkthrough (Disclosure 3).

## Criterion results (Stage 1)

"Read" = the assertions were read on 2026-10-30; otherwise the criterion rests on the test names, their pass in the focused run, and the walkthrough. Focused run: 1,546 of 1,546 passed (`evidence/backend-focused.txt`, results by class).

| # | Result | Method and evidence |
|---|---|---|
| E1 | **PASS** | `DocumentBackupRestoreTests` (read: every restored file equals the original byte for byte and its SHA-256 still equals the hash recorded at import; a backup that lacks a file, or whose file no longer matches its hash, fails the restore validation; the sample set holds real PNGs and a PDF), `DocumentIntegrityServiceTests`, `DocumentPathSafetyTests`, `DocumentFileRulesTests` (read: PNG/JPEG/TIFF recognised by signature, blocked extensions refused whatever their case), `BackupRestoreTests`; walkthroughs documents 14/14, documents-library 12/12. |
| E2 | **PASS** | `BackgroundJobTests.Restart_recovery_resumes_a_job_stuck_InProgress_from_a_crashed_process_without_duplicate_side_effects` (read: recovered once, handler ran once, job Succeeded), `FollowUpReminderJobTests` (read by name: a job left claimed by a dead server sends the reminder once; a job whose effect had committed is finished without a second reminder; forcing a rerun changes nothing); walkthroughs follow-ups 8/8, work-queue 8/8. |
| E3 | **PASS** | `ExternalCaseApiTests.A_document_is_linked_not_copied_shown_in_the_case_and_flagged_when_it_goes_bad`, `ExternalCaseServiceTests`; the threshold's manual walkthrough: external-cases 9/9. |
| E4 | **PASS** | `PatientMergeServiceTests.A_merge_moves_every_row_that_held_the_retired_patients_id_to_the_survivor_and_loses_nothing` (read: the retired patient has 0 rows left in every `PatientOwnedData` table, the survivor holds the sum of both; the catalog includes Appointments, Documents and the Billing tables), `PatientMergeChartPreservationTests`, `PatientUnmergeServiceTests`, `PatientMergeRecoveryTests`; walkthrough patient-merge 9/9. |
| E5 | **PASS** | `ExportDomainCatalogTests`, `PatientDataPackageDocumentTests` (by name: each file is in the package under its patient byte for byte with the hash the manifest gives; a mismatch makes the package partial), `AllPatientsPackageTests`; walkthroughs patient-export-package 10/10, patient-portability 7/7. |
| E6 | **PASS** | `ImportAppointmentDryRunTests` and `ImportAppointmentCommitTests` (read by name: every problem in a row reported at once as a fixed message; scheduler rules refuse clashes, past, daylight-saving and out-of-hours starts; a rerun gives the same result; replay books nothing twice; a crash-recovered commit books nothing twice), `PatientImportServiceTests`, opening balances through `BillingService.RecordOpeningBalanceAsync`; walkthrough bulk-import 13/13. |
| E7 | **PASS** | `FinancialReportsTests`, `BalancesReportTests`, `OperationalReportsTests`, `ClinicalReportsTests`, `ReportScaleTests` (a year of data, totals equal what was inserted); reports walkthrough 7/7 with its independent SQL cross-check. |
| E8 | **PASS** | Manual copy review re-run at this head, 2026-10-30: grep for improv/better/worse/worsen/declin/outperform/% up/down/change over the client report pages, the report and measurement services, `src/Alveara.Api/Architecture/Reporting` and `docs/REPORTING.md`. No reporting text claims improvement. Hits: doc comments that deny it ("never called an improvement, a worsening or an effect"; `docs/REPORTING.md` line 76: a valid change is worded Higher/Lower/The same, never an improvement, with a sentence that a difference is not a cause), the treatment-plan status "Declined" (a status, not an outcome), `lowerIsBetter` direction metadata, and test fixtures. A comparison needs a stored baseline (`MeasurementBaselineTests`, `MeasurementApiTests`, `MeasurementView.test.tsx`). See Disclosure 4. |
| E9 | **PASS** | `NotificationApiTests` (read by name and acceptance evidence: a list never carries the subject or the body; the same key creates one message; a replay after a lost cursor creates no second notification; break-glass needs a reason and a confirmation and audits every attempt; an unauditable success returns 503 `audit_unavailable` with no content and no success audit entry; an unknown message returns 404 and a refused-audit entry), `NotificationPermissionTests` (read: break-glass belongs to Admin alone; the history permission to Admin and Office Manager), `NotificationSchemaTests` (read by name: a message and its recipient rows are never updated or deleted, even by direct SQL; a notification changes only its read and deleted times and is never physically deleted); notifications walkthrough 10/10. See Disclosure 2 on "recoverable". |

## Integrated verification (Stage 1)

| Check | Result | Evidence |
|---|---|---|
| Backend build | exit 0 | `gates/backend_build.txt` |
| Backend focused (E1-E9 classes) | **1,546 of 1,546**, 0 failed, 0 skipped, 25 min 52 s | `backend-focused.txt`, `trx/` |
| `tsc -b`, `npm run build`, `npm run lint` | exit 0 / 0 / 0 (lint warnings only) | `gates/` |
| Vitest | **1,879 of 1,879** (120 files) | `gates/vitest.txt` |
| Mocked Playwright | **138 passed** | `gates/playwright_mocked.txt` |
| Repository checks | **13 of 13** | `gates/repo_checks.txt` |
| Real-backend walkthroughs (12, each on a fresh database) | documents 14, documents-library 12, follow-ups 8, work-queue 8, external-cases 9, patient-merge 9, patient-export-package 10, patient-portability 7, bulk-import 13, reports 7, notifications 10, billing-ledger 11 (with the repair): all pass | `gates/walk_*.txt`, `walkthroughs/` |
| Artifact leak counts | identical before and after (SQL holds only AlveraDev; no AlveraTest/Template/Restore database; 0 SQL logins) | `leak_before.txt`, `leak_after.txt` |

## Stage 2 (frozen contract; NOT run)

The contract is frozen in `.alveara/gates/E/stage2/` and is run once, only after the Stage 1 review, on the Stage 1 closure commit, with the machine idle:

- `run_stage2.sh <173051da01b1eababfb9bf10773283cfa3a6f74d> <out-folder>` runs the fresh full serial backend suite (`dotnet test`, template database as in ALV-N006), `tsc -b`, Vitest, build, lint, mocked Playwright, the repository checks and 17 real-backend walkthroughs (documents 14, documents-library 12, follow-ups 8, work-queue 8, external-cases 9, patient-merge 9, patient-export-package 10, patient-portability 7, bulk-import 13, reports 7, notifications 10, billing-ledger 11, flow 10, permission-policy 7, permission-policy-admin 8, workspace 14, patients 7), each on a fresh database, with leak counts before and after.
- `evaluate_stage2.py` decides every criterion with no judgement: exactly 5,612 backend tests passed, 0 failed, 0 skipped; the same ids and outcomes as the ALV-N006 Stage 2 baseline (`.alveara/handoffs/ALV-N006/R01-regression-evidence/backend-tests.csv`); no new and no retired id (no product code has changed since); the template built once with zero fallbacks and at least 3,000 clones; artifact counts unchanged; each client gate and walkthrough at its count; a clean tree before and after; and **only `.alveara/` files and `PROGRESS.md` differ between the Stage 1 evidence run commit and the run commit** (`src/` identical). The backend duration is reported, not a criterion.
- `validate_evaluator.py` proves the evaluator on 11 synthetic scenarios (accepts a good run at 80 and at 3 minutes; rejects a failed or skipped test, a missing or changed baseline id, an id outside the baseline, an extra id, a wrong runner output variable, and a run whose `src/` differs): 11 of 11 behave as stated (`stage2/evaluator-validation.txt`). `stage2/runner-proof.txt` proves all 17 walkthroughs write their output where the runner expects.
- The full run is long (about 83-97 minutes in ALV-N006); it will be started only after the owner agrees, with monitors, and the repository must not change while it runs.

## Disclosures

1. **E9 is a row added on 2026-10-30** to align QUALITY_GATES with the plan's Gate E prompt (evidence bullet 2). Recorded as an alignment, `thresholdsChanged: false`; this is the owner's interpretation and the reviewer may judge it. Its thresholds are drawn from criteria 3-7 of the ALV-N015 R02 acceptance evidence. Thread break-glass shares the message break-glass code path; only the message form is tested.
2. **"Recoverable" for notifications means consumer recovery** (owner's interpretation). No test restores a backup that contains notification messages. A fault between the notice insert and the cursor advance is untested (one transaction).
3. **Billing-ledger walkthrough.** It passed 11/11 at Gate D and then failed test 8: the workspace header's export buttons (`344ed89` STORY-018, `912312a` ALV-018-C01) each render an empty `p.alv-clinical__status`, so an administrator session matched three paragraphs (confirmed by a run on `22f66e9`: 7 passed, 1 failed, 3 not run). The test-only repair `2be1d73` scopes the locator to `section.alv-bill`; it passes 11/11 here. It is submitted for review in Stage 1; **it is not counted as passing until the reviewer approves it**. No Gate E criterion depends on it.
4. **E8 observations:** `lowerIsBetter` is direction metadata in the API and CSV, not a claim; `PerioComparisonPanel` labels site trends Improved/Worsened between two stored periodontal exams (a clinical comparison of stored readings, not an outcome metric or a percentage).
5. **E6 untested list** (ALV-018-C02 handoff): rollback guards 51263/51267/51268, restoring a backup that contains import files, ambiguous and merged patients in a file, a start time passing into the past between dry run and commit, a conflict or 503 leaving a row Valid, a half-done retry beyond the simulated one, stray-file removal on a real disk in the walkthrough. E6's "ledger" part covers opening balances only (positive amount, empty account).
6. **Criteria not read to the assertion.** For E3, E5, E7 and parts of E2, E6 and E9 the evidence is the test names, their pass and the walkthroughs; their assertion bodies were not read line by line.
7. **Not run in Stage 1:** the full serial backend suite, the all-`ApiTests` run, and any walkthrough other than the twelve above.
8. The ALV-N006 Stage 2 addendum's "three read-only checks" undercounts (two monitors also polled every 30 s); the addendum is immutable. PROGRESS.md still says "Nothing is pushed" in the last ALV-N006 entries; the closure was pushed (`22f66e9`); append-only, not rewritten.

## Reproduce / Repository state and next action

Evidence recorded at `c3d56e3`; report at the Stage 1 closure head. Next action after this stage: the Stage 1 review; then Stage 2 once; then the final gate decision. Item 54 is not authorized until Gate E is PASS and durably closed.
