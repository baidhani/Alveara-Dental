# ALV-N019 R02: test results (Stage 1)

Swept tree: `45706516294e7f66ca25da8e343a739c39ed2e2d` (R02 implementation commit `54806a3` plus the data-only catalog commit). Raw logs: `artifacts/sweep/`. Machine idle; ports 5072, 5173 and 4173 free. The R01 results are in the R01 package and are not repeated as R02 evidence.

## Commands and results
| # | Command | Result |
|---|---|---|
| 1 | `node --test tests/*.test.mjs tests/stabilization/*.test.mjs` | exit 0; **126 tests, 125 passed, 0 failed, 1 skipped** (the real-log test, which needs `DISPOSITION_RAW_LOG`). This run includes the tests that need SQL Server (runner resilience) because the machine had it. |
| 2 | `npx tsc -b` (src/alveara-client) | exit 0 |
| 3 | `npx oxlint` | exit 0 (warnings only, in files this story did not touch) |
| 4 | `npx vitest run` | exit 0; 120 files, **1,879 tests passed** |
| 5 | `npm run build` | exit 0 |
| 6 | `npx playwright test` (mocked suite) | exit 0; **138 passed, 2 skipped, 0 failed** (the two R01 failures in `architecture-map.spec.ts` are gone: the catalog now describes the ten stabilization stories) |
| 7 | `dotnet build` (src/Alveara.Api.Tests, which builds the new chain host) | exit 0 |
| 8 | Focused backend: `ChainClockSeamTests`, `PatientMergeHostileInputTests`, `PatientMergeSqlBoundaryTests`, `ImportCommitTests`, `BackupRecoveryHardeningTests`, `PatientMerge*`, `PatientUnmerge*` | exit 0; **197 passed, 0 failed, 0 skipped** (10 m 9 s) |
| 9 | Every `*ApiTests` class | exit 0; **964 passed, 0 failed, 0 skipped** (19 m 55 s). NOT the full backend suite (Stage 2). |
| 10 | Real-backend walkthroughs via `scripts/Invoke-RealBackendWalkthrough.ps1` | auth-real-backend **12 passed**; patient-portability **7**; patient-export-package **10**; patient-merge **9**; bulk-import **13**; all exit 0 on the first run. |
| 11 | `scripts/Invoke-AccumulatedChain.ps1 -ClockMode injected` | exit 0; **9 PASS** (S-09a-d, C-01, B-01, B-02, E10, CLEAN), run `cba2d2fc`, `clockMode` `injected` on every record |
| 12 | `scripts/Invoke-AccumulatedChain.ps1 -ClockMode real` | exit 0; **9 PASS**, run `cbb98644`, `clockMode` `real` on every record |

## New tests in R02 (all passed on first or second run, see defects in R02.md)
| Test file | Tests | Result |
|---|---|---|
| `src/Alveara.Api.Tests/PatientMergeHostileInputTests.cs` | 67 (7 hostile strings through each merge and undo input, plus refusals) | 67 passed |
| `src/Alveara.Api.Tests/ChainClockSeamTests.cs` | 6 | 6 passed |
| `tests/stabilization/chain-runner-resilience.test.mjs` | 8 (5 need no SQL; 3 need SQL Server; the kill-mid-run test also needs the built API and free ports) | 8 passed |
| `tests/stabilization/e10-invariant.test.mjs` | 9 | 9 passed |
| `tests/stabilization/production-composition.test.mjs` | 4 | 4 passed |
| `src/alveara-client/chain/stage-0.spec.ts` steps C-01, B-01, B-02 | 3 browser steps | PASS in both clock modes |

## Negative controls
Seven controls, each caught, each file restored with an identical SHA-256: `artifacts/sweep/negative-controls.txt`. D1 clock allows a backwards step; D2 chain host stops replacing the real clock; D3 `Program.cs` gains a chain reference; D4 E10 stops comparing row counts; D5 runner no longer refuses an existing database; D6 dirty-tree label removed; D7 merge reason stops refusing control characters (shows the hostile-input tests can fail). The 67 hostile-input tests passed without any product change, so D7 is the evidence that they are not vacuous. F-002 has no control (behaviour-neutral guard).

## Leak counts
Before: 0 databases other than AlveraDev; Test-Data 3,931 folders, 5,031 files. After: 0 databases; 3,931 folders; 5,051 files. The 20 added files are the kept chain evidence folders (`Test-Data\Temp\chain-output\<run id>\`) of the two chain runs. The four generated `*-e2e-out` folders my walkthroughs left in `src/alveara-client` were deleted afterwards.

## Not run, stated plainly
Full `dotnet test` of the whole backend project; the walkthroughs not listed above; any test on another machine; the frozen Stage 2 contract (not frozen, per the review).
