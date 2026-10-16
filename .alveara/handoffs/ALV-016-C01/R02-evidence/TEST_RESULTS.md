# ALV-016-C01 R02 - Test results

## Risk declaration (Binding Testing Cadence Notice and its review response, 2026-10-13)
**HIGH RISK.** Criteria from the notice: the change is in the authoritative transactional completion path that records sets and creates ledger entries; it is the repair of a failed mandatory high-risk regression; and it changes how a patient-record transaction ends under concurrency. So this attempt ran the full serial backend regression, the full frontend, repository-check and mocked-browser gates, the real-backend walkthroughs of every changed neighbour (completion, adoption, treatment-plans-complete, treatment-plans, billing, odontogram) and of the story and its parent, and negative controls for the new guards. The full 25-run walkthrough sweep is not required (no gate or milestone is being evaluated and no shared contract changed); the last full sweep was on item 36's R01 tree (266 of 266).

All on the final committed tree (implementation commit `486e44c621caf502def3555425d17d1adaa5fd71`), one machine, 2026-10-16. Raw output is under `artifacts/`.

## Gates, exact commands and results
| Gate | Command | Result (pass / fail / skip) | Where |
|---|---|---|---|
| Backend, full serial | `dotnet build --nologo` then, in `src/Alveara.Api.Tests`, `dotnet test --nologo --no-build --logger "trx;LogFileName=alv016c01_r02_full.trx"` (no parallelism, no rebuild during the run) | **3686 passed / 0 failed / 0 skipped** of 3686, 1 h 54 m (R01's run: 3675 of 3676; +10 new tests; the test that failed in R01 passed) | `artifacts/test-runs/backend-full-summary.txt`, `backend-full-output.txt` |
| The new tests | `dotnet test --filter "FullyQualifiedName~CompletionDeadlockTests"` | 10 passed; run three more times in a row, 10 of 10 each time | in the full run |
| The completion neighbours, targeted | `dotnet test --no-build --filter "FullyQualifiedName~Completion\|FullyQualifiedName~CompletedProcedure\|FullyQualifiedName~TreatmentPlan"` | 551 of 551 (ALV-008-C01, ALV-008-C02, STORY-015 and ALV-015-C01 classes) | `PARENT_REGRESSION.md` |
| Frontend unit | `npx vitest run` | **1453 passed / 0 failed / 0 skipped** of 1453 (93 files; unchanged) | `artifacts/test-runs/frontend-gates.txt` |
| Type check, build, lint | `npx tsc --noEmit`, `npm run build`, `npm run lint` | exit 0 each | same |
| Mocked browser | `npx playwright test` (default config) | **136 passed / 0 failed / 0 skipped** | same |
| Repository checks | `node --test tests/*.test.mjs` (repository root) | **8 passed / 0 failed / 0 skipped** | same |
| Real-backend walkthroughs (fresh database each, `sh run_e2e.sh <db> <config>`) | completion-adoption, completion, treatment-plans-complete, treatment-plans, billing, odontogram (the completion path and its neighbours), prescription-workflow and prescriptions (the story and its parent) | **8 + 11 + 11 + 11 + 10 + 11 + 15 + 10 = 87 passed / 0 failed / 0 skipped** | `artifacts/regression/walkthroughs-summary.txt`, `run-outputs/` |
| Leak check | `sqlcmd` counts of `AlveraTest%`, `AlveraRestore%` databases, `alveara_%` logins and databases | unchanged, 54 / 59 / 273 / 248 | `artifacts/test-runs/leak-check.txt` |
| Reproduction of the finding | `dotnet test --filter "FullyQualifiedName~CompletionDeadlockTests"` on the UNCHANGED code | failed with `InvalidOperationException > DbUpdateException > SqlException(1205)` (the first version of the engineered test, since split into tests 1 and 2; its own regression guard passed because an unforced race rarely deadlocks) | `artifacts/reproduction/reproduction-before-fix.txt` |
| Negative controls (proportionate: a new concurrency guard on a transactional path) | `python negctl_r02.py` (13 mutants of the classifier, the policy, the wiring, the message, the logging) | **13 accounted for**: 12 killed, 1 equivalent mutant documented | `artifacts/test-runs/negative-controls-r02.txt`, `docs/testing/COMPLETION_DEADLOCK_NEGATIVE_CONTROLS.md` |

## New backend tests (10), `CompletionDeadlockTests`
Real, deterministic deadlocks (see `HANDOFF.md` for the construction):
1. `A_completion_that_is_a_deadlock_victim_is_run_again_and_succeeds_exactly_once`
2. `A_completion_that_is_a_deadlock_victim_on_every_attempt_ends_in_save_failed_and_records_nothing`
3. `After_a_deadlock_the_retry_meets_the_winners_result_so_exactly_one_set_wins_and_nothing_is_duplicated`
4. `After_a_deadlock_a_double_submit_of_the_same_request_replays_the_saved_set_instead_of_recording_it_twice`
5. `An_adoption_that_is_a_deadlock_victim_is_run_again_and_succeeds_exactly_once`
6. `An_adoption_that_is_a_deadlock_victim_on_every_attempt_ends_in_save_failed_and_records_nothing`
7. `A_domain_refusal_is_not_retried_and_passes_through_unchanged`
8. `Racing_completions_of_one_plan_never_surface_a_provider_exception_however_the_server_orders_them` (a regression guard: it passed on the unchanged code too, since an unforced race rarely deadlocks; the engineered tests are the proof)
9. `The_classifier_recognises_a_real_1205_however_it_is_wrapped_and_nothing_else`
10. `The_policy_retries_only_a_deadlock_victim_a_bounded_number_of_times_and_wraps_the_last_failure_when_it_gives_up`

No frontend test, walkthrough or documentation check changed: the R01 prescription tests (167 backend, 30 frontend, 15 walkthrough) are part of the totals above and pass unchanged.
