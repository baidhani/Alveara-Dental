# ALV-009-C02 R01 - Test results (Stage 1)

Stage 1 under the two-stage policy: focused evidence only. **Full backend regression: PENDING** (frozen contract in `stage2/`). No full backend run was made for this story.

Baseline: the ALV-009-C01 R02 Stage 2 run, **4,906 passed** (`stage2/baseline-backend-tests.csv`).

## Backend
- **Focused run** (`artifacts/test-runs/focused.trx`, `.txt`): every new class plus every class whose name contains ExternalCase, RealMigrationInventory, FollowUp, WorkQueue, Recall, UnscheduledTreatment, BackgroundJob, Permission, Architecture, Backup, Document or Audit (the parents' suites, the ALV-N002 job tests, the permission and architecture suites, every backup and restore drill, the document suites): **1,606 passed, 0 failed, 0 skipped** in 21 m 30 s.
- **HTTP-level integration subset** (every `*ApiTests` class through the seam, `artifacts/test-runs/apitests.trx`): **872 passed, 0 failed, 0 skipped** in 16 m 5 s (the previous 860 plus the 12 new `ExternalCaseApiTests`).
- **117 new test ids** against the baseline: `ExternalCaseRulesTests` 39, `ExternalCaseServiceTests` 24, `ExternalCaseSchemaTests` 15, `ExternalCaseTaskJobTests` 14, `ExternalCaseApiTests` 12, `ExternalCasePermissionTests` 10, `ExternalCaseControllerPermissionTests` 2, `ExternalCaseBackupRestoreTests` 1 (listed in `stage2/expected_new_ids.txt`). No existing id is missing or changed in any class the runs cover, and none is retired.
- **Restore drill** (`ExternalCaseBackupRestoreTests`, 1 passed): parties, a sent referral and a lab case with their history, a real document with its file and the link to it are restored with the state of the backup; a later cancellation, removed link and inactive party are not in it; the link points at the one document; the restored database still refuses deleting a party, changing the history, a second link and another patient's document.

## How the identity contract was derived (the finding of the previous story's R01)
`artifacts/derive_identity_manifest.py` (output `artifacts/identity-derivation.txt`) checks three things before it writes the allowlist: (1) every test class declared in the files `git diff` reports as added or changed since the baseline commit (`artifacts/changed_files.txt`, 9 files) ran; (2) the whole assembly, from `dotnet test --list-tests` (`artifacts/list-tests.txt`, 5,006 names), contains exactly the same 117 new ids the runs produced (listed but not run: 0; run but not listed: 0), which would catch a class the filters missed; (3) every baseline id in a class the filters select by name is present and Passed. The allowlist is written in the results file's escaped form, the form the baseline and the Stage 2 comparison use. Expected Stage 2 total: 4,906 + 117 = **5,023**.

## Negative controls (33 of 33 killed; `artifacts/negative-controls.txt`)
Each is a deliberate break of one rule in shipped code, rebuilt and run against the ExternalCase suites, the source restored after every one: the note rules, the overdue and chase-task day rules, draft tracking, key reuse, party kind and activity, the row-version checks on moves and edits of cases and parties, the party lock, finished cases, status-move rules and dates, audit and history entries, the remake date reset, document link rules and the broken-link flag, the overdue filter, party replay, the chase scheduler and both job handlers, the High priority of an overdue chase, and the second permission of the document routes. The first pass killed 31; M32 and M33 (the `ViewDocuments` checks) survived because every role holding the case permissions also holds the document permission; `ExternalCaseControllerPermissionTests` was added and both are now killed.

## Client and repository gates (final tree)
`tsc --noEmit` exit 0; Vitest **103 files, 1,647 tests passed** (30 new in `ExternalCaseScreens.test.tsx`); build exit 0; lint exit 0 (nothing reported in the new files); mocked Playwright **140 passed**; repository checks (`node --test tests/*.test.mjs`) **13 passed**. Outputs: `artifacts/gates/`.

## Real-backend walkthroughs (`artifacts/walkthroughs`, each on a fresh database)
`playwright.external-cases.config.ts` **9 passed** (new); `playwright.work-queue.config.ts` **8** and `playwright.follow-ups.config.ts` **8** (the parents'); `playwright.notifications.config.ts` **10**; `playwright.permission-policy.config.ts` **7** and `playwright.permission-policy-admin.config.ts` **8** (the new permissions are in the matrix they read).

## Artifact counts (`artifacts/gates/leak-counts.txt`) - a disclosure
After the sweep one scratch database remains besides the development database: `AlveraTest_76fbf02b...`, created at 18:49 during this story's work. It holds no case data (one user row, no patients) and is not a template or a restore database. Which backend test run left it undisposed was **not established**. It is not removed by this package (removing it is the owner's decision); the Stage 2 leak criterion compares counts before and after within its own run, so it does not change that verdict, but the "unchanged since the previous story" comparison made in earlier packages (one database) does not hold here.

## Evaluator validation (`stage2/validate_evaluator.py`, output `stage2/evaluator-validation.txt`)
9 of 9 scenarios behave as required (an 80-minute and a 3-minute run meeting every criterion are accepted; a failed or skipped test, a missing baseline id with or without a replacement, a changed outcome, an allowlisted id that did not run and an extra id are rejected).

## Frozen Stage 2 contract
Run once on the Stage 1 review-closure commit, clean tree, nothing else running:
`sh .alveara/handoffs/ALV-009-C02/R01-evidence/stage2/run_stage2.sh <implementation-sha> <out-folder>`.
It runs the full serial backend suite with trx, tsc, full Vitest, build, lint, mocked Playwright, repository checks, the six real-backend walkthroughs above, and the artifact leak counts before and after; `evaluate_stage2.py` decides.
Expectations: backend exactly **5,023 passed, 0 failed, 0 skipped**; the same ids and outcomes as the 4,906-test baseline plus **exactly the 117 ids in `stage2/expected_new_ids.txt`**, nothing retired; template used (one build, zero fallbacks, at least 3,000 clones); leak counts unchanged; Vitest 1,647 in 103 files; Playwright 140; repository checks 13; walkthroughs 9, 8, 8, 10, 7, 8.
