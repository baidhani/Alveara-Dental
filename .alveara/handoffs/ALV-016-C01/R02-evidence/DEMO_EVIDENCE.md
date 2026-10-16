# ALV-016-C01 R02 - Demo evidence

**The visible prescription workflow is unchanged by R02.** The R01 demo (`R01-evidence/DEMO_EVIDENCE.md`: 15 of 15 real-browser tests, 14 screenshots, axe in light and dark with 0 blocking violations) was accepted by the review and no prescription screen, route or text changed. It was re-run on the R02 tree and passes: `artifacts/regression/run-outputs/prescription_workflow.txt` (15 of 15) and `prescriptions.txt` (10 of 10, STORY-016's own). The R01 screenshots remain in `R01-evidence/artifacts/walkthrough/` and `docs/testing/prescription-workflow-walkthrough/`.

## What R02 demonstrates
The change is a behaviour under concurrency with no new screen, so the demonstration is the engineered real deadlock rather than a picture:

| Step | What it shows | Where |
|---|---|---|
| Before the fix | A real SQL Server deadlock (a second session holds the billing accounts table while the completion is blocked inside its transaction, then closes the cycle) ends the completion with `InvalidOperationException > DbUpdateException > SqlException(1205)` | `artifacts/reproduction/reproduction-before-fix.txt` (produced by the first version of the engineered test, `A_real_deadlock_victim_during_a_completion_ends_in_a_domain_safe_outcome...`, since split into tests 1 and 2 below; it ran on the unchanged code) |
| After the fix: retried | The same deadlock, engineered once: the completion is run again and succeeds exactly once (1 set, 2 procedures, 2 charges); the retry is logged (`completion_deadlock_retry operation=completion attempt=1 max_attempts=3 error_class=DeadlockVictim`); the same key then replays | `CompletionDeadlockTests`, test 1 |
| After the fix: exhausted | The same deadlock engineered on all three attempts: `save_failed` 503 "the database was busy, so nothing was completed", no SQL words, and the plan, states, events, audit and ledger exactly as before | test 2 |
| After the fix: the winner | A competing request commits while the victim waits to retry: the victim ends in `concurrency_conflict` or `items_not_completable`, one set, two procedures, two charges, no procedure twice | test 3 |
| After the fix: a double submit | An identical request commits while the victim waits: the retry replays it and records nothing twice | test 4 |
| Adoption | The same retry and the same exhaustion for an adoption | tests 5 and 6 |

The completion, adoption, treatment-plans-complete, treatment-plans, billing and odontogram walkthroughs (real Chromium, real API, real LocalDB) pass on the R02 tree, 87 of 87 in all (`artifacts/regression/walkthroughs-summary.txt`).
