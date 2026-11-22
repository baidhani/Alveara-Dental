# ALV-N026 R06: test results

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. **No broad regression was performed or is implied.**

## The R05 proof (approved by the R05 code review, stored in `c5b15fe`)
- **Tree:** `c5b15fe`. `git diff --stat aa87419..c5b15fe -- src tests scripts` is empty, so the code equals `aa87419`.
- **Artifacts:** `artifacts/r05-proof/`.

| Run | Command | Result | Time | Status in R06 |
|---|---|---|---|---|
| P1, P2 | - | 64/64, 119/119 on the R04 tree | - | carried forward (R05 review) |
| W1 | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.prescription-workflow.config.ts -OutputVariable PRESCRIPTION_WORKFLOW_E2E_OUT` | **15/15 passed** | 77 s | **carried forward**: its spec is unchanged since `aa87419` |
| W2 | `... -Config playwright.documents-library.config.ts -OutputVariable DOCUMENTS_LIBRARY_E2E_OUT` | 5 passed, **1 failed**, 7 did not run | 68 s | to rerun |
| W3, W4 | - | not run | - | to run after W2 |

**Test databases:** `AlveraTest_` 1 before and 1 after; `AlveraWalk_` 0 before and 0 after.

### W1: the server-PDF print is proven (review item 2)
From `artifacts/r05-proof/W1/prescription-workflow-evidence.json`:

| Click | Arrived as | Browser response (intercepted) | Playwright observed | Authenticated follow-up |
|---|---|---|---|---|
| Print (first) | new tab, then a download on the new tab | 200 `application/pdf`, Content-Length 33145 | 0 bytes | 33145 bytes, `%PDF-` |
| Print (again) | new tab, then a download on the new tab | 200 `application/pdf`, Content-Length 33144 | 0 bytes | 33144 bytes, `%PDF-` |
| Record copy (cancelled) | new tab, then a download on the new tab | 200 `application/pdf`, Content-Length 34822 | 0 bytes | valid (checked by `expectServerPdf`) |

- `window.print()` was called 0 times, and 3 issues were made.
- **The R04 stop was a Playwright observation limit, not a product defect.** The server sent the full PDF (its Content-Length matched the follow-up's byte count), but Playwright could not read the body the page had taken as a blob.
- The headless browser turns the new tab's PDF navigation into a download, which the R03 helper now hears.

### W2: the failure and its cause
- **Failing test:** `INACTIVATE: a reason is required; ...` (spec line 235). The call `rowOf("plain.pdf").getByRole("button", { name: "Inactivate plain.pdf" }).click()` waited until the 30 s test timeout.
- **The page snapshot shows** Open, Download, Details and **"More actions for plain.pdf"** in the row. Since S2 (F-171), Edit, Inactivate and Reactivate are under that menu.
- **The product behaves as designed.** The scenario was older than S2 and was not updated. My R02 stale-spec repair missed it, and W3's EDIT scenario has the same defect.

## R06 change (`d2241c8`, test-only)
- An `openMore(page, name)` helper, in both document specs, opens a row's More menu only if it is closed.
- It is used before Inactivate and Reactivate in documents-library (INACTIVATE) and before both Edit clicks in documents (EDIT).
- **Sweep of all four walkthrough specs:** no other row action moved. Preview and a PDF's Open stay in the row.

**Code equivalence:** `git diff --stat aa87419..d2241c8 -- src/Alveara.Api src/Alveara.Api.Tests scripts src/alveara-client/src` is empty. The only changes are the two document specs (+19 lines).

**Checks:** the specs type-check (`tsc --strict`, exit 0). No test has run on `d2241c8`.

## The focused proof to run after R06 is approved
1. **W2** (documents-library). If it fails, stop.
2. **W3** (documents), only if W2 passed.
3. **W4** (forms), only if W3 passed.

The commands are as in R02 `TEST_RESULTS.md`. Each run stops at a safe point if it exceeds five minutes. W1, P1 and P2 are carried forward.
