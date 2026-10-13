# ALV-007-C02 R02 - Test results

All on the final committed tree (R02 implementation commit `19c8f1ca7cb5b3323cd93abbb7bd5a863da4d1d9`, on top of R01's `bc25a94ae50639a5a46509c443ebf65b8f0992ab`), one machine, 2026-10-12. Raw output is under `artifacts/`.

| Gate | Result | Where |
|---|---|---|
| Backend, full serial (`dotnet test`, no parallelism) | **3206 of 3206** passed, 0 failed, 0 skipped, 1 h 6 m. 171 new tests in the story (R01's 160 plus 11 for the finding). R01's final run was 3195 of 3195. | `artifacts/test-runs/backend-full-summary.txt` |
| Frontend unit tests (vitest) | **1391 of 1391** (90 files; 18 new in the story, 4 of them for the finding) | `artifacts/test-runs/frontend-gates.txt` |
| `tsc --noEmit`, lint, build | exit 0 each | same |
| Mocked browser tests (Playwright, default config) | **136 of 136** | same |
| Repository checks (`node --test tests/*.test.mjs`) | **8 of 8** | same |
| Real-backend walkthrough, new (`billing-statements`) | **9 of 9** (axe light and dark: 0 critical/serious on 6 scanned states) | `artifacts/regression/statements-run-output.txt`, `artifacts/walkthrough/` |
| Real-backend sweep on fresh databases | **25 runs, 267 of 267** (the existing 24 runs 258, plus the new walkthrough 9). Every run passed first time in the final sweep (the walkthrough now also opens a charge's source as the billing user) | `artifacts/regression/` |
| Leak check | databases and logins unchanged, 54 / 59 / 273 / 248 | `artifacts/test-runs/leak-check.txt` |
| Migration rollback and roll-forward | unchanged by R02 (no migration or model change); `BillingRefundMigrationTests` and `CompletedProcedureMigrationTests` pass in the full run | `artifacts/test-runs/migration-reversibility.txt` |
| Negative controls | **29 of 29 killed**: the 25 of R01 plus the source route's permission (N26), its patient scoping (N27), its charge-only rule (N28) and, in the UI, the gate on the clinical link (N29) | `artifacts/test-runs/negative-controls-c02-r02.txt`, `...-r02-ui-gate.txt` (R01's first-pass logs are kept for history) |

## New backend tests (171 in the story; 11 in R02)
`BillingRefundSchemaTests` 60 (the database on its own), `BillingRefundMigrationTests` 1, `BillingRefundMathTests` 10, `BillingRefundServiceTests` 25, `BillingDocumentBuilderTests` 15, `BillingDocumentServiceTests` 18, `BillingTimelineAgingTests` 3, `BillingStatementsApiTests` 22, `BillingRefundConcurrencyTests` 5, `BillingReconciliationTests` 1 (70 real transactions), and in R02 `BillingChargeSourceApiTests` 11.

## The review finding (R02)
- **Reproduced first.** `BillingChargeSourceApiTests` was written against the unchanged code and all 11 tests failed because the authorized destination did not exist; they pass with the fix. Two first-run mistakes in those tests were my own (a manual charge for a procedure the product's own completion had already charged, which is a 409; an assertion that hid the response body).
- **Fixed only that.** No other behaviour changed; the parent and R01 suites pass unchanged.

## Things the verification itself caught in R01 (all fixed before R01's final runs)
- **A real defect in an earlier story's trigger.** ALV-007-C01's trigger 51141 refused to reverse a charge while any allocation from a live payment existed, even when every cent of it had been refunded or moved away. The reconciliation walk hit it on its first run. The trigger now counts an allocation net of its releases (in this migration, with a schema test and a rollback assertion).
- **Two surviving negative controls** (N06 and N12), each a missing test, now killed.
- **An intermittent test of my own**: `Live_aging_follows_a_later_amendment...` amended "the first" of two procedures that one completion set finalizes at the same instant and so picked the wrong one about half the time; it failed in one full run (3194 of 3195) and is now deterministic (10 of 10 alone; the seven new classes three times in a row, 99 of 99 each).
- **A stale-preview message bug in the new component**, found by its unit test: the message was cleared by the re-preview it started.
- **Two dark-theme contrast failures** found only by the real browser (axe): the timeline's time stamps (CSS used custom properties that do not exist in this app) and the CSV link (plain links need the app's theme-aware link class).
- **Two parent walkthroughs failed in the first sweep** because the new panel reused the CSS class that they use to find the status line; fixed in the panel, the parent specs are unchanged.
- **The default mocked run picked up the new real-backend spec** until it was added to `testIgnore`.
- A first-run list of test mistakes of my own (a miscounted expectation, a wrong selector, a copied setup that omitted a field, an assertion on a number the code was right about); none was a product defect.
