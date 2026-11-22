# ALV-N026 R05 — Independent Code Review Before W1 Focused Proof

**Decision:** APPROVED_FOR_TARGETED_PROOF  
**Reviewed package:** `Alveara_Handoff_ALV-N026_aa87419_R05.zip`  
**Package SHA-256:** `4C3D9C31625DF67637FCF08A38DA5408579155DCB6D039C6296082168EE37B11`  
**Implementation SHA:** `aa874191e6014f472f00bee48735e8d150b68d91`

This is a targeted-proof authorization only. It is not final story approval, does not set `COMPLETE`, and does not authorize broad regression.

## Independent checks

- ZIP member hashes validate against `MANIFEST.json`; the stated implementation SHA exists locally.
- R04 preflight, P1 (64/64), and P2 (119/119) are valid carried-forward evidence. The R05 delta is confined to the W1 end-to-end specification; no API, migration, server test, script, or client source changed.
- The original browser action remains mandatory: it must create the issue, receive a 200 `application/pdf` browser-side response, reach a new tab or download, and never call `window.print()` on the application page.
- The intercepted response's content length and observed bytes are now diagnostics only. The same already-issued PDF is fetched once through the same signed-in session and is required to be a non-empty PDF beginning `%PDF-`, without app-address or raw-error leakage. A failure of that authenticated fetch remains a product failure.
- The thirteen secret-scan exceptions remain exact package path/line/text exceptions only. No broad secret-pattern exception is approved.

## Authorized proof sequence

1. Run **W1 only** on the R05 implementation.
2. If W1 fails, stop, preserve the evidence, and return for review. Do not make an unreviewed correction or run W2–W4.
3. If W1 passes, run W2, then W3, then W4, separately. Each run remains subject to the five-minute stop-and-ask boundary.

P1 and P2 must not be rerun unless W1 reveals a product/source issue that changes their code-equivalence basis.

Do not run a full backend suite, broad browser suite, full chain, or broad regression; those remain deferred to ALV-N028.

## Required evidence package

If W1–W4 pass, provide an additive focused-proof package for `aa87419` that contains the exact commands, durations, results, database counts, browser artifacts, W1 interception diagnostics and authenticated-fetch result, the acceptance map, code-equivalence statement, preserved stopped-run evidence, retained findings/limitations, and the thirteen exact scan exceptions.

ALV-N026 remains `AWAITING_REVIEW` until that evidence receives the final independent ruling.
