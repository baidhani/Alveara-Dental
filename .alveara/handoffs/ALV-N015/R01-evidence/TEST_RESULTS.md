# ALV-N015 R01 - Test results (Stage 1)

Stage 1 under the two-stage policy: focused evidence only. **Full backend regression: PENDING** (required; frozen contract below). No full backend run was made for this attempt.

## Focused backend run (one run, tree at the implementation commit)
`dotnet test --no-build --filter "FullyQualifiedName~NotificationSchemaTests|...NotificationPermissionTests|...NotificationApiTests|...RealMigrationInventoryTests|...DocumentApiTests|...DocumentLibraryApiTests"` -> **156 passed, 0 failed, 0 skipped** in 1 m 55 s (`artifacts/test-runs/backend-focused.txt`).
- `NotificationApiTests`: 23 (send, idempotent replay, changed body conflict, validation and limits, CSRF and session, rate limit, non-participant 404, hide rules, other person's notification, read-all and clear, banner counts, reply and thread, patient link and no id in lists, break-glass refusals and audit, audit failure returns no content, unknown message, privacy of stored rows and captured logs, concurrent read/hide, concurrent same-key send, consumer once-only, replay after a lost cursor, disabled account).
- `NotificationSchemaTests` and `RealMigrationInventoryTests`: schema refusals by raw SQL, thresholds, migration down/up on a clone, high-water mark, inventory (14 rollback classes).
- `NotificationPermissionTests`: the four permissions per role.
- `DocumentApiTests` + `DocumentLibraryApiTests` (66): the unchanged STORY-010 API regression.

## Negative controls
Schema: 7 of 7 mutations killed (`artifacts/negative-controls-schema.txt`). Service layer: **7 of 7 killed**, files restored byte-identical, `git diff` empty (`artifacts/negative-controls-service.txt`): non-participant read, rate limit removed, changed body accepted as replay, unread hide allowed, break-glass success not audited, consumer notifies every account, consumer does not skip an existing notice. Client: removing the stale-answer guard makes the new unit test fail (run, then restored).

## Client and repository gates (artifacts/test-runs)
tsc exit 0; Vitest 98 files, 1,553 tests passed (`Notifications.test.tsx`: 8); build exit 0; lint exit 0 (warnings only); mocked Playwright 140 passed; repository checks (`node --test tests/*.test.mjs`) 13 passed.

## Real-backend walkthroughs (artifacts/walkthrough, artifacts/regression-story010)
`playwright.notifications.config.ts`: **10 passed** (bell, banner, receipt, history and hide, patient link, duplicate delivery by the real background consumer, break-glass, database refusals, axe scans of six screens in both themes with zero critical or serious violations). Unchanged STORY-010 walkthroughs: `playwright.documents.config.ts` 14 passed; `playwright.documents-library.config.ts` 12 passed.

## Frozen Stage 2 contract
Run once on the Stage 1 review-closure commit (equivalence rule of HANDOFF_SCHEMA section 4: its diff from the implementation commit may contain only the Stage 1 review artifact, `EXECUTION_STATUS.json` and `BUILD_STATE.md`), clean tree, nothing else running:
`sh .alveara/handoffs/ALV-N015/R01-evidence/stage2/run_stage2.sh <implementation-sha> <out-folder>`.
It runs: the full serial backend suite with trx, tsc, full Vitest, build, lint, mocked Playwright, repository checks, the three real-backend walkthroughs through `scripts/Invoke-RealBackendWalkthrough.ps1`, and the artifact leak counts before and after; `evaluate_stage2.py` decides.
Expectations: backend exactly **4,392 passed, 0 failed, 0 skipped**, the same ids and outcomes as the 4,324-test baseline plus 68 new, 55-minute ceiling, zero template fallbacks and at least 3,000 clones, leak counts unchanged and no template left; Vitest 1,553 in 98 files; Playwright 140; repository checks 13; walkthroughs 10, 14 and 12 passed with none failed; gates tsc, build, lint exit 0.
Note: 20 theory rows of the baseline appear differently in `--list-tests` than in the trx (parameter formatting); the comparison uses the trx, as in the earlier Stage 2.
