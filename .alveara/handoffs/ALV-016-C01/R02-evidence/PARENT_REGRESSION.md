# ALV-016-C01 R02 - Parent regression, and the repair's effect on ALV-008-C01

**Result: verified.**

## The parent, STORY-016
Unchanged since R01 (`R01-evidence/PARENT_REGRESSION.md` maps its three acceptance criteria and the interaction between the two prescribing paths). R02 changes no prescription code. STORY-016's 185 backend tests, 31 screen tests and 10 real-browser tests pass on the R02 tree: the full serial backend run (3686 of 3686), the full vitest run (1453 of 1453) and the `prescriptions` walkthrough (10 of 10).

## The effect of the repair on ALV-008-C01's completion contract (the review asked for this mapping)

**Decision: ALV-008-C01 is not reopened.** The repair changes no request or response shape, route, permission, schema, rule, message or status code, with one exception that is not a contract change: a completion or an adoption chosen as a deadlock victim used to leave the service as an unhandled provider exception (an outcome no contract documents) and now runs again a bounded number of times and then ends in `save_failed` (503), which ALV-008-C01 already documents as "If it fails: nothing is stored (one transaction); the caller is told so (`save_failed`, 503) and may repeat the same request" (`docs/COMPLETION.md`, failure-first note 1). No stop condition, Done Means or approved decision of ALV-008-C01 or ALV-008-C02 is weakened, and the three verified-complete stories' own tests pass unchanged.

| ALV-008-C01 stop condition (`.alveara/handoffs/ALV-008-C01/R01-evidence/ACCEPTANCE_EVIDENCE.md`) | Effect of the repair | Proof that it still holds |
|---|---|---|
| 1. Original STORY-008 tests still pass | none | `TreatmentPlan*` and clinical-history tests pass (551 of 551 in the targeted run; the full regression) |
| 2. One or multiple selected planned procedures complete in one authoritative operation | none on the success path: a first-attempt success is byte for byte the same operation (the policy only wraps it) | `CompletedProcedureServiceTests`, `CompletedProcedureEffectsTests`, the `completion` walkthrough 11 of 11 |
| 3. Completed procedures remain linked to the exact source plan and version | none | `CompletedProcedureSchemaTests`, `CompletedProcedureServiceTests`, the walkthrough |
| 4. The odontogram updates when applicable | none | `CompletedProcedureEffectsTests`, the `odontogram` walkthrough 11 of 11, the completion walkthrough |
| 5. Exactly one linked charge per billable completed procedure | none; strengthened for the retry path: after a deadlock the retried set has exactly one charge per procedure (`(1 set, 2 procedures, 2 charges)` asserted), and a victim on every attempt leaves none | `CompletedProcedureEffectsTests`, the `billing` walkthrough 10 of 10, `CompletionDeadlockTests` 1, 2, 3 |
| 6. Duplicate or retry does not double-complete or double-charge | **strengthened**: the retry the repair adds is itself idempotent (it re-reads and re-checks the caller's key and version) | `CompletedProcedureServiceTests` (replay, key conflict, simultaneous sets), `CompletionDeadlockTests` 1 (replay after a retry), 3 (an overlapping winner leaves exactly one winner), 4 (an identical double submit replays) |
| 7. A failed submitted completion set leaves no mixed authoritative clinical or financial state | **strengthened**: a deadlock is a failure mode that is now proved to leave nothing | `CompletedProcedureServiceTests` (failed chart, failed charge, failed log), `CompletionDeadlockTests` 2 and 6 (a victim on every attempt: the plan, states, events, audit and ledger are exactly as before) |

## ALV-008-C02 (adoption)
The same repair covers `AdoptAsync` (the other operation that records a set in one transaction). ALV-008-C02's stop conditions (retirement of the legacy routes, adoption with its reason, one linked charge, reconciliation, the chart evidence) are unchanged: `CompletedProcedureAdoption*`, `CompletionAdoption*`, `CompletionChartEvidenceTests` and `CompletionReconciliationTests` pass; the `completion-adoption` walkthrough passes 8 of 8; `CompletionDeadlockTests` 5 and 6 prove an adoption victimised once is retried exactly once and one victimised every time records nothing.

## Dependency regression
- `ALV-N011` (safety), `ALV-005-C01` (clinical documentation), `ALV-002-C01` (concurrency and audit primitives): no file changed; the new `DeadlockRetry.cs` sits beside `ConcurrencySaveGuard` and does not alter it.
- The treatment-plans (STORY-015, ALV-015-C01) and billing (STORY-007, ALV-007-C01, ALV-007-C02) walkthroughs that touch the completion path pass (`treatment-plans` 11 of 11, `treatment-plans-complete` 11 of 11, `billing` 10 of 10).
