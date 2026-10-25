# ALV-017-C01 R01 - Test results (Stage 1)

Stage 1 under the two-stage policy: focused evidence only. **Full backend regression: PENDING** (frozen contract in `stage2/`, expected 5,131 passing). No full backend run was made for this story's final tree. The last full serial run was the STORY-017 final tree (5,080 of 5,080, 43 minutes), which is this story's baseline (`stage2/baseline-backend-tests.csv`).

## Backend
- **Focused run** (`artifacts/test-runs/focused.trx`): every new class, every patient-merge, scanner, owned-data, migration, permission, architecture, backup/restore, document and audit class, and every class that changed with this story's migrations: **3,076 passed, 0 failed, 0 skipped**, 30 minutes.
- **HTTP-level subset** (every `*ApiTests` class, `artifacts/test-runs/apitests.trx`): **883 passed, 0 failed, 0 skipped**, 10 m 25 s.
- **52 new test ids** against the baseline (`stage2/expected_new_ids.txt`): `PatientUnmergeServiceTests` 13, `PatientDuplicateReviewTests` 9, `PatientMergeApiTests` 5 (+1 existing renamed, below), `RetiredRecordGuardTests` 6, `PatientUnmergeSchemaTests` 6, `PatientMergeRecoveryTests` 6, `PatientMergeServiceTests` 3, `PatientMergeBackupRestoreTests` 1, `PatientMergeChartPreservationTests` 1, plus the theory rows (a failure at four named points in the merge and in the undo; a missing or false confirmation).
- **One id retired:** `PatientMergeApiTests.Only_the_administrator_may_use_any_of_the_four_endpoints_...` became `..._any_of_the_nine_endpoints_...` (the same test, now covering nine routes). Declared in `stage2/retired_ids.txt`.
- **Identity contract** (`artifacts/identity-derivation.txt`): 52 new ids in the assembly and in the runs, none listed but not run, none run but not listed; 0 baseline ids in covered classes missing, 0 outcomes changed, 0 not passed; expected total = 5,080 - 1 + 52 = **5,131**.
- **The original STORY-017 tests:** 56 ids of the baseline in the `PatientMerge*`, `PatientDuplicateScanner*` and `PatientOwnedData*` classes ran in the focused run and all passed.

## Client and repository gates (`artifacts/gates/`)
| Gate | Result |
|---|---|
| `tsc --noEmit` | exit 0 |
| `vitest run` | 1,676 passed in 105 files (STORY-017 final: 1,647 in 103; this story adds 28 tests in 2 files) |
| `npm run build` | exit 0 |
| `npm run lint` (oxlint) | exit 0, 41 warnings of which 3 are in this story's files (`UnmergeReview`, `MergeReview`, `MergeHistoryBanner`: the same `react(set-state-in-effect)` rule as the 38 already in the project; left as they are, listed in `artifacts/gates/lint.txt`) |
| mocked Playwright | 140 passed |
| repo checks (`node --test tests/*.test.mjs`, 4 files) | 13 passed, 0 failed |

**Correction of an earlier statement:** the STORY-017 records said "no ESLint config" for lint. The project's lint is oxlint (`npm run lint`), it exits 0. Corrected in `docs/testing/STORY-017_TEST_RESULTS.md`, BUILD_STATE and the item 48 summary.

## Real-backend walkthroughs (each on a fresh database)
- `playwright.patient-merge.config.ts`: **9 of 9** (the new one; screenshots 01 to 07 in `artifacts/walkthroughs/patient-merge/`).
- Neighbours the change touches (`artifacts/walkthroughs/*.txt`, `summary.txt`): workspace **14**, patients **7**, permission-policy **7**, permission-policy-admin **8**, all passed. (A first attempt at these four collided with a still-running earlier loop on the API port and printed "port in use"; those logs were replaced by the clean runs, and no saved log mentions the collision.)

## Negative controls (`artifacts/negative-controls/`)
38 single-rule mutations (30 backend, 8 screen), run against the focused suites with the source restored byte-identical after each: **38 of 38 killed in the final pass.** The first pass left 2 backend mutations surviving (B10 the undo does not check the ledger rows are where they were; B24 the retired-record check also runs inside a merge) and one build failure of my own (B23 invalid C# in the mutation). Two tests were added (`An_undo_is_refused_when_a_row_the_ledger_names_...`, `During_a_merge_a_new_row_for_a_retired_patient_is_answered_by_the_merge_scope_rule_...`), B23 was rewritten, and all three were re-run and killed. The files keep both passes: `c01-controls-backend-first-pass.txt` and the combined `c01-controls-backend.txt`.

## Stage 2 evaluator
`stage2/validate_evaluator.py`: nine scenarios (80 and 3 minutes accepted; a failed, skipped, missing, changed, unlisted or extra test rejected) all behave as stated (`artifacts/evaluator-validation.txt`).

## Disclosures
- A scratch test database `AlveraTest_14dc6e152f1548529b2138ccb83bbfe4` (created 2026-10-25 12:37) was left on the machine by a backend test run interrupted during this story. It was not dropped without the owner's say-so. Leak counts at the time of the evidence sweep: `artifacts/leak-counts-now.txt` (AlveraTest 1 for that reason; Storage, Keys, Backup, Database, Temp folders as listed).
- Nothing was run against production. No secrets or patient data are in the package; test data is synthetic.
