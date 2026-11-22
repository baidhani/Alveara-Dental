# ALV-N026 R02 — Independent Code Review Before Targeted Proof

**Decision:** APPROVED_FOR_TARGETED_PROOF  
**Reviewed package:** `Alveara_Handoff_ALV-N026_fa9a9d5_R02.zip`  
**Package SHA-256:** `D6132853D732819C01105A047B8168A21448DAFC3E2D06F74B2AEDD1A510E746`  
**Implementation SHA:** `fa9a9d5760b045da1d315b8919268e746735f80c`

This is an approval of the R02 code and narrowly selected proof plan only. It is **not** final story approval, does not set `COMPLETE`, and does not authorize a broad regression.

## Independent checks

- ZIP member hashes validate against `MANIFEST.json`; the stated implementation SHA exists locally.
- The F-107 test checks the rendered p2 PDF for the stored date-only value across ordinary, year-end, and pre-DST dates, and asserts the Chicago-offset previous day is absent.
- The server-PDF browser scenario starts from the current UI, observes issue creation and PDF retrieval, asserts a real PDF response, detects any application-page `window.print()` call, and checks that the app page and PDF bytes do not expose app addresses or raw error detail.
- The missing/damaged-file scenario exercises the final UI behavior: a missing or corrupt file offers no Open, Preview, or Download; an unchecked disappearance gives the safe in-app explanation; details remain available; files are restored and re-checked.
- The owner decision on signed forms is implemented at the server boundary. `ViewSignedForms`, not the Clinical/NonClinical category class, governs whether signed forms are listed. The controller passes the effective permission result to the library, so a permission override cannot be bypassed by the browser.
- Stale browser specifications were correctly updated for the S1 document-import policy and S6/S8 server-PDF flow.

## Authorized focused proof

Run exactly the P1, P2, W1, W2, W3, and W4 commands frozen in the R02 package on the implementation-equivalent tree rooted at `fa9a9d5`.

- P1 and P2 are targeted server groups.
- W1 through W4 are separate fresh-database real-browser walkthroughs.
- Each individual run is expected to remain at or below five minutes. If any individual run exceeds five minutes, stop it at a safe boundary and request owner direction before continuing that run.
- Client type/lint/build evidence already recorded for this code-review package need not be repeated unless a proof-run failure requires a source or test change.
- Do not run a full backend suite, full Vitest suite, broad mocked-browser suite, full chain, or Stage 2/broad-regression contract. Broad regression remains explicitly deferred to ALV-N028.

## Required evidence after the proof

If all six focused runs pass, create an additive immutable focused-proof/regression evidence package for this same R02 implementation SHA. It must contain:

1. Exact commands, duration, final SHA, pass/fail/skip counts, and test-database counts before and after.
2. The four browser outputs/screenshots and the server result artifacts.
3. An acceptance map showing R01 items 1–4 as proven, while retaining U-018, U-019, U-021 and every declared non-blocking limitation.
4. A code-equivalence check showing that the proof tree differs from `fa9a9d5` only by additive review/evidence/status material.
5. The narrowly scoped secret-scan allowlist: only the two placeholder public-key-header lines in `BackupAlertTests.cs` and the `currentPassword` parameter-name line in `backupApi.ts`. Any other scan match remains a failure.

If any focused proof fails or requires a code/test change, stop and return a new handoff attempt for review before continuing. Do not treat a failure as an environmental result without preserving the evidence and diagnosis.

## Remaining state

ALV-N026 remains `AWAITING_REVIEW`; it is not release-certified. A final independent decision follows only after the targeted-proof package is reviewed.
