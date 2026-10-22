# Acceptance evidence - ALV-007-C01 R02 (corrective attempt)

The approved ALV-007-C01 stop conditions (R01 approval, `.alveara/reviews/ALV-007-C01/R01.md`) must still hold; R02 adds the reviewer's corrective conditions from `.alveara/reviews/ALV-N016/R02.md`.

| # | Condition | Evidence |
|---|---|---|
| 1 | Original STORY-007 tests still pass | in the 409 and 1,379 runs (`BillingServiceTests`, `BillingApiTests`, `BillingSchemaTests`); the Stage 2 backend run repeats all of them |
| 2 | Ledger balance reconstructs from preserved authoritative transactions | `BillingLedgerMathTests`, `BillingReconciliationTests`, `D5` verification; the new tests assert `VerifyLedgerAsync` has no findings after the race |
| 3 | Payments/adjustments cannot silently rewrite original financial history | `BillingLedgerSchemaTests` and the D5 direct-overwrite attacks pass; no schema or trigger changed |
| 4 | Duplicate command cannot create duplicate charge/payment | new test 2 (same key again changes nothing); `BillingLedgerServiceTests` idempotency tests pass |
| 5 | Guarantor responsibility independent of patient identity | `BillingResponsibilityServiceTests` pass; untouched |
| 6 | A tested contract exists for ALV-008-C01 to create one linked billable charge | `BillingProcedureChargeContractTests` pass; untouched |
| 7 | Money/rounding consistent with ALV-N002 | `BillingLedgerMathTests`, `BillingRefundMathTests` pass; untouched |
| R1 | Coherent ledger read under concurrent payment/allocation activity (reviewer point 2) | `LoadLedgerAsync` brackets its three reads with the account row version and repeats; new tests 1 and 3 |
| R2 | Established idempotency, optimistic-concurrency, financial-history, transaction, audit and money invariants preserved (point 2) | no write path, rule, schema or contract changed (see `CHANGED_FILES.md`); D5 (duplicate submissions, stale edits, direct overwrite) and the 409-test run pass |
| R3 | Deterministic regression test forcing the formerly inconsistent window; one valid operation succeeds, the concurrent one gets the controlled conflict, integrity preserved (point 3) | `BillingLedgerReadConsistencyTests` (5), fails 4 of 5 on the old code, passes on the fix |
| R4 | Completed descendants assessed and marked `REVALIDATION_REQUIRED` with targeted evidence (point 4) | `REVALIDATION_ASSESSMENT.md`; 1,379 of 1,379 |
| R5 | Stage 2 frozen, not run before approval (point 5 and the two-stage policy) | `stage2/`, `fullRegression: PENDING` |
