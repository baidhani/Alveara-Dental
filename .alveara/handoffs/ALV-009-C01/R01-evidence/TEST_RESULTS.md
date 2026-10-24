# ALV-009-C01 R01 - Test results (Stage 1)

Stage 1 under the two-stage policy: focused evidence only. **Full backend regression: PENDING** (frozen contract in `stage2/`). No full backend run was made for this story.

Implementation commit: `4b12cc19bf8de4bf97a9c5e0aab3aee95218b8ae` (tree unchanged by the later evidence commits except files under `.alveara/`, `PROGRESS.md`).

## Backend
- **Focused run** (`artifacts/test-runs/backend-focused.trx`, `.txt`): the new classes plus every STORY-009 follow-up class plus the classes the story touches by name or by design: **314 passed, 0 failed, 0 skipped** in 2 m 14 s. By class: `RecallCalculatorTests` 22, `WorkQueueSchemaTests` 46, `RecallJobTests` 13, `UnscheduledTreatmentJobTests` 21, `FollowUpReminderJobTests` 15, `WorkQueueApiTests` 44, `WorkQueueBackupRestoreTests` 1; STORY-009 (unchanged): `FollowUpApiTests` 27, `FollowUpSchemaTests` 38, `FollowUpValidationTests` 12, `FollowUpPermissionTests` 11, `FollowUpReminderTests` 16, `FollowUpReminderApiTests` 1, `FollowUpBackupRestoreTests` 1; ALV-N002 `BackgroundJobTests` 11 and `BackgroundJobPhiSafeLoggingTests` 1; permission `PermissionMatrixTests` 8, `PermissionRegistryTests` 8, `AuthControllerPermissionMatrixTests` 8, `AuthorizationArchitectureTests` 5; `RealMigrationInventoryTests` 5.
- **HTTP-level integration subset** (every `*ApiTests` class, through the seam; `artifacts/test-runs/backend-apitests.trx`): **860 passed, 0 failed, 0 skipped** in 9 m 5 s on the final tree.
- **162 new test ids** against the STORY-009 final full run (4,734, all Passed): `WorkQueueSchemaTests` 46, `WorkQueueApiTests` 44, `RecallCalculatorTests` 22, `UnscheduledTreatmentJobTests` 21, `FollowUpReminderJobTests` 15, `RecallJobTests` 13, `WorkQueueBackupRestoreTests` 1 (listed in `stage2/expected_new_ids.txt`). **No existing id is missing or changed** in the classes these two runs cover (checked against the baseline), and none is retired.
- **Restore drill** (`WorkQueueBackupRestoreTests`): settings, a patient's recall arrangement and a system-made recall task with its history and audit entry survive a real backup and restore into an isolated database; later changes are not in the restore; the restored database still refuses a duplicate source key, a second settings row, an empty arrangement and an edit of a closed task.

## Negative controls (20 of 20 killed; `artifacts/negative-controls.txt`)
Each is a deliberate break of one rule in shipped code, rebuilt and run against the focused suites, source restored after every one. The first pass killed 16 of 20 and four survived (the "task already exists" guard in the recall and unscheduled create handlers, the close handler's "still waiting" guard, and the reminder scan's closed-task filter): four tests were added for them and all four are now killed. K1 recall runs while off; K2 opted-out patients recalled; K3 open recall ignored; K4 future appointment ignored; K5 handler does not re-check the switch; K6 handler ignores an existing task; K7 job key new every time; K8 withdrawn items get tasks; K9 scheduled items get tasks; K10 unscheduled handler ignores an existing task; K11 close handler closes a task whose treatment still waits; K12/K13 assign and priority ignore the row version; K14 assign not audited; K15 overdue filter wrong; K16 closed task can be reassigned; K17/K18 settings ignore the version / are not audited; R1 reminder scan enqueues twice; R2 reminder scan includes closed tasks.

## Client and repository gates (final client tree)
`tsc --noEmit` exit 0; Vitest **102 files, 1,617 tests passed** (28 new in `WorkQueueScreens.test.tsx`, 17 in `FollowUpScreens.test.tsx` unchanged in count); build exit 0; lint exit 0 (warnings only, none in the new files); mocked Playwright **140 passed**; repository checks (`node --test tests/*.test.mjs`) **13 passed**. Outputs: `artifacts/gates/`.

## Real-backend walkthroughs (`artifacts/walkthroughs`, each on a fresh database)
- `playwright.work-queue.config.ts` **8 passed** (new): recall off by default; permission refusals; settings checked then turned on; exactly one unassigned system-made recall task; take, raise priority, complete; stale completion explained; patient timeline; audit event types; axe light and dark on the settings page, the queue and the patient follow-ups page.
- `playwright.follow-ups.config.ts` **8 passed** (STORY-009's own, after three locators were made unambiguous: see the handoff's defects section).
- `playwright.notifications.config.ts` **10 passed**; `playwright.permission-policy.config.ts` **7 passed**; `playwright.permission-policy-admin.config.ts` **8 passed**.
- Artifact counts after the runs: only the development database remains; no test, restore or template databases or template backups (`artifacts/gates/leak-counts.txt`).

## Evaluator validation (`stage2/validate_evaluator.py`, output `stage2/evaluator-validation.txt`)
9 of 9 scenarios behave as required (an 80-minute and a 3-minute run meeting every criterion are accepted; a failed or skipped test, a missing baseline id with or without a replacement, a changed outcome, an allowlisted id that did not run and an extra id are rejected).

## Frozen Stage 2 contract
Run once on the Stage 1 review-closure commit, clean tree, nothing else running:
`sh .alveara/handoffs/ALV-009-C01/R01-evidence/stage2/run_stage2.sh <implementation-sha> <out-folder>`.
It runs the full serial backend suite with trx, tsc, full Vitest, build, lint, mocked Playwright, repository checks, the five real-backend walkthroughs above, and the artifact leak counts before and after; `evaluate_stage2.py` decides.
Expectations: backend exactly **4,896 passed, 0 failed, 0 skipped**; the same ids and outcomes as the 4,734-test STORY-009 final run (`stage2/baseline-backend-tests.csv`) plus **exactly the 162 ids in `stage2/expected_new_ids.txt`**; template used (zero fallbacks, at least 3,000 clones, one template); leak counts unchanged; Vitest 1,617 in 102 files; Playwright 140; repository checks 13; walkthroughs 8, 8, 10, 7, 8.
