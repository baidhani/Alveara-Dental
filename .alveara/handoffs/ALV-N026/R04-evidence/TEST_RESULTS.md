# ALV-N026 R04: test results

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. **No broad regression was performed or is implied.**

## Two stopped proof runs, kept separately
| Attempt | Tree (code) | P1 | P2 | W1 | W2-W4 | Evidence |
|---|---|---|---|---|---|---|
| R02 proof | `eac4407` (= `fa9a9d5`) | 64/64, 88 s | 119/119, 149 s | 7 passed, 1 failed, 50 s | not run | `.alveara/handoffs/ALV-N026/R03-evidence/artifacts/r02-proof-stopped/` (packaged in R03) |
| R03 proof (restart) | `e9e8616` (= `a018c7e`) | 64/64, 96 s | 119/119, 148 s | **7 passed, 1 failed**, 48 s | not run | `artifacts/r03-proof-stopped/` |

**Test databases** (both runs): `AlveraTest_` 1 before and 1 after (the known leftover); `AlveraWalk_` 0 before and 0 after (the runner drops its own).

**Code equivalence:** `git diff --stat a018c7e..e9e8616 -- src tests scripts` was empty for the R03 run.

### R03's W1 failure and the corrected diagnosis
- **Failing test:** `CANCELLING needs a reason ...` (spec line 368).
- **What the corrected helper showed:** `expectServerPdf`, `expect(result.status).toBe(200)`, received **503** from `GET /api/prescription-issues/{id}/pdf`.
- **Cause, confirmed in code:**
  - `scripts/Invoke-RealBackendWalkthrough.ps1` started the API without `Pdf__EngineDirectory` and `Pdf__FontDirectory`;
  - the engine is provisioned at `C:\.alvera-data\Test-Data\PdfEngine` (`typst.exe`, `fonts\`);
  - the API tests set those values (`AlveraTestEnvironment.cs`), and so does the dev API (`Start-DevApi.ps1`);
  - the walkthrough runner was not updated when S4 added the engine, and no PDF walkthrough ran in R01 to catch it.
- **The R02 failure was almost certainly the same 503.** On a refused PDF the app closes the tab it opened and shows the refusal (`openPdf`), so R02's helper saw neither a tab nor a download. **R03's diagnosis, a helper that missed a tab-download, was wrong.** The R03 helper change stays: it is correct and records how the PDF arrives.
- **Product behaviour on the 503 was correct:** the refusal was shown, the tab closed, and nothing printed.

## R04 change (`884a936`, shared infrastructure, additive)
`scripts/Invoke-RealBackendWalkthrough.ps1`:
- `$pdfEngineDir = Test-Data\PdfEngine`;
- it stops at once if `typst.exe` is missing there;
- it passes `Pdf__EngineDirectory` and `Pdf__FontDirectory` to the API process.

No product or test code changed. **Check:** PowerShell parser, 0 errors. No test has run on `884a936`.

## The focused proof to restart after R04 is approved
The commands are unchanged from R02 `TEST_RESULTS.md`: P1, P2, then W1, W2, W3 and W4, each with the runner, on the R04 tree. Each run stops at a safe point if it exceeds five minutes, and a failure stops the proof with its evidence kept.

All four walkthroughs now get the engine. Only W1 renders PDFs, but W2 and W3 open documents, and the change is harmless for them.
