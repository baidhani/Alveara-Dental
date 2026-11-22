# ALV-N026 R04 — Independent Code Review Before Restarted Targeted Proof

**Decision:** APPROVED_FOR_TARGETED_PROOF  
**Reviewed package:** `Alveara_Handoff_ALV-N026_884a936_R04.zip`  
**Package SHA-256:** `2385A9F484ED241BD51A28AD88F41760518A2084148DBCD3701128EDD41F111D`  
**Implementation SHA:** `884a936464414d760242fa8698237bff7f071772`

This approval covers the R04 shared walkthrough-runner correction and the focused-proof restart. It is not final story approval, does not set `COMPLETE`, and does not authorize broad regression.

## Independent checks

- ZIP member hashes validate against `MANIFEST.json`; the stated implementation SHA exists locally.
- The two stopped runs are retained distinctly. Both passed P1 (64/64) and P2 (119/119), then stopped in W1; W2–W4 were correctly not run after the failure.
- The R03 helper established that the real W1 failure was an HTTP 503 from the PDF route. The API's PDF readiness contract requires both the engine executable and pinned-font directory; the walkthrough runner previously supplied neither configuration value.
- R04 passes `Pdf__EngineDirectory` and `Pdf__FontDirectory` from the established Test-Data PDF-engine location to the API process, matching the existing API-test and development-host configuration. It also fails before launching the walkthrough API when `typst.exe` is absent.
- The runner change is additive infrastructure configuration only. It does not alter product behavior, the browser assertions, test selection, or the prior failure evidence.
- `implementation.diff` legitimately excludes `.alveara/` control/evidence files to avoid repeated artifact content while the package retains those files separately. The scope and all ten exact secret-scan exceptions are disclosed; no generic credential-pattern exception is allowed.

## Preflight and authorized restart

Before the first run, confirm read-only that the configured Test-Data engine directory contains `typst.exe` **and** its `fonts` directory. If either is absent, stop before launching W1 and report the provisioning problem; do not substitute a system engine or another font source.

Then restart the frozen focused proof on implementation-equivalent code rooted at `884a936`:

1. P1 — prescription/PDF focused server group.
2. P2 — forms, signed-form snapshot, and document focused server group.
3. W1 — prescription-workflow server-PDF browser walkthrough.
4. W2 — missing/damaged-document browser walkthrough.
5. W3 — document-library signed-form/PD-24 browser walkthrough.
6. W4 — signed-form integrity and roles browser walkthrough.

Each run is independent and must stay within the five-minute boundary. Stop and ask the owner if an individual run exceeds five minutes. A failure or a needed source/test change stops the proof and preserves its evidence.

Do not run a full backend suite, broad browser suite, full chain, or broad regression. Those remain deferred to ALV-N028.

## Required next package

If all six focused runs pass, submit an additive focused-proof evidence package for implementation `884a936`. It must distinguish R02/R03 stopped evidence from the R04 successful proof and include exact commands, durations, results, database counts, browser artifacts, acceptance map, code-equivalence check, retained findings/limitations, and the ten exact secret-scan exceptions.

ALV-N026 remains `AWAITING_REVIEW` pending that evidence and a final independent ruling.
