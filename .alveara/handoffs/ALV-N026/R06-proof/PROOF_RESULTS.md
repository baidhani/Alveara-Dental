# ALV-N026 R06: focused-proof evidence (additive, for implementation `d2241c8`)

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. **No broad regression was performed or implied.** Session `CC-20261115-m7x2`, 2026-11-22.

## Authority and tree
- **Authority:** the R06 code review, `.alveara/reviews/ALV-N026/R06-code-review.md` (APPROVED_FOR_TARGETED_PROOF), stored in `81a5078`.
- **Proof tree:** `81a5078`.
- **Code equivalence:**
  - `git diff --stat d2241c8..81a5078 -- src tests scripts` is **empty**, so the proof tree's code equals `d2241c8`;
  - from the trees where earlier runs passed: `git diff --stat 884a936..d2241c8 -- src/Alveara.Api src/Alveara.Api.Tests scripts src/alveara-client/src` is **empty**;
  - since `884a936`, the only changes are the e2e specs: R05 changed the prescription-workflow spec, and R06 the two document specs. P1 and P2 run none of them, and W1's spec is unchanged since `aa87419`.

## Results

| Run | Command | Tree run on | Result | Duration | Status |
|---|---|---|---|---|---|
| Preflight | `typst.exe` and `fonts\` (NotoSans-Regular, NotoSans-Bold) present in `C:\.alvera-data\Test-Data\PdfEngine` | `85815a3` (= `884a936`) | present | - | carried forward |
| P1 | `dotnet test src/Alveara.Api.Tests --filter "...PrescriptionWorkflowApiTests.\|...PrescriptionPdfTextTests.\|...PrescriptionIssueOwnershipTests.\|...PdfEngineTests."` | `85815a3` (= `884a936`) | **64/64 passed**, 0 failed, 0 skipped | 98 s | carried forward (R05 and R06 reviews); `R05` package `artifacts/r04-proof/P1*` |
| P2 | `dotnet test ... --filter "...FormsApiTests.\|...SignedFormSnapshotTests.\|...DocumentLibraryApiTests.\|...DocumentClassificationAccessApiTests.\|...DocumentApiTests."` | `85815a3` | **119/119 passed**, 0 failed, 0 skipped | 180 s | carried forward; `artifacts/r04-proof/P2*` |
| W1 | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.prescription-workflow.config.ts -OutputVariable PRESCRIPTION_WORKFLOW_E2E_OUT` | `c5b15fe` (= `aa87419`) | **15/15 passed** | 77 s | carried forward (R06 review); `R06-evidence/artifacts/r05-proof/W1/` |
| W2 | `... -Config playwright.documents-library.config.ts -OutputVariable DOCUMENTS_LIBRARY_E2E_OUT` | `81a5078` (= `d2241c8`) | **13/13 passed** | 75 s | `artifacts/W2/` |
| W3 | `... -Config playwright.documents.config.ts -OutputVariable DOCUMENTS_E2E_OUT` | `81a5078` | **14/14 passed** | 43 s | `artifacts/W3/` |
| W4 | `... -Config playwright.forms.config.ts -OutputVariable FORMS_E2E_OUT` | `81a5078` | **10/10 passed** | 52 s | `artifacts/W4/` |

Each walkthrough ran on a fresh database, which the runner created and dropped. Each ran from PowerShell with `Set-Location -LiteralPath` to the repository. Every run stayed under five minutes.

**Test databases, for R06's W2-W4:** `AlveraTest_` 1 before and 1 after (the known leftover from earlier stories); `AlveraWalk_` 0 before and 0 after. Every earlier run had the same counts.

### W1: interception diagnostics and the authenticated follow-up
Source: `R06-evidence/artifacts/r05-proof/W1/prescription-workflow-evidence.json`.

| Click | Arrived as | Browser response (intercepted) | Playwright observed | Authenticated follow-up |
|---|---|---|---|---|
| Print | new tab, then download on the new tab | 200 `application/pdf`, Content-Length 33145 | 0 bytes | 33145 bytes, `%PDF-` |
| Print again | new tab, then download on the new tab | 200 `application/pdf`, Content-Length 33144 | 0 bytes | 33144 bytes, `%PDF-` |
| Record copy (cancelled) | new tab, then download on the new tab | 200 `application/pdf`, Content-Length 34822 | 0 bytes | valid (`expectServerPdf`) |

- `window.print()` was called 0 times, and 3 issues were made.
- The empty intercepted body is a **browser-test observation limit**, not a product failure: the server's Content-Length equals the follow-up's bytes.

### W2-W4: what was shown (from their evidence JSON)
- **W2** (`documents-library-evidence.json`, `missingOrDamagedAsSeen`): unchecked missing file explained on Open: true. Checked missing file, no actions: true. Damaged file, no actions: true. Restored: true. No storage path or raw detail was on the page in any case (assertions). Screenshots `07b`, `07c`, `07d`.
- **W3** (`documents-evidence.json`):
  - `signedForm`: listed read-only; hash unchanged after a template edit; the front desk with ViewSignedForms sees it listed; with it revoked, hidden and the form 403; reverted, listed again. Screenshots `05`, `05b`.
  - `permissions`: the dentist gets the import form but cannot manage; the front desk lists no clinical document (0), gets 404 on its content and 403 on import; the office manager gets 403 on settings; a missing CSRF token gets 400.
- **W4** (`forms-e2e.json`): signed-form templates, completion, signing, immutability after template versions, the interrupted signature, void and roles all passed (10/10), on the final tree. Screenshots `01` to `08`.

## Acceptance map: review R01's obligations
| Review item | Plan item 63 criterion | Proof | Status |
|---|---|---|---|
| 1. F-107: rendered p2 PDF date of birth (UTC-slip case) | 2: print output accurate | P1 (`PrescriptionWorkflowApiTests.The_pdf_prints_the_stored_date_of_birth_as_the_same_calendar_date`, 3 dates) | **PROVEN** |
| 2. Real-browser server-PDF print | 2: print output accurate and privacy-safe | W1 | **PROVEN** |
| 3. Real-browser missing or damaged file | 1: document integrity; 4: missing-file paths communicate safely | W2 | **PROVEN** |
| 4. Signed-form integrity and visibility under PD-24 (owner decision: ViewSignedForms) | 1: signed forms keep integrity and appropriate visibility | P2 (FormsApiTests override case, SignedFormSnapshotTests), W3, W4 | **PROVEN** |

## Preserved stopped-run evidence (separate from this successful proof)
| Run | Tree | Outcome | Where |
|---|---|---|---|
| R02 proof | `eac4407` (= `fa9a9d5`) | P1 and P2 passed, W1 stopped (503: no PDF engine in the walkthrough API) | R03 package: `artifacts/r02-proof-stopped/` |
| R03 proof | `e9e8616` (= `a018c7e`) | P1 and P2 passed, W1 stopped (503) | R04 package: `artifacts/r03-proof-stopped/` |
| R04 proof | `85815a3` (= `884a936`) | preflight, P1 and P2 passed (carried forward), W1 stopped (intercepted body empty) | R05 package: `artifacts/r04-proof/` |
| R05 proof | `c5b15fe` (= `aa87419`) | W1 passed (carried forward), W2 stopped (stale INACTIVATE scenario) | R06 package: `R06-evidence/artifacts/r05-proof/` |

## Retained findings and limitations (none closed here)
- U-018 (flaky BillingStatementsPanel test), U-019 (the readable export, and so the record copy, lists clinical document names), U-021 (the plan copy swallows a practice-header fetch error): open in their register.
- Untested:
  - the Down paths of the six migrations;
  - the route-level PDF 503 (proven at engine level; the R03 run also showed it end to end);
  - long backup and outage classes;
  - Q-5cx's root cause (not confirmed);
  - the prescriber-details screen (not built);
  - signed forms beyond the scenarios above.
- The R01 package's other disclosures stand. Not release-certified.

## Secret scan
The thirteen exact exceptions, each a package path, line and text, are listed with reasons in the package's `evidence/SECRET_SCAN.md`:
- 3 lines in the code diff;
- 4 in R02's, 3 in R03's and 3 in R04's stopped-run `error-context.md`.

No general pattern exception is allowed. The R05 and R06 runs' artifacts add no match.
