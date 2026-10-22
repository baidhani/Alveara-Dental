# Decision requested from the reviewer - ALV-N016 R04 / ALV-007-C01 R03

Both resubmissions are Stage 1 packages with **no source change** (the backend and the template and billing work are byte-identical to the failed shared run; the Command Center correction is the separate, approved `ALV-CONTROL-C01`).

**Question: how must the shared-regression evidence be revalidated?**
- **Option B (frozen as the default):** one new physical shared full run (about 38 minutes of machine time: backend about 34 minutes plus the client gates), judged by the two updated evaluators. Nothing from the failed run is reused.
- **Option A (requires the reviewer's approval):** re-use the preserved backend, performance and template result of the failed run (it measured everything on byte-identical code: 4,324 of 4,324, 34.2 minutes against 55, 0 fallbacks, 3,289 clones, 32 real-path fixtures, ledger tests and D5 passed) and re-qualify only the Command Center with a fresh frontend gate (about 4 minutes), with the equivalence decided by `stage2/evaluate_reuse.py`.

I will run whichever the reviewer states in the Stage 1 decision; without a statement I will run Option B.
