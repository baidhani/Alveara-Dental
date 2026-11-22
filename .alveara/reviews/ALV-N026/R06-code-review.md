# ALV-N026 R06 — Independent Code Review Before Remaining Focused Proof

**Decision:** APPROVED_FOR_TARGETED_PROOF  
**Reviewed package:** `Alveara_Handoff_ALV-N026_d2241c8_R06.zip`  
**Package SHA-256:** `634E84559C554BB711085536A94D89175B9FF465D334EB412AC85B2CB5280522`  
**Implementation SHA:** `d2241c8b5773c24757d32d8100ef4af39a041493`

This approval covers the test-only More-menu repair and the remaining focused proof. It is not final story approval, does not set `COMPLETE`, and does not authorize broad regression.

## Independent checks

- ZIP member hashes validate against `MANIFEST.json`.
- P1 (64/64), P2 (119/119), and W1 (15/15) are valid carried-forward evidence. The PDF flow is now proven through the real browser: 200 `application/pdf`, new-tab/download arrival, no application-page print, and a valid authenticated PDF fetch despite Playwright's zero-byte intercepted-body limitation.
- W2 stopped only because the existing test still clicked a row action that S2 had moved under the approved More menu. The page artifact showed the menu exactly where the implementation specifies; it did not indicate a product defect.
- R06 adds an idempotent `openMore` helper in both affected document walkthroughs and uses it before every affected Edit, Inactivate, and Reactivate operation. The action inventory was swept; Preview, Open, and Download correctly remain direct row actions.
- No API, migration, server test, script, or client source changed since the carried-forward evidence. R06 changes the two browser specs only.

## Authorized proof sequence

1. Run W2 (documents library) on R06.
2. If W2 passes, run W3 (documents) on the same implementation-equivalent tree.
3. If W3 passes, run W4 (forms) on the same tree.

Do not rerun P1, P2, or W1. Each run must remain within five minutes; if one exceeds that boundary, stop safely and request owner direction. If any run fails or needs a source/test change, stop and preserve evidence for a new review attempt.

No full backend suite, broad browser suite, full chain, or broad regression is authorized. Those remain deferred to ALV-N028.

## Required final focused-proof package

If W2–W4 pass, submit additive focused-proof evidence for `d2241c8`: exact commands, durations, counts, database before/after counts, W2–W4 browser artifacts, acceptance map, code-equivalence evidence, all preserved stopped-run evidence, thirteen exact secret-scan exceptions, retained findings, and limitations. The final independent review will then rule on closure.

ALV-N026 remains `AWAITING_REVIEW` pending that package.
