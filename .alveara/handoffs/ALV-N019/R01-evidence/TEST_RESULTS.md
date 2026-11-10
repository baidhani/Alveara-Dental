# ALV-N019 R01: test results (Stage 1)

Swept tree: `448d2b3` (implementation commit `6b083cd` adds one test file, `tests/stabilization/docs-hygiene.test.mjs`, 4 tests, run green on its own). Raw logs: `artifacts/sweep/`. Machine idle during the sweep; ports 5072, 5173 and 4173 free.

## Commands and results
| # | Command | Result |
|---|---|---|
| 1 | `node --test tests/*.test.mjs tests/stabilization/*.test.mjs` | exit 0; 99 pass, 0 fail, 1 skipped (the real-log test; with `DISPOSITION_RAW_LOG` set it passes, 17 of 17 in that file, and the check prints "OK all 385 raw ids have a disposition (388 register rows)"). With `docs-hygiene.test.mjs` added in `6b083cd`: +4 pass. |
| 2 | `npx tsc -b` (src/alveara-client) | exit 0 |
| 3 | `npx oxlint` | exit 0 (warnings only, all in files this story did not touch) |
| 4 | `npx vitest run` | exit 0; 120 files, **1879 tests passed** |
| 5 | `npm run build` | exit 0 |
| 6 | `npx playwright test` (mocked suite) | **136 passed, 2 skipped, 2 failed.** The 2 failures are `architecture-map.spec.ts:69` (desktop, tablet): the ledger has ALV-N019..N028 from the owner's uncommitted `EXECUTION_STATUS.json`, the catalog does not. Run against the COMMITTED status file (working copy saved and restored, SHA-256 identical): `architecture-map.spec.ts` 38 passed (`artifacts/sweep/11-architecture-map-on-committed-status.log`). |
| 7 | `dotnet build` (src/Alveara.Api.Tests) | exit 0 |
| 8 | `dotnet test --filter "FullyQualifiedName~ApiTests"` (every `*ApiTests` class) | exit 0; **964 passed, 0 failed, 0 skipped** (19 m 55 s). NOT the full backend suite (that is Stage 2). |
| 9 | Real-backend walkthroughs via `scripts/Invoke-RealBackendWalkthrough.ps1` | auth-real-backend **12 passed**; patient-portability **7 passed**; patient-merge **9 passed**; bulk-import **13 passed**; patient-export-package **1 failed, 4 passed** in the sweep rerun (browser closed at a `fill`), then **5 of 5 reruns passed 10 of 10**. |
| 10 | `scripts/Invoke-AccumulatedChain.ps1` (stage 0) | exit 0; six records PASS (S-09a..d, E10, CLEAN); database dropped, run folder removed. Evidence: `artifacts/chain/stage0-evidence.json`. |

## Earlier focused runs on the same work (before the sweep)
C# focused: 261 passed (backup, export, import, readable record, package, portability), 88 passed (merge, import dry-run), 22 passed (`ImportCommitTests`, `PatientMergeSqlBoundaryTests`). Client unit tests of the touched areas: 150 passed.

## Known gaps in this sweep (disclosed)
- **The first sweep launch** ran steps 09 and 10 (the five walkthroughs and the chain) with an unescaped `[ ]` path, so they failed instantly; the summary shows "exit 0 in 0 s" for them. Those logs were overwritten by the correct rerun at 10:38 to 10:43 (`artifacts/sweep/SUMMARY.txt` keeps both timelines). Rows 9 and 10 above are the rerun.
- The full backend suite, the all-tests Stage 2 contract, and the other ~13 real-backend walkthroughs were NOT run.
- `auth-real-backend` flake analysis: `artifacts/flake-analysis/` (clean checkout `3d70b69`: 6 of 7 full runs passed; this tree: 2 of 4 failed then the rerun passed).

## Leak counts
Before: 0 databases other than AlveraDev; Test-Data 3,931 folders, 4,973 files. After the rerun: 0 databases; 3,931 folders; 4,978 files. The 5 added files are the chain's kept evidence folder (`Test-Data\Temp\chain-output\<run id>\`).

## Negative controls
Seven controls, each caught and each file restored with an identical SHA-256: `artifacts/sweep/negative-controls.txt`. F-002 has none (behaviour-neutral guard).

## Not run, stated plainly
Full `dotnet test` of the whole backend project; the walkthroughs not listed above; any test on a machine other than the development machine.
