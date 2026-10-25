# ALV-017-C01 R01 - Parent regression

Parent: STORY-017 (a course story, `COMPLETE`, portal-verified 3 of 3, sync commit `4847e7c`, completion commit `1aa4891`, pushed).

- **Stage 1 (this package):** the parent's 56 backend test ids in the merge, scanner and owned-data classes ran in the focused run (3,076 passed) and all passed with no outcome changed; the parent's screens, the vitest (1,676), mocked Playwright (140) and repo (13) suites pass; the parent's real-backend walkthrough is extended and passes 9 of 9; neighbouring walkthroughs (workspace 14, patients 7, permission-policy 7, permission-policy-admin 8) pass.
- **Baseline for Stage 2:** the STORY-017 final full serial run on its final tree, 5,080 of 5,080 (`stage2/baseline-backend-tests.csv`).
- **Stage 2 (frozen, not yet run):** one full serial backend run on the closure commit, expected 5,131 passed (5,080 - 1 retired + 52 new), plus all gates and five walkthroughs, decided by `stage2/evaluate_stage2.py`.
- **Result for this package:** parent regression **verified at Stage 1; full regression pending Stage 2**.
- **Dependencies** ALV-003-C01, ALV-007-C01, ALV-010-C01 are `COMPLETE`; their suites (audit, permissions, documents, backup/restore) are inside the focused run.
