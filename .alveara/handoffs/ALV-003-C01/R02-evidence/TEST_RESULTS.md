# ALV-003-C01 R02 — Test results

Run on the unchanged product tree (implementation commit `fdafb00`; `git diff fdafb00 HEAD -- . ':!.alveara'` empty), baseline HEAD `cd6518a`.

| Suite | Result | Output |
|---|---|---|
| Backend `dotnet test` (real SQL Server LocalDB) | **492 of 492**, 0 failed, 0 skipped, 24 m | `artifacts/run-output-backend-r02.txt` |
| Frontend `npx vitest run` | **319 of 319** (37 files) | `artifacts/run-output-vitest-r02.txt` |
| Repository checks `node --test tests/*.test.mjs` | **7 of 7** | `artifacts/run-output-repo-checks-r02.txt` |

Total re-run in R02: 818.

## Not re-run in R02
Mocked Playwright (40), ALV-003-C01 real-backend walkthrough (14), STORY-003 walkthrough (7), auth real-backend (12), Gate A route scans (3). They ran against a byte-identical tree in R01; their results and artifacts are R01 evidence (`../R01-evidence/`), carried forward, not R02 runs.

## Run note
A first backend attempt failed at fixture setup because SQL Server LocalDB had stopped (connection errors, no test logic failing). LocalDB was restarted and the full suite re-run from scratch; only the second, complete run is counted.
The reviewer's own focused rerun in the R01 review did not complete and is not counted either.
