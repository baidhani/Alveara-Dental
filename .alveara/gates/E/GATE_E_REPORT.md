# Alveara Dental - Gate E (Operations, Documents, and Data Ready) - Evaluation Report

**DRAFT SKELETON - Stage 1 not started. No result below is a result; every "Result" cell is PENDING until the evidence run.** Thresholds, methods and environments are the predeclared E1-E8 rows in `.alveara/QUALITY_GATES.md` plus the row E9 added on 2026-10-30 (see Disclosures 1).

Product code under test: PENDING (must include the test-only repair `2be1d73`). Evidence commit: PENDING.

## Preconditions

- Items 39-53 are `COMPLETE` in `.alveara/EXECUTION_STATUS.json` with `blockingIssues` empty and no revalidation pending (15 contributor stories; checked 2026-10-30, to be re-checked at the evidence head).
- Gate D is PASS, independently approved, closure `df99323` (review `.alveara/gates/D/reviews/d052330.md`).
- The evidence run must be on one clean tree (`evidence/REPOSITORY_STATE.md`) and the full serial backend regression is run fresh in Stage 2, not reused from ALV-N006 (lesson of finding C-REV-01).

## Criterion results

| # | Result | Method and evidence (candidates; assertions marked "read" were read on 2026-10-30) |
|---|---|---|
| E1 | PENDING | `DocumentBackupRestoreTests` (read: files restored byte for byte, SHA-256 recorded at import still matches, restore fails on a missing file or a hash mismatch; the sample set holds real PNGs and a PDF), `DocumentIntegrityServiceTests`, `DocumentPathSafetyTests`, `DocumentFileRulesTests` (read: PNG/JPEG/TIFF recognised by signature, blocked extensions refused), `BackupRestoreTests`; walkthroughs documents, documents-library |
| E2 | PENDING | `BackgroundJobTests.Restart_recovery_resumes_a_job_stuck_InProgress_from_a_crashed_process_without_duplicate_side_effects` (read: recovered once, handler ran once, job Succeeded), `FollowUpReminderJobTests` (a job left claimed by a dead server sends the reminder once; a committed effect is finished without a second reminder; forcing a rerun changes nothing); walkthroughs follow-ups, work-queue |
| E3 | PENDING | `ExternalCaseApiTests.A_document_is_linked_not_copied_shown_in_the_case_and_flagged_when_it_goes_bad`, `ExternalCaseServiceTests`; manual-style walkthrough external-cases |
| E4 | PENDING | `PatientMergeServiceTests.A_merge_moves_every_row_that_held_the_retired_patients_id_to_the_survivor_and_loses_nothing` (read: retired patient has 0 rows left in every `PatientOwnedData` table, survivor holds the sum; the catalog includes Appointments, Documents and the Billing tables), `PatientMergeChartPreservationTests`, `PatientUnmergeServiceTests`, `PatientMergeRecoveryTests`; walkthrough patient-merge |
| E5 | PENDING | `ExportDomainCatalogTests`, `PatientDataPackageDocumentTests` (file in the package byte for byte with the manifest hash), `AllPatientsPackageTests`; walkthroughs patient-export-package, patient-portability |
| E6 | PENDING | `ImportAppointmentDryRunTests`, `ImportAppointmentCommitTests` (read: every problem reported at once as a fixed message, scheduler conflict rules, replay books nothing twice, crash-recovered commit books nothing twice), `PatientImportServiceTests`, balance import through `BillingService.RecordOpeningBalanceAsync`; walkthrough bulk-import |
| E7 | PENDING | `FinancialReportsTests`, `BalancesReportTests`, `OperationalReportsTests`, `ClinicalReportsTests`, `ReportScaleTests`; reports walkthrough with an independent SQL cross-check |
| E8 | PENDING | Manual copy review re-run at the evidence head (grep for improv/better/worse/decline/% improvement across client pages, API messages, CSV and `docs/REPORTING.md`); first pass 2026-10-29 found no improvement claim |
| E9 | PENDING | `NotificationApiTests`, `NotificationPermissionTests`, `NotificationSchemaTests`, break-glass tests (`Break_glass_needs_a_reason_and_a_confirmation_audits_every_attempt_and_returns_one_message`, `Break_glass_returns_no_content_when_the_success_cannot_be_audited`, `Break_glass_on_an_unknown_message_is_audited_and_returns_not_found`); notifications walkthrough |

## Integrated verification

PENDING: Stage 1 = targeted classes, walkthroughs, client and repo gates, leak counts. Stage 2 (frozen, after the Stage 1 review) = fresh full serial backend run plus the same client/repo gates and walkthroughs.

## Disclosures (carry into the final report)

1. **E9 is a row added on 2026-10-30** to align QUALITY_GATES with the plan's Gate E prompt (bullet 2). Recorded as an alignment, `thresholdsChanged: false`; this is the owner's interpretation and the reviewer may judge it. Its thresholds are drawn from criteria 3-7 of the ALV-N015 R02 acceptance evidence (read in full 2026-10-30). Thread break-glass shares the message break-glass code path; only the message form is tested.
2. **"Recoverable" for notifications means consumer recovery** (owner's interpretation). No test restores a backup that contains notification messages. Also untested (N015): a fault between the notice insert and the cursor advance (one transaction).
3. **Billing-ledger walkthrough.** It passed 11/11 at Gate D and failed test 8 afterwards: the workspace header's export buttons (`344ed89` STORY-018, `912312a` ALV-018-C01) each render an empty `p.alv-clinical__status`, so an administrator session matched three paragraphs. Confirmed by a run on `22f66e9` (7 passed, 1 failed, 3 not run). Test-only repair `2be1d73` (11/11) is submitted for review inside Stage 1; until approved it is not counted as passing.
4. **E8 observations:** `lowerIsBetter` metadata in the API and CSV is direction metadata, not a claim; `PerioComparisonPanel` labels site trends Improved/Worsened between two stored periodontal exams (a clinical comparison, not an outcome metric or a percentage).
5. **E6 untested list** from the ALV-018-C02 handoff (rollback guards 51263/51267/51268, restoring a backup that contains import files, ambiguous and merged patients in a file, a start time passing into the past between dry run and commit, a conflict or 503 leaving a row Valid, a half-done retry beyond the simulated one, stray-file removal on a real disk in the walkthrough). E6's "ledger" part covers opening balances only (positive amount, empty account).
6. The ALV-N006 Stage 2 addendum's "three read-only checks" undercounts (two monitors also polled every 30 s); the addendum is immutable.
7. PROGRESS.md still says "Nothing is pushed" in the last ALV-N006 entries; the closure was pushed (`22f66e9`). Append-only; not rewritten.

## Reproduce / Repository state and next action

PENDING.
