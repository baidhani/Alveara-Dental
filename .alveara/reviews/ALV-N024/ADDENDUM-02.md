# ALV-N024 — addendum 02 independent review

**Package:** `Alveara_Addendum_ALV-N024_c7fb422_02.zip`  
**SHA-256:** `1E9DF6018D481D9D811B78289EAC00580B2408889A2E80858A6765D94EF1BC9B`  
**Decision:** **APPROVED — proceed to final implementation commit, focused sweep, and R01 handoff.**

## Ruling

The addendum correctly repairs the evidence contract without changing production behavior.

- `READCOMMITTEDLOCK` remains in the approved trigger, but its necessity is no longer claimed. The one direct-race mutation that removed it passed 20 rounds; that result is evidence that the prior necessity rationale was unproven, not evidence that the receipt rule is unsafe.
- The receipt invariant remains the transaction-owned, payment-keyed application lock plus guard 51295. Control (f) is retained as an observation only and is no longer a required failing negative control.
- Control (e) is now meaningful: after a deliberately removed service lock, it catches the altered refusal behavior **after** asserting the receipt count, audit count, and transaction-lock cleanup. The database trigger still preserves the one-receipt invariant; that is the intended layering proof.
- Controls (a) through (d) have also been interpreted correctly. Their expected failures show the named service/format/property boundary is detected; no claim is made that a database trigger alone proves a client-preview rule.

## Required carry-forward

Before the final R01 handoff is built, record the already-decided Q-5ag privacy outcome in the story documentation and handoff: **do not add `performed by` to billing views.** Billing continues to show the charge and its recording actor only; the clinical record remains the authorized place for the performing provider. This supersedes the contrary Q-5ag line in briefing R02. No implementation or test for displaying the provider is authorized.

## Evidence boundary

Proceed with the complete focused Stage 1 sweep and R01 package. Keep each command/group within the five-minute policy and do not run a broad/full suite or package-level Stage 2 regression. The partial files included in this addendum are not independently approved as a completed implementation; the final R01 review will assess the frozen complete implementation and its evidence.

