# ALV-N026 R03: test results

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. **No broad regression was performed or is implied.**

## The R02 targeted proof, stopped at W1 (2026-11-22)
- **Authority:** the R02 code review (`.alveara/reviews/ALV-N026/R02-code-review.md`, APPROVED_FOR_TARGETED_PROOF).
- **Tree:** `eac4407`. `git diff --stat fa9a9d5..eac4407 -- src tests scripts` is empty, so the code equals `fa9a9d5`.
- **Artifacts:** `artifacts/r02-proof-stopped/` (logs, trx, W1 output, screenshots and error context).

| Run | Command (as frozen in R02 `TEST_RESULTS.md`) | Result | Time |
|---|---|---|---|
| P1 | prescription and PDF classes | **64/64** passed | 88 s |
| P2 | forms, signed-form snapshot and document classes | **119/119** passed | 149 s |
| W1 | `Invoke-RealBackendWalkthrough.ps1 -Config playwright.prescription-workflow.config.ts -OutputVariable PRESCRIPTION_WORKFLOW_E2E_OUT` | **7 passed, 1 failed, 7 did not run** (serial mode stops at a failure) | 50 s |
| W2-W4 | - | **not run**: the review says to stop after a failure that needs a test change | - |

Test databases: `AlveraTest_` 1 before and 1 after (the known leftover); `AlveraWalk_` 0 before and 0 after (the runner dropped its own).

### The W1 failure and its diagnosis
- **Failing test:** `CANCELLING needs a reason ...`, at the record copy of the cancelled prescription.
- **What the error context shows:**
  - `POST .../issues` was answered, and `GET /api/prescription-issues/{id}/pdf` was answered with the PDF (the helper got past both awaits);
  - the poll "the PDF reached the user as a tab or a download" then saw 0 after 5 s;
  - the page snapshot still shows the application page, so the app was not navigated away.
- **Diagnosis (a hypothesis; the R03 run is built to confirm or refute it):**
  - the product calls `window.open` at the click, then sends that tab to the PDF's object URL;
  - headless Chromium cannot display a PDF, so that navigation can become a **download on the new tab**, and the tab can close;
  - R02's helper counted only tabs still open at poll time and listened for downloads only on the application page, so it would miss both.
- **Classification:** a defect in my test helper, not in the product. No product code changes in R03.
- **Not an environmental result:** the evidence and this diagnosis are kept.

## R03 change (test-only, `a018c7e`)
`pdfFromClick` in `prescription-workflow-real-backend.spec.ts`:
- counts every tab opened during the click, even one that closes;
- listens for downloads on each new tab as well as the application page;
- waits 500 ms after the first sign so that a tab's download is heard;
- records `arrivals` (new tab / download on the new tab / download on the application page) in the evidence JSON.

The assertions are unchanged:
- POST issue then GET PDF;
- `application/pdf` starting with `%PDF-`;
- no `window.print()` call;
- no app address or raw detail in the PDF.

**Checks:** the three changed specs type-check (`tsc --strict`, exit 0). No test has run on `a018c7e`.

## The focused proof to run after R03 is approved
This is the same command set as R02, on the R03 tree. The proof restarts from the beginning, because the review asks for a proof tree that differs from the reviewed implementation only by review, evidence and status material.

| # | Proof | Command | Estimate |
|---|---|---|---|
| P1 | prescription and PDF classes | as in R02 | about 1.5 min |
| P2 | forms, signed-form and document classes | as in R02 | about 2.5 min |
| W1 | server-PDF print | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.prescription-workflow.config.ts -OutputVariable PRESCRIPTION_WORKFLOW_E2E_OUT` | about 1-2 min |
| W2 | missing and damaged files | `... -Config playwright.documents-library.config.ts -OutputVariable DOCUMENTS_LIBRARY_E2E_OUT` | about 2-3 min |
| W3 | signed form in the library and PD-24 | `... -Config playwright.documents.config.ts -OutputVariable DOCUMENTS_E2E_OUT` | about 2-3 min |
| W4 | signed-form integrity and roles | `... -Config playwright.forms.config.ts -OutputVariable FORMS_E2E_OUT` | about 2-3 min |

Each run is stopped at a safe point if it exceeds five minutes. A failure stops the proof again, with its evidence kept.
