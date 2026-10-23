# ALV-N017 — Independent Review R02, Stage 1

**Decision:** APPROVED_PENDING_REGRESSION  
**Reviewed:** 2026-10-23  
**Handoff:** `Alveara_Handoff_ALV-N017_134fbdf_R02.zip`  
**Package SHA-256:** `6C746F9C6156CCF60902E9EDECA58BF59886C056714E4E4A6C41016FD03C58BA`  
**Implementation reviewed:** `134fbdfc77097638c936f45edeb0c86b7c386090`  
**Evidence reviewed:** `210689e3427c8595af719a00cc450172f0171255`

## Decision basis

R02 fully corrects `ALV-N017-R01-01` without changing production code: its `src/` tree is identical to R01. The Stage 2 evaluator no longer treats a backend duration as a pass/fail requirement. It continues to measure and report elapsed time while preserving every substantive regression control: exact full-TRX outcome and ID comparison, the 159-ID allowlist, template use/fallback/cleanup checks, client and repository gates, four real-backend walkthroughs, restore coverage, and the predeclared authorization-performance budget.

I independently ran `validate_evaluator.py`. All **9 of 9** declared R02 scenarios behaved correctly: an otherwise qualifying 80-minute synthetic run passes; a failed or skipped test, missing or changed baseline ID, missing allowlisted ID, or unexpected ID fails. The R01 control intentionally demonstrates the former defect by rejecting the 80-minute case.

The administrator's direct answer during a policy-store outage or degraded policy remains accepted. The approved design requires Admin to retain access so that an Admin can inspect and repair the policy; non-Admin decisions still fail closed.

## Frozen Stage 2 authorization

The R02 implementation is authorized for exactly one formal Stage 2 regression after the required closure-only control-record commit. The closure tree must be clean and differ from `134fbdfc77097638c936f45edeb0c86b7c386090` only as permitted by the handoff schema.

Run once, with no other test activity:

```sh
sh .alveara/handoffs/ALV-N017/R02-evidence/stage2/run_stage2.sh 134fbdfc77097638c936f45edeb0c86b7c386090 <out-folder>
```

The evaluator must qualify all frozen substantive requirements: **4,569 backend passed / 0 failed / 0 skipped**, exact ALV-N015 R02 baseline preservation plus the exact 159-ID allowlist, no template fallback, one template build, at least 3,000 clones, no leaks, TypeScript/build/lint clean, Vitest **1,553 in 98 files**, mocked Playwright **140**, repository checks **13**, real-backend walkthroughs **7, 10, 14, and 12**, and the predeclared p95 authorization budgets. Backend elapsed time is evidence only and is **not** a failure criterion.

Submit the regression package for final review. Do not mark ALV-N017 `COMPLETE` or begin ALV-N018 until final approval is recorded.
