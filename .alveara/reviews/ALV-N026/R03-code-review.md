# ALV-N026 R03 — Independent Code Review Before Restarted Targeted Proof

**Decision:** APPROVED_FOR_TARGETED_PROOF  
**Reviewed package:** `Alveara_Handoff_ALV-N026_a018c7e_R03.zip`  
**Package SHA-256:** `C5A0E98FC82E95CA4066A38E0F6110A0695ED5453DE35DAA0683789B62E431E2`  
**Implementation SHA:** `a018c7e9d2cb35eb0e7775c4d194c1779e2dab48`

This decision approves the R03 test-only correction and the restart of the frozen focused proof. It is not final approval, does not set `COMPLETE`, and does not authorize broad regression.

## Independent checks

- ZIP member hashes validate against `MANIFEST.json`; the implementation SHA exists locally.
- The stopped R02 proof is preserved. P1 passed 64/64, P2 passed 119/119, W1 stopped after 7 passed and 1 failed, and W2–W4 were not run. The test-database before/after counts remain unchanged.
- The W1 artifact shows the server issued the record-copy request and served the PDF before the helper failed to observe a persistent tab/download. It is not discarded as an environmental result.
- R03 changes only `pdfFromClick` in the real-browser prescription walkthrough. It now observes every new tab, including one that closes, listens for downloads on both the initiating page and each new tab, records arrival evidence, and waits briefly for the new-tab download event.
- The substantive checks remain: UI click, issue POST, PDF GET, PDF content type and signature, no application-page `window.print()`, and no raw application address/error detail in the PDF response. The correction therefore fixes observation rather than lowering the safety or privacy assertion.
- The secret-scan allowlist is appropriately exact: the three earlier approved code lines and the four specified throwaway fixture-password lines quoted by Playwright in the immutable stopped-run `error-context.md`. No general password-pattern exception is approved.

## Authorized proof restart

Restart the frozen R02 focused proof on implementation-equivalent code rooted at `a018c7e`:

1. P1 — prescription/PDF focused server group.
2. P2 — forms, signed-form snapshot, and document focused server group.
3. W1 — prescription-workflow server-PDF browser walkthrough.
4. W2 — missing/damaged-document browser walkthrough.
5. W3 — document-library signed-form/PD-24 browser walkthrough.
6. W4 — signed-form integrity and roles browser walkthrough.

Each is an independent run expected to remain at or below five minutes. Stop an individual run at a safe boundary and request owner direction if it exceeds five minutes. Stop the proof and preserve evidence if any run fails or a source/test change becomes necessary.

Do not run a full backend suite, full Vitest suite, broad mocked-browser suite, full chain, or any broad Stage 2 regression. Those remain deferred to ALV-N028.

## Required next package

If all six focused runs pass, provide an additive focused-proof evidence package for this same implementation SHA. Include exact commands, durations, pass/fail/skip counts, before/after database counts, browser artifacts, the acceptance map, the code-equivalence check, and all retained limitations/findings. It must clearly distinguish the preserved failed R02 W1 evidence from the successful R03 rerun.

ALV-N026 remains `AWAITING_REVIEW` pending that evidence and the final independent ruling.
