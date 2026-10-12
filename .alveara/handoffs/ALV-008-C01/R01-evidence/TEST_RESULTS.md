# ALV-008-C01 R01 - Test results

Run on the committed implementation tree `9bc1df833915289d30eac6c4c62d683f2fd1d405` (the only later changes are handoff files). Exact outputs are in `artifacts/test-runs/` and `artifacts/regression/`.

## Backend (full, serial)
- `dotnet test Alveara.Api.Tests` (parallel execution off): **3035 of 3035 passed, 0 failed, 0 skipped, 1 h 15 m**.
- **152 new tests** in eight classes: `CompletionRulesTests` 23 (the pure rules: request shape, the inclusive five-minute clock tolerance, the canonical hash, the acceptance time), `CompletedProcedureSchemaTests` 52 (every check constraint, append-only triggers, uniqueness and each link and time trigger, written around the service on purpose), `CompletedProcedureMigrationTests` 1 (rollback and roll-forward), `CompletedProcedureServiceTests` 19 (the operation, retries, stale and racing sets, a plan changed in the gap, wrong references, the time rules, failed audit in either save), `CompletedProcedureEffectsTests` 18 (the chart, the charge, rollback after a late failure, readiness), `CompletedProcedureAmendmentTests` 13, `CompletedProcedureHistoryTests` 5, `CompletionApiTests` 21 (roles, CSRF, the workflow, refusals, the legacy routes unchanged).
- The parent suites (STORY-008, STORY-015 and ALV-015-C01 treatment plans, STORY-007 and ALV-007-C01 billing, odontogram, diagnoses) pass unchanged inside it (see `PARENT_REGRESSION.md`).
- Build: 0 warnings, 0 errors.

## Frontend
- `tsc --noEmit` exit 0; `npm run lint` exit 0 (no warning in a file this attempt changed or added); `npm run build` ok.
- `vitest run`: **1373 of 1373** in 89 files on the final run (14 new in `TreatmentPlanReviewComplete.test.tsx`: the primary and legacy controls, the server-driven table, missing-field messages, the exact request, the confirmed result and links, a partial selection, every failure, key reuse on retry, axe, the legacy disclosure on the per-procedure control, and the enriched history). **One disclosure:** an earlier full run in the same session had one failure in `BackupRecoveryPage.test.tsx` (a file this attempt did not touch); it passed 3 of 3 alone and the full suite passed on the rerun (`artifacts/test-runs/frontend-gates.txt`).
- Mocked browser (Playwright, default config): **136 of 136**. It was 134 of 136 until the architecture map's story catalog (`.alveara/story_catalog.json`) was given an entry for `ALV-008-C02`, which the reviewer had added to the execution status after the design review.
- Repository checks (`node --test tests/*.test.mjs`): **8 of 8**.

## Real-backend walkthroughs (fresh database each)
- The new walkthrough `completion-real-backend.spec.ts`: **11 of 11** (axe in light and dark on the review, the failure, the history and the ambiguity case: 0 critical or serious).
- The whole sweep (`artifacts/regression/`): **24 runs, 258 of 258 passed** (the existing 23 runs 247, plus the new walkthrough 11).
- Leak check: databases and logins unchanged (`artifacts/test-runs/leak-check.txt`).

## Negative controls
Fifteen rules, each mutated, built, tested and restored byte-identical (SHA-256) (`artifacts/test-runs/negative-controls-c008.txt`). Fourteen were caught on the first run. **Mutant 1** (removing the atomic plan-version check from the completion's save) was not: all 18 service tests of that time passed, because the earlier non-atomic check and the unique constraints caught every case the tests used. A deterministic test (`A_plan_changed_after_it_was_checked_but_before_the_set_is_saved_is_still_a_conflict_and_records_nothing`, a clock hook that renames the plan between the check and the save) was added and now kills it (`negative-controls-c008-mutant1-rerun.txt`). The closing "unmutated tree" line of that rerun script said 18 of 19; this could not be reproduced (eight consecutive reruns of the class passed 19 of 19, and the whole 173-test completion set passed on the restored tree); it most likely used the mutated build, which was not proven.

## Migration
Down and Up verified by `CompletedProcedureMigrationTests` (`artifacts/test-runs/migration-reversibility.txt`).

## Not tested / untested claims
- Corrections are made through the API only; no screen for amending a completed procedure exists, so none is tested in the browser (the API is tested at the service, API and walkthrough levels).
- Mutation checks cover fifteen rules, not the whole feature.
- No load or timing test of a large set (the limit is 50 procedures; the tests use up to three).
