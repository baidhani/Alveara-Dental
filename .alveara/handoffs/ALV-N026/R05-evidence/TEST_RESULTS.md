# ALV-N026 R05: test results

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. **No broad regression was performed or is implied.**

## The R04 proof (approved by the R04 code review, stored in `85815a3`)
- **Tree:** `85815a3`. `git diff --stat 884a936..85815a3 -- src tests scripts` is empty, so the code equals `884a936`.
- **Artifacts:** `artifacts/r04-proof/`.

| Run | Result | Time | Status in R05 |
|---|---|---|---|
| Preflight | `C:\.alvera-data\Test-Data\PdfEngine\typst.exe` present; `fonts\` holds NotoSans-Regular.ttf and NotoSans-Bold.ttf | - | done |
| P1: prescription and PDF classes | **64/64** passed | 98 s | **carried forward** (owner decision 2026-11-22) |
| P2: forms, signed-form snapshot and document classes | **119/119** passed | 180 s | **carried forward** |
| W1: prescription-workflow walkthrough | 7 passed, **1 failed**, 7 did not run | 57 s | to rerun |
| W2-W4 | not run | - | to run if W1 passes |

**Test databases:** `AlveraTest_` 1 before and 1 after; `AlveraWalk_` 0 before and 0 after.

### The R04 W1 failure
- **Failing test:** `CANCELLING ...`, at the record copy.
- **What passed:**
  - the issue was 201;
  - the browser-side PDF request was **200** with `Content-Type: application/pdf` (R04's runner fix worked, and the 503 is gone).
- **What failed:** `expect(result.bytes.subarray(0, 5)).toBe("%PDF-")` received `""`. The body Playwright intercepted from the browser's own response was **empty**.
- **Undetermined:** whether that is a Playwright observation limit (the page takes the body as a blob and hands it to the new tab, possibly through the Vite proxy) or a product defect (a real host sending an empty 200 PDF). R05 is built to tell the two apart.

## R05 change (`aa87419`, test-only)
`pdfFromClick` / `expectServerPdf` in `prescription-workflow-real-backend.spec.ts`:
- **Still required:**
  - the click issues the PDF (201);
  - the browser-side PDF request is 200 `application/pdf`;
  - the PDF reaches the user as a tab or a download;
  - `window.print()` is never called.
- **Diagnostics only:** the intercepted response's status, Content-Type, Content-Length (if sent) and the byte count Playwright observed, written to the evidence JSON (`intercepted`).
- **Validated:**
  - one follow-up `GET /api/prescription-issues/{id}/pdf` of the same, already-issued PDF through the same signed-in session must be 200, `application/pdf`, non-empty, start with `%PDF-`, and contain no app address or raw server detail;
  - these assertions are labelled **PRODUCT**, so an empty or non-PDF follow-up is a product failure and stops the proof.
- **If only the intercepted body is empty** and the follow-up is valid, that is recorded as a browser-test observation limit, not a product failure.

## Code equivalence for carrying P1 and P2 forward
`git diff --stat 884a936..aa87419 -- src/Alveara.Api src/Alveara.Api.Tests scripts src/alveara-client/src` is **empty**: no API, migration, product, server-test, script or client-source change. The only change in `src`, `tests` and `scripts` is `src/alveara-client/e2e/prescription-workflow-real-backend.spec.ts` (+25 lines, -6). P1 and P2 do not touch that file.

## The focused proof to run after R05 is approved
| Order | Run | Command | Rule |
|---|---|---|---|
| 1 | W1 | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.prescription-workflow.config.ts -OutputVariable PRESCRIPTION_WORKFLOW_E2E_OUT` | if it fails, stop |
| 2 | W2 | `... -Config playwright.documents-library.config.ts -OutputVariable DOCUMENTS_LIBRARY_E2E_OUT` | only if W1 passed |
| 3 | W3 | `... -Config playwright.documents.config.ts -OutputVariable DOCUMENTS_E2E_OUT` | only if W2 passed |
| 4 | W4 | `... -Config playwright.forms.config.ts -OutputVariable FORMS_E2E_OUT` | only if W3 passed |

Each run stops at a safe point if it exceeds five minutes. P1 and P2 are not rerun.
