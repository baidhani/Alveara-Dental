# ALV-N017 — Independent Review R01, Stage 1

**Decision:** CHANGES_REQUIRED  
**Reviewed:** 2026-10-23  
**Handoff:** `Alveara_Handoff_ALV-N017_266d1e3_R01.zip`  
**Package SHA-256:** `51B79F349C530EC2660C723CDD976385B78712774ED727F2E822E32B832DABB8`  
**Implementation reviewed:** `266d1e3c5f5ce6724e0c4a07d25aa4889ac6e687`  
**Evidence reviewed:** `fbfe2a969cf6384024b74840579777b73e4edcff`

## Review result

The implementation and focused evidence are strong. I independently ran `AuthorizationArchitectureTests`: **5 passed, 0 failed, 0 skipped**. The administrator's direct effective-permission answer during a policy-store outage or an unattributable invalid row is **accepted**: it is required by the approved design's explicit “Admin keeps access by invariant, so an Admin can repair” rule. No product-code change is required for that behavior.

R01 cannot be approved for Stage 2 because its frozen evaluator adds an unapproved, inappropriate backend time failure condition.

## Required correction

### ALV-N017-R01-01 — Stage 2 incorrectly imposes the ALV-N016 55-minute qualification ceiling

**Severity:** blocking execution-control defect; no product defect found

Item 43 of the authoritative Execution Plan requires one formal full Stage 2 regression but sets **no backend duration ceiling**. The package's own evaluator acknowledges that fact, yet `stage2/evaluate_stage2.py` still contains `CEILING_SECONDS = 55 * 60` and fails a run above that threshold.

That 55-minute requirement belonged to the ALV-N016 test-infrastructure qualification. It is not a general performance requirement for later high-risk product stories. The most recent qualified product Stage 2, ALV-N015 R02, took 53.6 minutes before ALV-N017 adds 159 backend test IDs and additional performance evidence. Retaining an inherited 55-minute failure condition risks rejecting a correct full run merely because this larger suite takes longer, then wasting another full run after no product change.

## R02 scope and proof required

1. Remove the 55-minute ceiling as a pass/fail criterion from the ALV-N017 Stage 2 evaluator, its handoff/test-results language, and any manifest or summary expectation. Keep elapsed time measured and reported as operational evidence.
2. Preserve every substantive Stage 2 qualification: clean equivalent closure tree, full serial backend TRX, **4,569 passed / 0 failed / 0 skipped**, exact comparison to the 4,410-test ALV-N015 R02 baseline plus the explicit 159-ID allowlist, template/fallback/clone/cleanup checks, frontend and repository gates, all four walkthroughs, restore drill, and the predeclared authorization performance evidence.
3. Add a focused evaluator validation proving a backend result over 55 minutes with every substantive criterion met is accepted, while a failed backend test or any baseline/allowlist mismatch is still rejected. Do not alter production behavior or broaden the test suite.
4. Repackage as a new Stage 1 attempt with `fullRegression: "PENDING"`. Do **not** run the formal Stage 2 regression for R01 or R02 before the corrected contract receives `APPROVED_PENDING_REGRESSION`.

No Execution Plan amendment is required: item 43 already has the correct policy (mandatory Stage 2 with no time limit). The R02 handoff/evaluator must conform to it.
