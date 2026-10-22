# Test results - ALV-007-C01 R02 (Stage 1)

**Cadence classification: HIGH RISK (money/billing, concurrency primitive).** A Stage 1 package: **the formal Stage 2 broad regression is `PENDING` and has not been run.** The next broad-regression story after this one is `ALV-N016` R03 (its own Stage 2). Implementation `37d2b08420d1b24837052aafddb8994bd186d6da`.

## Commands and results (this session, on `37d2b08`, test project rebuilt before each run)

| # | Command (from `src/Alveara.Api.Tests`) | Result |
|---|---|---|
| 1 | `dotnet test --no-build --filter "FullyQualifiedName~BillingLedgerReadConsistencyTests"` | **5 of 5 passed** |
| 2 | the same on the previous ledger code (negative control, `artifacts/negative-control/`) | **4 of 5 fail with `KeyNotFoundException`**: the defect is reproduced deterministically and fixed |
| 3 | `dotnet test --no-build --filter "<Billing, Ledger, GateD, GateC, ProcedureCharge, Financial>"` (filter in `artifacts/test-runs/filters.txt`; 409 tests listed first) | **409 of 409 passed**, 7 m 08 s (`artifacts/test-runs/ledger-focused-409.csv`) |
| 4 | `dotnet test --no-build --filter "<TreatmentPlanComplete, TreatmentPlanCompletion, TreatmentPlanLifecycle, CompletedProcedure, Completion, RetiredCompletionCallerSearch, Prescription, Document>"` (1,379 tests listed first) | **1,379 of 1,379 passed**, 16 m 07 s (`artifacts/test-runs/descendants-1379.csv`) |

The previously flaky `GateDHistoryIntegrityTests.D5_financial_history_survives_duplicate_submissions_corrections_stale_edits_and_direct_overwrite` passed in run 3.

## New tests (5, `BillingLedgerReadConsistencyTests`)
A competing payment is committed between the entries query and the allocations query (a command interceptor on the reading context, so the formerly inconsistent window happens every time):
1. the stale writer gets `ConcurrencyConflictException`, exactly one payment is recorded (the winner's), the balance is 95.00 and the ledger verifies clean;
2. the loser's retry with the fresh version is recorded once and the same key again changes nothing (idempotency preserved);
3. a reader that meets the payment mid-read repeats the read and returns one coherent account;
4. a reader that never gets a stable picture (a payment in every window) is refused with the conflict after exactly 4 attempts, never an endless loop, and the ledger verifies clean;
5. an account with no billing yet reads as empty (boundary).

## Failure-first answers (for the changed component)
1. **What happens if the read is inconsistent?** It is detected by the account row version and repeated.
2. **Retry?** Immediate, up to 4 attempts, no backoff (a read-only repeat of three small queries).
3. **If retries are exhausted?** The caller gets `ConcurrencyConflictException` (HTTP 409 via the existing handling) and reloads; nothing is stored.
4. **Handled:** a recording by another request during the read. **Not handled:** a database outage (the exception reaches the caller unchanged); a writer that changes the ledger without moving the account row version (none exists: every code path that adds an entry, allocation or release calls `Touch` or `Open`, and the schema's triggers keep the history append-only).

## Frozen Stage 2 contract (run once after `APPROVED_PENDING_REGRESSION`)
`stage2/run_stage2.sh <implementation-sha> <out-folder>` on the closure-only tree, nothing else running, clean tree; `stage2/evaluate_stage2.py` decides every line:
1. run commit is the Stage 1 closure commit, tree clean before and after, no `src/` file differs from `37d2b08`, `ALVERA_TEST_TEMPLATE` unset;
2. `dotnet test` of the whole backend: exit 0, **4,324 passed, 0 failed, 0 skipped** (the 4,300 of the ALV-010-C01 baseline + 19 ALV-N016 template tests + these 5); a two-hour sanity bound on elapsed time (this story has no time criterion);
3. all 4,300 baseline ids present and Passed, exactly 24 new ids; the 5 new tests and `D5_financial_history...` present and Passed;
4. zero template fallback, no template build failure;
5. Storage, `Keys`, `Backup`, `Database`, `Temp` counts, databases, logins and template backups unchanged;
6. `npx tsc --noEmit`, `npx vitest run` (1,544 in 97 files), `npm run build`, `npm run lint`, `npx playwright test` (136), `node --test tests/*.test.mjs` (8): all pass (no client file changed);
7. any failed line means `CHANGES_REQUIRED` and a new attempt.
Note: the tree also contains the ALV-N016 R02 template code (already committed on `main`); the contract's counts include its 19 tests.

## Disclosures
- The ALV-N016 R02 package disclosed an unplanned full run in which this defect was observed; no full run was made for this story.
- Three older ALV-010-C01 commits carry a `Co-Authored-By` trailer against the owner's instruction (not rewritten); none of this story's commits has one.
- Test-hook note: my first interceptor matched the idempotency lookup instead of the ledger's entries query, so two tests passed on the old code; this was found by the negative control and fixed before the evidence above.
- An earlier focused run (409) was invalid because the test project had been left built from the old code after the negative control; it was discarded and rerun after a rebuild.
