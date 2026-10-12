# ALV-007-C02 R01 - Test results

All on the final committed tree (implementation commit `bc25a94ae50639a5a46509c443ebf65b8f0992ab`), one machine, 2026-10-12. Raw output is under `artifacts/`.

| Gate | Result | Where |
|---|---|---|
| Backend, full serial (`dotnet test`, no parallelism) | **3195 of 3195** passed, 0 failed, 0 skipped, 1 h 1 m. 160 new tests. Earlier runs of this attempt: 3193 of 3193, 3194 of 3194 and one 3194 of 3195 whose single failure was an intermittent test of my own (see below and `backend-full-summary.txt`). | `artifacts/test-runs/backend-full-summary.txt` |
| Frontend unit tests (vitest) | **1387 of 1387** (90 files; 14 new) | `artifacts/test-runs/frontend-gates.txt` |
| `tsc --noEmit`, lint, build | exit 0 each | same |
| Mocked browser tests (Playwright, default config) | **136 of 136** | same |
| Repository checks (`node --test tests/*.test.mjs`) | **8 of 8** | same |
| Real-backend walkthrough, new (`billing-statements`) | **9 of 9** (axe light and dark: 0 critical/serious on 6 scanned states) | `artifacts/regression/statements-run-output.txt`, `artifacts/walkthrough/` |
| Real-backend sweep on fresh databases | **25 runs, 267 of 267** (the existing 24 runs 258, plus the new walkthrough 9). In the final sweep one run (odontogram longitudinal) failed once on a 5-second page-load timeout while the machine was slow and passed 11 of 11 on two fresh reruns; both outputs are in `artifacts/regression/` | `artifacts/regression/` |
| Leak check | databases and logins unchanged, 54 / 59 / 273 / 248 | `artifacts/test-runs/leak-check.txt` |
| Migration rollback and roll-forward | tested (`BillingRefundMigrationTests`, `CompletedProcedureMigrationTests`); `has-pending-model-changes`: none | `artifacts/test-runs/migration-reversibility.txt` |
| Negative controls | **25 of 25 killed** on the final tree; two survived the first pass and are closed by new tests | `artifacts/test-runs/negative-controls-c02-*.txt` |

## New backend tests (160)
`BillingRefundSchemaTests` 60 (the database on its own), `BillingRefundMigrationTests` 1, `BillingRefundMathTests` 10, `BillingRefundServiceTests` 25, `BillingDocumentBuilderTests` 15, `BillingDocumentServiceTests` 18, `BillingTimelineAgingTests` 3, `BillingStatementsApiTests` 22, `BillingRefundConcurrencyTests` 5, `BillingReconciliationTests` 1 (70 real transactions).

## Things the verification itself caught (all fixed before the final runs)
- **A real defect in an earlier story's trigger.** ALV-007-C01's trigger 51141 refused to reverse a charge while any allocation from a live payment existed, even when every cent of it had been refunded or moved away. The reconciliation walk hit it on its first run. The trigger now counts an allocation net of its releases (in this migration, with a schema test and a rollback assertion).
- **Two surviving negative controls** (N06 and N12), each a missing test, now killed.
- **An intermittent test of my own**: `Live_aging_follows_a_later_amendment...` amended "the first" of two procedures that one completion set finalizes at the same instant and so picked the wrong one about half the time; it failed in one full run (3194 of 3195) and is now deterministic (10 of 10 alone; the seven new classes three times in a row, 99 of 99 each).
- **A stale-preview message bug in the new component**, found by its unit test: the message was cleared by the re-preview it started.
- **Two dark-theme contrast failures** found only by the real browser (axe): the timeline's time stamps (CSS used custom properties that do not exist in this app) and the CSV link (plain links need the app's theme-aware link class).
- **Two parent walkthroughs failed in the first sweep** because the new panel reused the CSS class that they use to find the status line; fixed in the panel, the parent specs are unchanged.
- **The default mocked run picked up the new real-backend spec** until it was added to `testIgnore`.
- A first-run list of test mistakes of my own (a miscounted expectation, a wrong selector, a copied setup that omitted a field, an assertion on a number the code was right about); none was a product defect.
