# ALV-N026 R02: test results (code-review stage: no tests run yet)

Route: focused proof only. `fullRegression: DEFERRED_TO_ALV-N028`. **No broad regression was performed or is implied.**

**Owner instruction (2026-11-22):** the R02 test changes are reviewed first. After approval, the four walkthroughs run as the focused proof, each within the five-minute rule. Until then **no test has run on `fa9a9d5`**, and no result below is claimed.

## Checks run (not tests)
| Check | Result |
|---|---|
| `dotnet build src/Alveara.Api.Tests` (API and tests) | 0 errors |
| `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| `npx tsc --noEmit -p tsconfig.node.json` | exit 0 |
| The three changed specs: `npx tsc --noEmit --ignoreConfig --skipLibCheck --strict --target es2022 --module esnext --moduleResolution bundler --types node e2e/prescription-workflow-real-backend.spec.ts e2e/documents-library-real-backend.spec.ts e2e/documents-real-backend.spec.ts` (no tsconfig covers `e2e/`) | exit 0 |
| `npm run lint` | warnings only, as before (no e2e files are linted) |

## The focused proof to run after approval
Each command runs on the final R02 tree. Each walkthrough uses a fresh database through `scripts/Invoke-RealBackendWalkthrough.ps1`, launched from PowerShell with `Set-Location -LiteralPath` to the repository. The times are estimates; the actual time of each run will be recorded. A run that exceeds five minutes is stopped at a safe point and the owner is asked.

| # | Proof | Command | Estimate |
|---|---|---|---|
| P1 | F-107 and the prescription PDF classes | `dotnet test src/Alveara.Api.Tests --filter "FullyQualifiedName~Tests.PrescriptionWorkflowApiTests.\|FullyQualifiedName~Tests.PrescriptionPdfTextTests.\|FullyQualifiedName~Tests.PrescriptionIssueOwnershipTests.\|FullyQualifiedName~Tests.PdfEngineTests."` | about 2 min |
| P2 | Signed forms and the document classes the R02 server change touches | `dotnet test ... --filter "FullyQualifiedName~Tests.FormsApiTests.\|FullyQualifiedName~Tests.SignedFormSnapshotTests.\|FullyQualifiedName~Tests.DocumentLibraryApiTests.\|FullyQualifiedName~Tests.DocumentClassificationAccessApiTests.\|FullyQualifiedName~Tests.DocumentApiTests."` | about 4-5 min |
| W1 | Browser: server-PDF print | `scripts/Invoke-RealBackendWalkthrough.ps1 -Config playwright.prescription-workflow.config.ts -OutputVariable PRESCRIPTION_WORKFLOW_E2E_OUT` | about 2-3 min |
| W2 | Browser: missing and damaged files | `... -Config playwright.documents-library.config.ts -OutputVariable DOCUMENTS_LIBRARY_E2E_OUT` | about 2-3 min |
| W3 | Browser: signed form in the library and PD-24 | `... -Config playwright.documents.config.ts -OutputVariable DOCUMENTS_E2E_OUT` | about 2-3 min |
| W4 | Browser: signed-form integrity and roles | `... -Config playwright.forms.config.ts -OutputVariable FORMS_E2E_OUT` (unchanged spec, re-run on the final tree) | about 2-3 min |

Client gates are not re-run, because no client source changed in R02, only specs. `node --test` is not re-run either: no guard file changed.

**Deliberately excluded:** the full backend suite, full vitest, the mocked Playwright suite, other walkthroughs and the chain (ALV-N028).
